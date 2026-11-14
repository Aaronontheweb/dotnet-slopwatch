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

        // Cache each scope's text once. Calling ToString() per occurrence is quadratic when
        // many occurrences share a scope.
        var scopeText = new Dictionary<SyntaxNode, string>();
        foreach (var (_, scope) in occurrences)
        {
            if (!scopeText.ContainsKey(scope))
                scopeText[scope] = scope.ToString();
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
                BuildSnippet(scope, scopeText[scope], ordinals[node], scopeCounts[scope]),
                "Replace 'dynamic' with a concrete type, a generic type parameter, or an interface. If dynamic is genuinely required (COM interop, dynamic proxy boundary), use [SlopwatchSuppress(\"SW021\", \"reason with 20+ chars\", \"Interop\")]"
            );
        }
    }

    /// <summary>
    /// Determines whether an identifier named <c>dynamic</c> occupies a type position.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Roslyn already answers this question: <see cref="SyntaxFacts.IsInNamespaceOrTypeContext"/>
    /// reports whether an expression appears in a namespace-or-type context. Using it instead of
    /// a hand-maintained switch means the rule inherits Roslyn's own knowledge of the grammar,
    /// so new C# syntax forms are handled without adding cases.
    /// </para>
    /// <para>
    /// A hand-written allow-list of parent node kinds was tried first and was provably
    /// incomplete: it missed explicit lambda return types, LINQ <c>from</c>/<c>join</c> clause
    /// types, and <c>__refvalue</c>. A deny-list was tried second and required enumerating every
    /// non-type role instead. Both were whack-a-mole. <c>SyntaxFacts</c> is the grammar's own
    /// answer.
    /// </para>
    /// </remarks>
    private static bool IsTypePosition(IdentifierNameSyntax identifier)
    {
        // Deliberate exclusions: nameof(...) and attribute arguments produce a string or
        // metadata, not a dynamic-typed slot.
        if (IsInExcludedContext(identifier))
            return false;

        // A qualified name (`N.dynamic`, `global::dynamic`, `X::dynamic`) refers to a named
        // type called `dynamic`, not the builtin contextual keyword. The keyword can never be
        // qualified.
        if (identifier.Parent is QualifiedNameSyntax or AliasQualifiedNameSyntax)
            return false;

        return SyntaxFacts.IsInNamespaceOrTypeContext(identifier);
    }

    /// <summary>
    /// Determines whether a node sits inside a context excluded from the ban.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Attribute arguments produce metadata rather than a dynamic-typed slot, so they are
    /// excluded. Note that <c>typeof(dynamic)</c> is CS1962 and cannot appear in valid code,
    /// so this exclusion is defensive rather than load-bearing.
    /// </para>
    /// <para>
    /// <c>nameof(dynamic)</c> is CS0103 and does not compile, so no exclusion is needed for it.
    /// An earlier version excluded any invocation whose callee was named <c>nameof</c>, which
    /// wrongly suppressed detection inside a user-defined method of that name.
    /// </para>
    /// </remarks>
    private static bool IsInExcludedContext(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
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
    /// Builds the snippet text used for reporting and baseline hashing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Baseline entries hash on RuleId + path + snippet, so the snippet must uniquely identify
    /// one occurrence. Earlier attempts qualified the statement text with the enclosing member
    /// name, then with a signature, then with a block path. Each was correct for the case that
    /// prompted it and wrong for the next one: overloads, sibling blocks, switch cases, catch
    /// clauses, same-named local functions, getter/setter pairs, and generic arity all collided.
    /// </para>
    /// <para>
    /// This builds a <em>structural path</em> instead: the chain of enclosing declarations and
    /// block-introducing constructs, each tagged with a discriminator that makes it unique
    /// (parameter types, generic arity, ref modifiers, case labels, exception types, accessor
    /// kind). The path is unique by construction rather than by enumeration of special cases.
    /// </para>
    /// <para>
    /// Line numbers are deliberately excluded: they would un-baseline an occurrence whenever
    /// unrelated code above it moved.
    /// </para>
    /// </remarks>
    private static string BuildSnippet(SyntaxNode scope, string scopeText, int occurrence, int total)
    {
        var path = BuildStructuralPath(scope);

        var qualified = path.Length == 0 ? scopeText : $"{path}: {scopeText}";

        return total <= 1
            ? qualified
            : $"{qualified}\n// dynamic occurrence {occurrence} of {total}";
    }

    /// <summary>
    /// Builds a unique structural path from the file root down to the given scope.
    /// </summary>
    /// <remarks>
    /// Sibling constructs of the same kind (two <c>if</c> statements, two lambdas, two
    /// <c>foreach</c> loops) would otherwise produce identical paths. Each segment therefore
    /// carries its ordinal among same-kind siblings, which is stable as long as the sibling
    /// order is unchanged.
    /// </remarks>
    private static string BuildStructuralPath(SyntaxNode scope)
    {
        var segments = new List<string>();

        // Include the scope itself so two identical statements in the same block differ by
        // their sibling ordinal.
        var scopeOrdinal = SiblingOrdinal(scope);
        if (scopeOrdinal is not null)
            segments.Add($"stmt#{scopeOrdinal}");

        for (var current = scope.Parent; current is not null; current = current.Parent)
        {
            var segment = DescribeNode(current);
            if (segment is null)
                continue;

            // Only constructs without a natural discriminator need a sibling ordinal.
            // Named members already carry their name and signature, so adding an ordinal
            // would change their hash whenever a sibling is added — un-baselining
            // pre-existing occurrences for no reason.
            if (HasNaturalDiscriminator(current))
            {
                segments.Add(segment);
            }
            else
            {
                var ordinal = SiblingOrdinal(current);
                segments.Add(ordinal is null ? segment : $"{segment}#{ordinal}");
            }
        }

        segments.Reverse();
        return string.Join("/", segments);
    }

    /// <summary>
    /// Indicates whether a node's description already distinguishes it from its siblings.
    /// </summary>
    /// <remarks>
    /// Named declarations (types, methods, properties, fields, events, local functions) carry
    /// their name and signature, so they are unique without an ordinal. Anonymous constructs
    /// (<c>if</c>, <c>else</c>, loops, lambdas, <c>try</c>) need one.
    /// </remarks>
    private static bool HasNaturalDiscriminator(SyntaxNode node) => node switch
    {
        BaseTypeDeclarationSyntax => true,
        DelegateDeclarationSyntax => true,
        MethodDeclarationSyntax => true,
        ConstructorDeclarationSyntax => true,
        DestructorDeclarationSyntax => true,
        PropertyDeclarationSyntax => true,
        IndexerDeclarationSyntax => true,
        EventDeclarationSyntax => true,
        EventFieldDeclarationSyntax => true,
        FieldDeclarationSyntax => true,
        LocalFunctionStatementSyntax => true,
        OperatorDeclarationSyntax => true,
        ConversionOperatorDeclarationSyntax => true,
        BaseNamespaceDeclarationSyntax => true,
        SwitchSectionSyntax => true,
        CatchClauseSyntax => true,
        AccessorDeclarationSyntax => true,
        _ => false
    };

    /// <summary>
    /// Gets the 1-based ordinal of a node among its same-kind siblings, or null when it is
    /// the only one.
    /// </summary>
    private static int? SiblingOrdinal(SyntaxNode node)
    {
        var parent = node.Parent;
        if (parent is null)
            return null;

        var sameKind = parent.ChildNodes().Where(n => n.RawKind == node.RawKind).ToList();
        return sameKind.Count <= 1 ? null : sameKind.IndexOf(node) + 1;
    }

    /// <summary>
    /// Describes a single node in a structural path, or returns null if it is not a
    /// path-relevant construct.
    /// </summary>
    private static string? DescribeNode(SyntaxNode node)
    {
        return node switch
        {
            // --- Type declarations: include generic arity and kind ---
            ClassDeclarationSyntax c => $"class {c.Identifier.ValueText}{Arity(c.TypeParameterList)}",
            StructDeclarationSyntax s => $"struct {s.Identifier.ValueText}{Arity(s.TypeParameterList)}",
            InterfaceDeclarationSyntax i => $"interface {i.Identifier.ValueText}{Arity(i.TypeParameterList)}",
            RecordDeclarationSyntax r => $"record {r.Identifier.ValueText}{Arity(r.TypeParameterList)}",
            EnumDeclarationSyntax e => $"enum {e.Identifier.ValueText}",

            // --- Namespaces: keep same-named types in different namespaces distinct ---
            BaseNamespaceDeclarationSyntax n => $"ns {n.Name}",

            // --- Callables: include parameter types, ref kinds, and generic arity ---
            MethodDeclarationSyntax m =>
                $"method {m.Identifier.ValueText}{Arity(m.TypeParameterList)}({ParameterSignature(m.ParameterList)})",
            ConstructorDeclarationSyntax c =>
                $"ctor {c.Identifier.ValueText}({ParameterSignature(c.ParameterList)})",
            DestructorDeclarationSyntax d => $"dtor {d.Identifier.ValueText}",
            LocalFunctionStatementSyntax l =>
                $"local {l.Identifier.ValueText}{Arity(l.TypeParameterList)}({ParameterSignature(l.ParameterList)})",
            OperatorDeclarationSyntax o =>
                $"operator {o.OperatorToken.ValueText}({ParameterSignature(o.ParameterList)})",
            ConversionOperatorDeclarationSyntax co =>
                $"conversion {co.ImplicitOrExplicitKeyword.ValueText} {co.Type}({ParameterSignature(co.ParameterList)})",
            DelegateDeclarationSyntax dd =>
                $"delegate {dd.Identifier.ValueText}{Arity(dd.TypeParameterList)}({ParameterSignature(dd.ParameterList)})",

            // --- Properties and indexers: distinguish accessors ---
            PropertyDeclarationSyntax p => $"property {p.Identifier.ValueText}",
            IndexerDeclarationSyntax i => $"indexer this[{ParameterSignature(i.ParameterList)}]",
            AccessorDeclarationSyntax a => $"accessor {a.Keyword.ValueText}",

            // --- Fields and events ---
            FieldDeclarationSyntax f =>
                $"field {string.Join(",", f.Declaration.Variables.Select(v => v.Identifier.ValueText))}",
            EventFieldDeclarationSyntax ef =>
                $"event {string.Join(",", ef.Declaration.Variables.Select(v => v.Identifier.ValueText))}",
            EventDeclarationSyntax e => $"event {e.Identifier.ValueText}",

            // --- Block-introducing constructs: include a discriminator where one exists ---
            IfStatementSyntax => "if",
            ElseClauseSyntax => "else",
            ForStatementSyntax => "for",
            ForEachStatementSyntax fe => $"foreach {fe.Identifier.ValueText}",
            WhileStatementSyntax => "while",
            DoStatementSyntax => "do",
            SwitchStatementSyntax => "switch",
            SwitchExpressionSyntax => "switch-expr",
            SwitchSectionSyntax section =>
                $"case {string.Join("|", section.Labels.Select(l => l.ToString()))}",
            TryStatementSyntax => "try",
            CatchClauseSyntax c => $"catch {c.Declaration?.Type.ToString() ?? "all"}",
            FinallyClauseSyntax => "finally",
            UsingStatementSyntax => "using",
            LockStatementSyntax => "lock",
            CheckedStatementSyntax => "checked",
            UnsafeStatementSyntax => "unsafe",
            FixedStatementSyntax => "fixed",
            AnonymousFunctionExpressionSyntax => "lambda",

            _ => null
        };
    }

    /// <summary>
    /// Renders generic arity, e.g. <c>&lt;T,U&gt;</c> becomes <c>&lt;2&gt;</c>.
    /// </summary>
    private static string Arity(TypeParameterListSyntax? typeParameters)
    {
        var count = typeParameters?.Parameters.Count ?? 0;
        return count == 0 ? string.Empty : $"<{count}>";
    }

    /// <summary>
    /// Renders a parameter list including ref kinds, so <c>N(int)</c> and <c>N(ref int)</c>
    /// stay distinct.
    /// </summary>
    private static string ParameterSignature(BaseParameterListSyntax? parameters)
    {
        if (parameters is null || parameters.Parameters.Count == 0)
            return string.Empty;

        return string.Join(",", parameters.Parameters.Select(p =>
        {
            var modifiers = string.Join(" ", p.Modifiers.Select(m => m.ValueText));
            var type = p.Type?.ToString() ?? "?";
            return modifiers.Length == 0 ? type : $"{modifiers} {type}";
        }));
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
