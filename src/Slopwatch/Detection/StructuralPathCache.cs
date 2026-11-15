// -----------------------------------------------------------------------
// <copyright file="StructuralPathCache.cs" company="Aaron Stannard">
//     Copyright (C) 2025 - 2025 Aaron Stannard
// </copyright>
// -----------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;

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
    private readonly Dictionary<GroupKey, GroupInfo> _groups = new(GroupKeyComparer.Instance);

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
        if (_groups.TryGetValue(key, out var cached))
            return cached;

        var siblings = new List<SyntaxNode>();
        foreach (var sibling in parent.ChildNodes())
        {
            if (sibling.RawKind != node.RawKind)
                continue;

            if (namedIdentity is not null && StructuralPath.DescribeNodeForCache(sibling) != namedIdentity)
                continue;

            siblings.Add(sibling);
        }

        var ordinal = siblings.FindIndex(sibling => ReferenceEquals(sibling, node)) + 1;
        if (ordinal <= 0)
        {
            // Defensive: the node was not found in its own sibling list.
            siblings = new List<SyntaxNode> { node };
            ordinal = 1;
        }

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

        var info = new GroupInfo(ordinal, siblings.Count, fingerprint);
        _groups[key] = info;
        return info;
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
