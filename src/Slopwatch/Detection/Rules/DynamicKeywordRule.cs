using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Slopwatch.Suppression;

namespace Slopwatch.Detection.Rules;

/// <summary>
/// Bans the <c>dynamic</c> keyword outright.
/// Rule ID: SW021
/// </summary>
/// <remarks>
/// <para>
/// <c>dynamic</c> is a contextual keyword: it has no <c>SyntaxKind</c> of its own and parses
/// as a plain <see cref="IdentifierNameSyntax"/>. Detection therefore has to decide, for each
/// identifier named <c>dynamic</c>, whether it occupies a type position.
/// </para>
/// <para>
/// <strong>This rule fails closed.</strong> Rather than enumerating the type positions that
/// should be flagged (a whitelist that is provably incomplete — it missed lambda return types,
/// LINQ query clauses, and <c>__refvalue</c>), it flags every identifier named <c>dynamic</c>
/// and then excludes only the roles where the identifier is provably <em>not</em> a type.
/// A new C# syntax form that nobody anticipated is flagged by default rather than silently
/// allowed through.
/// </para>
/// <para>
/// A semantic model would be the ideal way to resolve this, but slopwatch analyzes each file
/// in isolation (<see cref="Analysis.FileAnalyzer"/> parses a single <c>SyntaxTree</c> with no
/// compilation or project references). Building a per-file compilation produces a <em>worse</em>
/// result: a user-defined <c>class dynamic</c> declared in a file outside the compilation
/// resolves to <c>TypeKind.Dynamic</c>, introducing a false positive the syntactic approach
/// does not have. So the rule stays syntactic and fails closed.
/// </para>
/// <para>
/// <strong>Deliberate exclusions:</strong>
/// </para>
/// <list type="bullet">
/// <item><description>
/// <c>nameof(dynamic)</c> and attribute arguments produce a string or metadata rather than a
/// dynamic-typed slot, so they have no runtime effect.
/// </description></item>
/// <item><description>
/// A user-defined type literally named <c>dynamic</c> is flagged. This is an accepted false
/// positive: without a semantic model the rule cannot distinguish it, and banning the name is
/// consistent with the rule's intent.
/// </description></item>
/// <item><description>
/// <c>System.Dynamic</c> and <c>ExpandoObject</c> are not flagged. The ban is keyword-shaped,
/// not behavior-shaped.
/// </description></item>
/// </list>
/// <para>
/// This rule can be suppressed with <see cref="SlopwatchSuppressAttribute"/> for the rare
/// legitimate case (COM interop, dynamic proxy boundaries). Suppression applies to the nearest
/// enclosing declaration, so a class-level attribute does not cover its members.
/// </para>
/// </remarks>
public sealed class DynamicKeywordRule : IDetectionRule
{
    /// <inheritdoc />
    public string RuleId => "SW021";

    /// <inheritdoc />
    public string Name => "Dynamic Keyword Ban";

    /// <inheritdoc />
    public string Description =>
        "Detects any use of the 'dynamic' keyword. Dynamic bypasses compile-time type checking, " +
        "defeats static analysis, breaks trimming/AOT, and is a common LLM escape hatch for " +
        "silencing type errors instead of fixing them.";

    /// <inheritdoc />
    public DetectionSeverity DefaultSeverity => DetectionSeverity.Error;

    /// <inheritdoc />
    public IReadOnlyList<string> ApplicableFilePatterns => new[] { "*.cs" };

    /// <inheritdoc />
    public async IAsyncEnumerable<DetectionResult> AnalyzeAsync(
        DetectionContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (context.SyntaxTree is null)
            yield break;

        var root = await context.SyntaxTree.GetRootAsync(cancellationToken);

        // Create suppression checker
        var suppressionChecker = await SuppressionChecker.CreateAsync(context, cancellationToken);

        // Collect candidates first so ordinals can be assigned before any line filtering.
        // Ordinals must be stable across full-file and diff-scoped analysis, otherwise the
        // same token gets a different baseline hash depending on which mode produced it.
        var occurrences = new List<(IdentifierNameSyntax Node, SyntaxNode Scope)>();

        foreach (var identifier in root.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (identifier.Identifier.ValueText != "dynamic")
                continue;

            if (!IsTypePosition(identifier))
                continue;

            occurrences.Add((identifier, FindSnippetScope(identifier)));
        }

        var scopeCounts = occurrences
            .GroupBy(o => o.Scope)
            .ToDictionary(g => g.Key, g => g.Count());

        var scopeSeen = new Dictionary<SyntaxNode, int>();
        var ordinals = new Dictionary<IdentifierNameSyntax, int>();
        foreach (var (node, scope) in occurrences)
        {
            scopeSeen.TryGetValue(scope, out var seen);
            scopeSeen[scope] = ++seen;
            ordinals[node] = seen;
        }

        foreach (var (node, scope) in occurrences)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var location = node.GetLocation().GetLineSpan();
            var lineNumber = location.StartLinePosition.Line + 1;

            // Skip if line is not in scope
            if (!context.IsLineInScope(lineNumber))
                continue;

            // Check if the enclosing declaration has a valid suppression
            var suppressionTarget = FindSuppressionTarget(node);
            if (suppressionChecker.IsSuppressed(context, suppressionTarget, RuleId, lineNumber))
                continue;

            yield return new DetectionResult(
                RuleId,
                Name,
                DefaultSeverity,
                context.FilePath,
                lineNumber,
                location.StartLinePosition.Character + 1,
                "Use of 'dynamic' bypasses compile-time type checking",
                BuildSnippet(scope, ordinals[node], scopeCounts[scope]),
                "Replace 'dynamic' with a concrete type, a generic type parameter, or an interface. If dynamic is genuinely required (COM interop, dynamic proxy boundary), use [SlopwatchSuppress(\"SW021\", \"reason with 20+ chars\", \"Interop\")]"
            );
        }
    }

    /// <summary>
    /// Determines whether an identifier named <c>dynamic</c> occupies a type position.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is deliberately a <em>deny-list of non-type roles</em> rather than an allow-list of
    /// type positions. An identifier named <c>dynamic</c> is assumed to be the keyword unless it
    /// is provably something else (a member name, a label, a named argument, and so on). That
    /// way an unanticipated syntax form is flagged rather than silently skipped.
    /// </para>
    /// <para>
    /// The previous allow-list approach required a new switch arm for every type position and
    /// was found to miss explicit lambda return types, LINQ <c>from</c>/<c>join</c> clause
    /// types, and <c>__refvalue</c>.
    /// </para>
    /// </remarks>
    private static bool IsTypePosition(IdentifierNameSyntax identifier)
    {
        // Deliberate exclusions: nameof(...) and attribute arguments produce a string or
        // metadata, not a dynamic-typed slot.
        if (IsInExcludedContext(identifier))
            return false;

        return identifier.Parent switch
        {
            // --- Provably NOT a type position ---

            // `dynamic.ToString()`, `this.dynamic`, `E.dynamic` — the identifier is a value
            // receiver or a member name, never a type. `dynamic` has no static members, so a
            // member-access receiver can never be the dynamic type.
            MemberAccessExpressionSyntax => false,

            // `M(dynamic: 1)` — named argument.
            NameColonSyntax => false,

            // `dynamic:` — statement label.
            LabeledStatementSyntax => false,

            // `goto dynamic;`
            GotoStatementSyntax => false,

            // Declaration names: `int dynamic;`, `void dynamic()`, `class dynamic { }`.
            VariableDeclaratorSyntax => false,
            MethodDeclarationSyntax m => m.Identifier.ValueText != "dynamic",
            PropertyDeclarationSyntax p => p.Identifier.ValueText != "dynamic",
            EventDeclarationSyntax e => e.Identifier.ValueText != "dynamic",
            BaseTypeDeclarationSyntax t => t.Identifier.ValueText != "dynamic",
            DelegateDeclarationSyntax d => d.Identifier.ValueText != "dynamic",
            EnumMemberDeclarationSyntax => false,
            TypeParameterSyntax => false,
            ParameterSyntax p => p.Identifier.ValueText != "dynamic",
            LocalFunctionStatementSyntax l => l.Identifier.ValueText != "dynamic",
            ConstructorDeclarationSyntax c => c.Identifier.ValueText != "dynamic",
            DestructorDeclarationSyntax d => d.Identifier.ValueText != "dynamic",
            IndexerDeclarationSyntax i => i.Type == identifier,
            OperatorDeclarationSyntax o => o.ReturnType == identifier,
            ConversionOperatorDeclarationSyntax c => c.Type == identifier,
            AccessorDeclarationSyntax => false,

            // Attribute names: `[dynamic]`.
            AttributeSyntax a => a.Name != identifier,

            // `using dynamic = ...;` — the alias name, not the target type.
            UsingDirectiveSyntax u => u.Alias?.Name != identifier,

            // `nameof` / `typeof` handled by IsInExcludedContext or are genuine type slots.

            // --- Everything else is treated as a type position ---
            _ => true
        };
    }

    /// <summary>
    /// Determines whether a node sits inside a context excluded from the ban.
    /// </summary>
    /// <remarks>
    /// <c>nameof(dynamic)</c> and attribute arguments produce a string or metadata rather
    /// than a dynamic-typed slot, so they have no runtime effect and are not flagged.
    /// </remarks>
    private static bool IsInExcludedContext(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case InvocationExpressionSyntax invocation
                    when invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" }:
                    return true;

                case AttributeSyntax:
                    return true;

                // Stop at the enclosing declaration; exclusions are local to the expression.
                case StatementSyntax:
                case MemberDeclarationSyntax:
                    return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Finds the node whose text is used as the reported snippet and baseline hash input.
    /// </summary>
    /// <remarks>
    /// Baseline entries hash on RuleId + path + snippet, so the snippet must be narrow enough
    /// that a newly introduced <c>dynamic</c> produces a different hash. The enclosing statement
    /// or member declaration gives per-statement granularity inside method bodies while keeping
    /// declaration-level granularity for signatures. Falling back to the bare keyword would let
    /// one baselined occurrence whitelist every future <c>dynamic</c> in the file.
    /// </remarks>
    private static SyntaxNode FindSnippetScope(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is StatementSyntax or MemberDeclarationSyntax or UsingDirectiveSyntax)
                return current;
        }

        return node;
    }

    /// <summary>
    /// Builds the snippet text, disambiguating occurrences that would otherwise collide.
    /// </summary>
    /// <remarks>
    /// Two identical statements in different methods (<c>void A() { dynamic x = 1; }</c> and
    /// <c>void B() { dynamic x = 1; }</c>) produce identical statement text. Without a
    /// qualifier they hash the same, so baselining one would exempt the other. The enclosing
    /// member's name is prepended to keep those distinct.
    /// </remarks>
    private static string BuildSnippet(SyntaxNode scope, int occurrence, int total)
    {
        var text = scope.ToString();
        var qualifier = FindEnclosingMemberName(scope);

        var qualified = qualifier is null ? text : $"{qualifier}: {text}";

        return total <= 1
            ? qualified
            : $"{qualified}\n// dynamic occurrence {occurrence} of {total}";
    }

    /// <summary>
    /// Gets a stable name for the member enclosing a scope, used to disambiguate snippets.
    /// </summary>
    private static string? FindEnclosingMemberName(SyntaxNode scope)
    {
        for (var current = scope; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case MethodDeclarationSyntax m:
                    return m.Identifier.ValueText;
                case ConstructorDeclarationSyntax c:
                    return c.Identifier.ValueText;
                case DestructorDeclarationSyntax d:
                    return "~" + d.Identifier.ValueText;
                case PropertyDeclarationSyntax p:
                    return p.Identifier.ValueText;
                case IndexerDeclarationSyntax:
                    return "this[]";
                case EventDeclarationSyntax e:
                    return e.Identifier.ValueText;
                case EventFieldDeclarationSyntax ef:
                    return string.Join(",", ef.Declaration.Variables.Select(v => v.Identifier.ValueText));
                case FieldDeclarationSyntax f:
                    return string.Join(",", f.Declaration.Variables.Select(v => v.Identifier.ValueText));
                case OperatorDeclarationSyntax o:
                    return "operator " + o.OperatorToken.ValueText;
                case ConversionOperatorDeclarationSyntax co:
                    return "operator " + co.Type.ToString();
                case LocalFunctionStatementSyntax lf:
                    return lf.Identifier.ValueText;
                case BaseTypeDeclarationSyntax t:
                    return t.Identifier.ValueText;
                case DelegateDeclarationSyntax dd:
                    return dd.Identifier.ValueText;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the nearest enclosing declaration that can carry a suppression attribute.
    /// </summary>
    private static SyntaxNode? FindSuppressionTarget(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case MethodDeclarationSyntax:
                case ConstructorDeclarationSyntax:
                case DestructorDeclarationSyntax:
                case LocalFunctionStatementSyntax:
                case BaseTypeDeclarationSyntax:
                case PropertyDeclarationSyntax:
                case IndexerDeclarationSyntax:
                case FieldDeclarationSyntax:
                case EventDeclarationSyntax:
                case EventFieldDeclarationSyntax:
                case AccessorDeclarationSyntax:
                case OperatorDeclarationSyntax:
                case ConversionOperatorDeclarationSyntax:
                case DelegateDeclarationSyntax:
                    return current;
            }
        }

        return null;
    }
}
