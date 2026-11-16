using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
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
/// A using directive's name is a namespace-or-type reference, not a slot: <c>using dynamic;</c>
/// imports a namespace named <c>dynamic</c> and <c>using static dynamic;</c> imports a type of
/// that name. An alias target is <em>not</em> excluded — <c>using D = dynamic;</c> aliases the
/// builtin type and is still flagged.
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

        // Structural-path fragments are shared between occurrences; compute each one once.
        var identity = new IdentityCache();

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
                BuildSnippet(node, scopeText[scope], ordinals[node], scopeCounts[scope], identity),
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

        // A using directive's name is a namespace-or-type *reference*, never a dynamic-typed
        // slot. `using dynamic;` imports a namespace called `dynamic`; `using static dynamic;`
        // imports a type of that name. Neither can bind to the builtin keyword — `using static
        // dynamic;` on its own is CS0246 — so both are name references, not slots.
        //
        // The one exception is an alias target: `using D = dynamic;` aliases the builtin type
        // and is a genuine dynamic slot, so the exclusion applies only when the directive has
        // no alias.
        //
        // This is a direct-parent test, not a walk up the ancestor chain. A chain walk would
        // also swallow `using D = List<dynamic>;`, whose type argument is a real dynamic slot.
        // It stays a single stable grammar node rather than the first arm of a growing switch:
        // the total `SyntaxFacts` fallback below still answers every other case.
        if (identifier.Parent is UsingDirectiveSyntax { Alias: null })
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
    /// Combines an occurrence's structural identity with its enclosing source snippet.
    /// </summary>
    private static string BuildSnippet(
        SyntaxNode node, string scopeText, int occurrence, int total, IdentityCache identity)
        => identity.BuildSnippet(node, scopeText, occurrence, total);

    /// <summary>
    /// Caches structural-path fragments for the duration of a single analysis.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every occurrence needs a total path from the compilation unit down to itself, and those
    /// paths share almost all of their text. Building each path independently is quadratic: the
    /// shared prefix is re-rendered once per occurrence, and — worse — the sibling fingerprint
    /// for a group of N siblings is recomputed by each of the N members, hashing the
    /// concatenation of all N sibling texts N times.
    /// </para>
    /// <para>
    /// Both costs are pure functions of the syntax tree, so they are memoized here. The
    /// fingerprint for a sibling group is identical for every member of that group, so it is
    /// computed once per group. Path prefixes are memoized per node, so an ancestor shared by
    /// many occurrences is rendered once. The emitted strings are byte-for-byte identical to the
    /// uncached implementation; only the number of times they are computed changes.
    /// </para>
    /// <para>
    /// Keys are <see cref="SyntaxNode"/> references. Roslyn's nodes use reference equality
    /// (they do not implement <see cref="IEquatable{T}"/>), so structurally identical but
    /// distinct nodes never share a cache entry — matching the uncached ordinal lookup, which
    /// used <see cref="List{T}.IndexOf"/> and therefore also compared by reference.
    /// </para>
    /// </remarks>
    private sealed class IdentityCache
    {
        // The shared helper owns the caching. This wrapper exists so the rule keeps its own
        // BuildSnippet shape while the path and fingerprint logic lives in one place, shared with
        // the other rules that need structural identities.
        private readonly StructuralPathCache _cache = new();

        /// <summary>
        /// Builds the reported snippet for one occurrence.
        /// </summary>
        public string BuildSnippet(SyntaxNode node, string scopeText, int occurrence, int total)
        {
            var path = BuildStructuralPath(node, _cache);
            // string.Concat sizes the result once, so the full snippet is allocated exactly
            // once instead of once per interpolation step.
            return total <= 1
                ? string.Concat(path, ": ", scopeText)
                : string.Concat(path, ": ", scopeText,
                    $"\n// dynamic occurrence {occurrence} of {total}");
        }
    }

    /// <summary>
    /// Builds a total path from the compilation unit to the occurrence.
    /// </summary>
    /// <remarks>
    /// Delegates to <see cref="StructuralPath.ForNode"/>, the shared implementation extracted
    /// from this rule. The path format is unchanged, so existing SW021 baselines and the
    /// identity test suite continue to match.
    /// </remarks>
    private static string BuildStructuralPath(SyntaxNode node, StructuralPathCache cache)
        => StructuralPath.ForNode(node, cache);

    /// <summary>
    /// Finds the nearest enclosing declaration that can carry a suppression attribute.
    /// </summary>
    private static SyntaxNode? FindSuppressionTarget(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case LambdaExpressionSyntax:
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
