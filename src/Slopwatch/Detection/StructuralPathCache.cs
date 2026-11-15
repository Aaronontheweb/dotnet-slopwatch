// -----------------------------------------------------------------------
// <copyright file="StructuralPathCache.cs" company="Aaron Stannard">
//     Copyright (C) 2025 - 2025 Aaron Stannard
// </copyright>
// -----------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Slopwatch.Detection;

/// <summary>
/// Caches the per-sibling-group work that <see cref="StructuralPath"/> would otherwise repeat for
/// every occurrence in the same group.
/// </summary>
/// <remarks>
/// <para>
/// Without a cache, identifying N occurrences that share one sibling group enumerates the group N
/// times and serializes plus hashes all N sibling texts N times. That is quadratic in both time
/// and allocation: a test method with 2,000 <c>Task.Delay</c> calls allocated over a gigabyte.
/// </para>
/// <para>
/// A cache instance is scoped to a single analysis. It holds no static state, so concurrent
/// analyses never share mutable data. Keys use reference equality because Roslyn nodes do not
/// implement <see cref="IEquatable{T}"/>; structurally identical but distinct nodes must not share
/// an entry, or ordinals would be computed against the wrong group.
/// </para>
/// </remarks>
public sealed class StructuralPathCache
{
    private readonly Dictionary<GroupKey, CachedGroup> _groups = new(GroupKeyComparer.Instance);

    private readonly Dictionary<DirectiveKey, StructuralPath.DirectiveGroup> _directives =
        new(DirectiveKeyComparer.Instance);

    /// <summary>
    /// Returns the cached group information for a node's sibling group, computing it on first use.
    /// </summary>
    /// <param name="node">The node whose sibling group is needed.</param>
    /// <param name="namedIdentity">
    /// When non-null, only siblings whose description matches this value are grouped together.
    /// </param>
    /// <returns>The ordinal of <paramref name="node"/> and the group's fingerprint.</returns>
    internal GroupInfo GetGroup(SyntaxNode node, string? namedIdentity)
    {
        var parent = node.Parent;
        if (parent is null)
            return new GroupInfo(1, 1, null);

        var key = new GroupKey(parent, node.RawKind, namedIdentity);
        if (!_groups.TryGetValue(key, out var cached))
        {
            cached = BuildGroup(node, namedIdentity);
            _groups[key] = cached;
        }

        // The cached entry describes the whole group, not any particular member. Each node must
        // look up its own ordinal: returning the ordinal of whichever node happened to populate
        // the cache would give every sibling the same position, which is exactly the baseline
        // collision this identity scheme exists to prevent.
        var ordinal = cached.Ordinals.TryGetValue(node, out var found) ? found : 1;
        return new GroupInfo(ordinal, cached.Count, cached.Fingerprint);
    }

    /// <summary>
    /// Enumerates a sibling group once and records each member's ordinal.
    /// </summary>
    private static CachedGroup BuildGroup(SyntaxNode node, string? namedIdentity)
    {
        var parent = node.Parent!;
        var siblings = new List<SyntaxNode>();
        foreach (var sibling in parent.ChildNodes())
        {
            if (sibling.RawKind != node.RawKind)
                continue;

            if (namedIdentity is not null && StructuralPath.DescribeNodeForCache(sibling) != namedIdentity)
                continue;

            siblings.Add(sibling);
        }

        if (siblings.Count == 0)
            siblings.Add(node);

        // The fingerprint covers the whole group, so it is computed once here rather than once
        // per member. Single-member groups never need one.
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

        return new CachedGroup(siblings.Count, fingerprint, ordinals);
    }

    /// <summary>
    /// A sibling group's shared data: member count, group fingerprint, and per-member ordinals.
    /// </summary>
    private sealed record CachedGroup(
        int Count,
        string? Fingerprint,
        Dictionary<SyntaxNode, int> Ordinals);

    /// <summary>
    /// Returns the cached directive group for a structured trivia node's owner.
    /// </summary>
    /// <param name="owner">The node that owns the trivia's token.</param>
    /// <param name="trivia">The trivia whose group is needed.</param>
    /// <remarks>
    /// Structured trivia reports a null Parent, so it cannot use the ordinary sibling-group cache.
    /// Keying on the owner plus the trivia kind gives the same grouping the uncached path builds,
    /// while computing it once per owner instead of once per directive.
    /// </remarks>
    internal StructuralPath.DirectiveGroup GetDirectiveGroup(SyntaxNode? owner, StructuredTriviaSyntax trivia)
    {
        if (owner is null)
            return new StructuralPath.DirectiveGroup(new[] { trivia }, null);

        var key = new DirectiveKey(owner, trivia.RawKind);
        if (_directives.TryGetValue(key, out var cached))
            return cached;

        var members = owner.DescendantTrivia()
            .Where(candidate => candidate.RawKind == trivia.RawKind)
            .Select(candidate => candidate.GetStructure())
            .OfType<StructuredTriviaSyntax>()
            .ToArray();

        if (members.Length == 0)
            members = new[] { trivia };

        var fingerprint = members.Length == 1
            ? null
            : StructuralPath.FingerprintForCache(members.Select(m => m.ToString()));

        var group = new StructuralPath.DirectiveGroup(members, fingerprint);
        _directives[key] = group;
        return group;
    }

    /// <summary>
    /// Identifies a directive group: its owning node and the trivia kind.
    /// </summary>
    private readonly record struct DirectiveKey(SyntaxNode Owner, int RawKind);

    /// <summary>
    /// Compares directive keys by owner reference, so distinct owners never share a group.
    /// </summary>
    private sealed class DirectiveKeyComparer : IEqualityComparer<DirectiveKey>
    {
        public static readonly DirectiveKeyComparer Instance = new();

        public bool Equals(DirectiveKey x, DirectiveKey y) =>
            ReferenceEquals(x.Owner, y.Owner) && x.RawKind == y.RawKind;

        public int GetHashCode(DirectiveKey key) =>
            HashCode.Combine(
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(key.Owner),
                key.RawKind);
    }

    /// <summary>
    /// Identifies a sibling group: its parent, the node kind, and the optional named identity.
    /// </summary>
    private readonly record struct GroupKey(SyntaxNode Parent, int RawKind, string? NamedIdentity);

    /// <summary>
    /// Compares group keys by parent reference, so distinct nodes never share a cache entry.
    /// </summary>
    private sealed class GroupKeyComparer : IEqualityComparer<GroupKey>
    {
        public static readonly GroupKeyComparer Instance = new();

        public bool Equals(GroupKey x, GroupKey y) =>
            ReferenceEquals(x.Parent, y.Parent)
            && x.RawKind == y.RawKind
            && string.Equals(x.NamedIdentity, y.NamedIdentity, StringComparison.Ordinal);

        public int GetHashCode(GroupKey key) =>
            HashCode.Combine(
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(key.Parent),
                key.RawKind,
                key.NamedIdentity is null ? 0 : StringComparer.Ordinal.GetHashCode(key.NamedIdentity));
    }

    /// <summary>
    /// The ordinal of a node within its group, the group size, and the group fingerprint.
    /// </summary>
    internal readonly record struct GroupInfo(int Ordinal, int Count, string? Fingerprint);
}
