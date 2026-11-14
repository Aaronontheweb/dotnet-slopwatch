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
/// This rule flags every syntactic use of <c>dynamic</c> as a type:
/// <list type="bullet">
/// <item><description>Parameters, return types, fields, properties, indexers, locals</description></item>
/// <item><description>Generic type arguments and generic constraints (<c>where T : dynamic</c>)</description></item>
/// <item><description>Casts, <c>as dynamic</c>, <c>is dynamic</c>, and type patterns</description></item>
/// <item><description><c>using</c> aliases (<c>using D = dynamic;</c>)</description></item>
/// <item><description>Array element types, tuples, function pointers, base types</description></item>
/// </list>
///
/// <c>dynamic</c> is a hard ban because it:
/// <list type="bullet">
/// <item><description>Bypasses compile-time type checking entirely</description></item>
/// <item><description>Defeats static analysis, refactoring, and IDE tooling</description></item>
/// <item><description>Breaks trimming, AOT compilation, and source generators</description></item>
/// <item><description>Is a common LLM escape hatch for silencing type errors instead of fixing them</description></item>
/// </list>
///
/// <strong>Deliberate exclusions:</strong>
/// <list type="bullet">
/// <item><description>
/// <c>nameof(dynamic)</c> and attribute-argument positions are not flagged — they produce a
/// string, not a dynamic-typed slot, so they have no runtime effect.
/// </description></item>
/// <item><description>
/// A user-defined type literally named <c>dynamic</c> (<c>class dynamic { }</c>) is flagged.
/// This is an accepted false positive: the rule is purely syntactic and has no semantic model.
/// </description></item>
/// <item><description>
/// <c>System.Dynamic</c> and <c>ExpandoObject</c> are not flagged. The ban is keyword-shaped,
/// not behavior-shaped.
/// </description></item>
/// </list>
///
/// This rule can be suppressed with <see cref="SlopwatchSuppressAttribute"/> for the rare
/// legitimate case (COM interop, dynamic proxy boundaries). Suppression applies to the nearest
/// enclosing declaration, so a class-level attribute does not cover its members.
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

        // Group occurrences by their enclosing snippet node so we can disambiguate
        // multiple `dynamic` tokens that share the same enclosing declaration.
        var occurrences = new List<(IdentifierNameSyntax Node, SyntaxNode Scope)>();

        foreach (var node in root.DescendantNodes())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (node is not IdentifierNameSyntax identifier || !IsDynamicTypeReference(identifier))
                continue;

            occurrences.Add((identifier, FindSnippetScope(identifier)));
        }

        var scopeCounts = occurrences
            .GroupBy(o => o.Scope)
            .ToDictionary(g => g.Key, g => g.Count());

        var scopeSeen = new Dictionary<SyntaxNode, int>();

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

            scopeSeen.TryGetValue(scope, out var seen);
            scopeSeen[scope] = ++seen;

            yield return new DetectionResult(
                RuleId,
                Name,
                DefaultSeverity,
                context.FilePath,
                lineNumber,
                location.StartLinePosition.Character + 1,
                "Use of 'dynamic' bypasses compile-time type checking",
                BuildSnippet(scope, seen, scopeCounts[scope]),
                "Replace 'dynamic' with a concrete type, a generic type parameter, or an interface. If dynamic is genuinely required (COM interop, dynamic proxy boundary), use [SlopwatchSuppress(\"SW021\", \"reason with 20+ chars\")]"
            );
        }
    }

    /// <summary>
    /// Determines whether a node is the <c>dynamic</c> keyword used in a type position.
    /// </summary>
    /// <remarks>
    /// <c>dynamic</c> is a contextual keyword, so it parses as an <see cref="IdentifierNameSyntax"/>.
    /// We only flag it when it occupies a type slot, which avoids false positives on
    /// identifiers that happen to be named <c>dynamic</c> (e.g. <c>dynamic.ToString()</c>).
    /// </remarks>
    private static bool IsDynamicTypeReference(SyntaxNode node)
    {
        if (node is not IdentifierNameSyntax identifier || identifier.Identifier.ValueText != "dynamic")
            return false;

        return identifier.Parent switch
        {
            // Declarations and signatures
            VariableDeclarationSyntax v => v.Type == identifier,
            ParameterSyntax p => p.Type == identifier,
            PropertyDeclarationSyntax p => p.Type == identifier,
            IndexerDeclarationSyntax i => i.Type == identifier,
            MethodDeclarationSyntax m => m.ReturnType == identifier,
            LocalFunctionStatementSyntax l => l.ReturnType == identifier,
            DelegateDeclarationSyntax d => d.ReturnType == identifier,
            OperatorDeclarationSyntax o => o.ReturnType == identifier,
            ConversionOperatorDeclarationSyntax c => c.Type == identifier,
            EventDeclarationSyntax e => e.Type == identifier,
            EventFieldDeclarationSyntax e => e.Declaration.Type == identifier,
            ForEachStatementSyntax f => f.Type == identifier,
            CatchDeclarationSyntax c => c.Type == identifier,
            DeclarationExpressionSyntax d => d.Type == identifier,
            TupleElementSyntax t => t.Type == identifier,
            SimpleBaseTypeSyntax s => s.Type == identifier,
            PrimaryConstructorBaseTypeSyntax p => p.Type == identifier,

            // Generic positions
            TypeArgumentListSyntax t => t.Arguments.Contains(identifier),
            TypeConstraintSyntax t => t.Type == identifier,

            // Conversions and patterns
            CastExpressionSyntax c => c.Type == identifier,
            ObjectCreationExpressionSyntax o => o.Type == identifier,
            TypeOfExpressionSyntax t => t.Type == identifier,
            DefaultExpressionSyntax d => d.Type == identifier,
            DeclarationPatternSyntax d => d.Type == identifier,
            RecursivePatternSyntax r => r.Type == identifier,
            BinaryExpressionSyntax b => (b.IsKind(SyntaxKind.AsExpression) || b.IsKind(SyntaxKind.IsExpression))
                                        && b.Right == identifier,

            // Composite type forms
            ArrayTypeSyntax a => a.ElementType == identifier,
            NullableTypeSyntax n => n.ElementType == identifier,
            RefTypeSyntax r => r.Type == identifier,
            PointerTypeSyntax p => p.ElementType == identifier,
            FunctionPointerParameterSyntax f => f.Type == identifier,

            // using aliases: `using D = dynamic;` and `using static dynamic;`
            UsingDirectiveSyntax u => u.Alias?.Name == identifier || u.Name == identifier,

            _ => false
        };
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
    /// Builds the snippet text, disambiguating multiple occurrences within one scope.
    /// </summary>
    private static string BuildSnippet(SyntaxNode scope, int occurrence, int total)
    {
        var text = scope.ToString();
        return total <= 1
            ? text
            : $"{text}\n// dynamic occurrence {occurrence} of {total}";
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
