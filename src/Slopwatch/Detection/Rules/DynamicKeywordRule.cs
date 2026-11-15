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
        // Segments are cached per node, never complete prefixes. Caching a complete prefix per
        // ancestor would retain O(depth^2) characters for a single occurrence, because each
        // ancestor's cached string contains every ancestor above it. Segments are O(1) each, so
        // the cache stays linear in tree size and the join happens once per occurrence.
        private readonly Dictionary<SyntaxNode, string> _segments =
            new(ReferenceEqualityComparer.Instance);

        private readonly Dictionary<SyntaxNode, string> _descriptions =
            new(ReferenceEqualityComparer.Instance);

        private readonly Dictionary<(SyntaxNode Parent, int RawKind, string? NamedIdentity), SiblingGroup> _groups =
            new(GroupKeyComparer.Instance);

        /// <summary>
        /// Builds the reported snippet for one occurrence.
        /// </summary>
        public string BuildSnippet(SyntaxNode node, string scopeText, int occurrence, int total)
        {
            var path = BuildStructuralPath(node);
            // string.Concat sizes the result once, so the full snippet is allocated exactly
            // once instead of once per interpolation step.
            return total <= 1
                ? string.Concat(path, ": ", scopeText)
                : string.Concat(path, ": ", scopeText,
                    $"\n// dynamic occurrence {occurrence} of {total}");
        }

        /// <summary>
        /// Builds a total path from the compilation unit to the occurrence.
        /// </summary>
        /// <remarks>
        /// Choose syntax identity because FileAnalyzer supplies no semantic model. Every node
        /// contributes a segment, including blocks and argument wrappers; unknown syntax never
        /// vanishes. Named declarations use their complete header, which includes static,
        /// explicit interface qualification, generic parameters, ref kinds and checked operators.
        /// This generic declaration fallback does not depend on a second list of member kinds
        /// and ignores differently named siblings. Repeated identical headers (for example,
        /// partial types or repeated namespace blocks) use ordinals within that header group.
        ///
        /// Unnamed nodes use kind + same-kind sibling ordinal. A multi-sibling group's count and
        /// content fingerprint also participate: inserting, replacing or reordering siblings must
        /// invalidate the old group rather than lend its old ordinal to newly introduced code.
        /// This deliberately trades baseline churn for rejecting false exemptions. Identical
        /// before/after source cannot reveal that an occurrence was deleted and recreated; no
        /// stateless syntax identity can recover that history. Hashes retain SHA256's usual
        /// collision assumption. Repeated headers are distinguished even in invalid code.
        /// </remarks>
        public string BuildStructuralPath(SyntaxNode node)
        {
            // Collect the ancestor chain, then join the cached segments once. Each segment is
            // rendered at most once per node, so total work is linear in the number of distinct
            // nodes touched rather than quadratic in depth.
            var segments = new List<string>();
            for (var current = node; current is not null; current = current.Parent)
                segments.Add(Segment(current));

            segments.Reverse();
            return string.Join("/", segments);
        }

        /// <summary>
        /// Renders one length-prefixed path segment.
        /// </summary>
        private string Segment(SyntaxNode node)
        {
            if (_segments.TryGetValue(node, out var cached))
                return cached;

            var named = IsNamedDeclaration(node);
            var description = Describe(node);
            var segment = description + SiblingIdentity(node, named ? description : null);
            // Length prefixes make paths unambiguous even if source text contains '/' or ':'.
            var rendered = $"{segment.Length}:{segment}";
            _segments[node] = rendered;
            return rendered;
        }

        /// <summary>
        /// Describes every syntax node, with a generic header identity for named declarations.
        /// </summary>
        private string Describe(SyntaxNode node)
        {
            if (_descriptions.TryGetValue(node, out var cached))
                return cached;

            var description = DescribeNode(node);
            _descriptions[node] = description;
            return description;
        }

        /// <summary>
        /// Identifies a node's position and the contents of its same-kind sibling group.
        /// </summary>
        private string SiblingIdentity(SyntaxNode node, string? namedIdentity)
        {
            if (node.Parent is null)
                return "#1of1";

            var group = GetGroup(node.Parent, node.RawKind, namedIdentity);
            if (namedIdentity is not null && group.Count == 1)
                return string.Empty;

            var position = $"#{group.OrdinalOf(node)}of{group.Count}";
            return group.Count == 1 ? position : $"{position}@{group.Fingerprint}";
        }

        /// <summary>
        /// Returns the cached sibling group for a parent, node kind and optional named identity.
        /// </summary>
        private SiblingGroup GetGroup(SyntaxNode parent, int rawKind, string? namedIdentity)
        {
            var key = (parent, rawKind, namedIdentity);
            if (_groups.TryGetValue(key, out var cached))
                return cached;

            var siblings = new List<SyntaxNode>();
            foreach (var sibling in parent.ChildNodes())
            {
                if (sibling.RawKind != rawKind)
                    continue;

                if (namedIdentity is not null && Describe(sibling) != namedIdentity)
                    continue;

                siblings.Add(sibling);
            }

            // The fingerprint covers the whole group, so it is computed once here rather than
            // once per member. Single-member groups never need one.
            string? fingerprint = null;
            if (siblings.Count > 1)
            {
                var contents = new StringBuilder();
                foreach (var sibling in siblings)
                {
                    var text = sibling.ToString();
                    contents.Append(text.Length).Append(':').Append(text);
                }

                fingerprint = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(contents.ToString())));
            }

            var ordinals = new Dictionary<SyntaxNode, int>(ReferenceEqualityComparer.Instance);
            for (var i = 0; i < siblings.Count; i++)
                ordinals[siblings[i]] = i + 1;

            var group = new SiblingGroup(siblings.Count, fingerprint, ordinals);
            _groups[key] = group;
            return group;
        }
    }

    /// <summary>
    /// Compares sibling-group cache keys by parent node reference.
    /// </summary>
    /// <remarks>
    /// The default tuple comparer would use <see cref="SyntaxNode"/>'s own equality. Roslyn
    /// nodes compare by reference today, but pinning that here keeps the cache correct even if
    /// that ever changes: two structurally identical parents must never share a group, because
    /// their members are distinct nodes with distinct ordinals.
    /// </remarks>
    private sealed class GroupKeyComparer
        : IEqualityComparer<(SyntaxNode Parent, int RawKind, string? NamedIdentity)>
    {
        public static readonly GroupKeyComparer Instance = new();

        public bool Equals(
            (SyntaxNode Parent, int RawKind, string? NamedIdentity) x,
            (SyntaxNode Parent, int RawKind, string? NamedIdentity) y)
            => ReferenceEquals(x.Parent, y.Parent)
                && x.RawKind == y.RawKind
                && string.Equals(x.NamedIdentity, y.NamedIdentity, StringComparison.Ordinal);

        public int GetHashCode((SyntaxNode Parent, int RawKind, string? NamedIdentity) key)
            => HashCode.Combine(
                RuntimeHelpers.GetHashCode(key.Parent),
                key.RawKind,
                key.NamedIdentity is null ? 0 : StringComparer.Ordinal.GetHashCode(key.NamedIdentity));
    }

    /// <summary>
    /// The shared identity of one sibling group: its size, content fingerprint and ordinals.
    /// </summary>
    private sealed class SiblingGroup
    {
        private readonly Dictionary<SyntaxNode, int> _ordinals;

        public SiblingGroup(int count, string? fingerprint, Dictionary<SyntaxNode, int> ordinals)
        {
            Count = count;
            Fingerprint = fingerprint;
            _ordinals = ordinals;
        }

        public int Count { get; }

        public string? Fingerprint { get; }

        /// <summary>
        /// Returns the 1-based position of <paramref name="node"/> within its sibling group.
        /// </summary>
        public int OrdinalOf(SyntaxNode node) => _ordinals[node];
    }

    /// <summary>
    /// Reports whether a node is a named declaration that uses its header as its identity.
    /// </summary>
    private static bool IsNamedDeclaration(SyntaxNode node)
        => node is MemberDeclarationSyntax and not GlobalStatementSyntax
            or LocalFunctionStatementSyntax;

    /// <summary>
    /// Describes every syntax node, with a generic header identity for named declarations.
    /// </summary>
    private static string DescribeNode(SyntaxNode node)
    {
        if (node is MemberDeclarationSyntax and not GlobalStatementSyntax
            or LocalFunctionStatementSyntax)
        {
            return $"{node.Kind()} {DeclarationHeader(node)}";
        }

        return node switch
        {
            CatchClauseSyntax c => $"catch {c.Declaration?.Type.ToString() ?? "all"} {c.Filter}",
            AccessorDeclarationSyntax a => $"accessor {a.Keyword.ValueText}",
            _ => node.Kind().ToString()
        };
    }

    /// <summary>
    /// Retains all declaration header tokens without including bodies or nested members.
    /// </summary>
    private static string DeclarationHeader(SyntaxNode declaration)
    {
        var tokens = declaration.DescendantTokens(descendIntoChildren: child =>
            ReferenceEquals(child, declaration) || child is not (
                BlockSyntax or ArrowExpressionClauseSyntax or AccessorListSyntax
                or EqualsValueClauseSyntax or MemberDeclarationSyntax));
        // Preserve token boundaries and literal spelling; no special cases for today's named
        // declaration forms are needed. A future member form gets this same header fallback.
        return string.Join(" ", tokens.Select(token => token.Text));
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
