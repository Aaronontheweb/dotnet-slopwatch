// -----------------------------------------------------------------------
// <copyright file="StructuralPathCache.cs" company="Aaron Stannard">
//     Copyright (C) 2025 - 2025 Aaron Stannard
// </copyright>
// -----------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
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
    private readonly Dictionary<PartitionKey, Partition> _partitions = new(PartitionKeyComparer.Instance);

    private readonly Dictionary<DirectiveKey, StructuralPath.DirectiveGroup> _directives =
        new(DirectiveKeyComparer.Instance);

    private readonly Dictionary<XElement, Dictionary<XName, XmlGroup>> _xmlPartitions =
        new(ReferenceEqualityComparer.Instance);

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

        // Partition the parent's children by kind ONCE, then by header within that kind. Keying
        // the cache on the header instead would make each differently named sibling its own group,
        // and each of those groups would rescan every sibling to build itself: N methods with one
        // delay each meant N groups each scanning N headers, which is quadratic.
        var partitionKey = new PartitionKey(parent, node.RawKind);
        if (!_partitions.TryGetValue(partitionKey, out var partition))
        {
            partition = BuildPartition(parent, node.RawKind);
            _partitions[partitionKey] = partition;
        }

        var group = namedIdentity is null
            ? partition.Unnamed
            : partition.ByHeader.TryGetValue(namedIdentity, out var named)
                ? named
                : partition.Unnamed;

        // The cached entry describes the whole group, not any particular member. Each node must
        // look up its own ordinal: returning the ordinal of whichever node happened to populate
        // the cache would give every sibling the same position, which is exactly the baseline
        // collision this identity scheme exists to prevent.
        var ordinal = group.Ordinals.TryGetValue(node, out var found) ? found : 1;
        return new GroupInfo(ordinal, group.Count, group.Fingerprint);
    }

    /// <summary>
    /// Partitions a parent's children of one kind by declaration header, in a single pass.
    /// </summary>
    private static Partition BuildPartition(SyntaxNode parent, int rawKind)
    {
        var byHeader = new Dictionary<string, List<SyntaxNode>>(StringComparer.Ordinal);
        var unnamed = new List<SyntaxNode>();

        foreach (var sibling in parent.ChildNodes())
        {
            if (sibling.RawKind != rawKind)
                continue;

            unnamed.Add(sibling);

            var header = StructuralPath.DescribeNodeForCache(sibling);
            if (!byHeader.TryGetValue(header, out var list))
            {
                list = new List<SyntaxNode>();
                byHeader[header] = list;
            }

            list.Add(sibling);
        }

        var namedGroups = new Dictionary<string, CachedGroup>(StringComparer.Ordinal);
        foreach (var (header, members) in byHeader)
            namedGroups[header] = BuildGroup(members);

        return new Partition(BuildGroup(unnamed), namedGroups);
    }

    /// <summary>
    /// Builds a group's shared data from an already-enumerated member list.
    /// </summary>
    private static CachedGroup BuildGroup(List<SyntaxNode> members)
    {
        if (members.Count == 0)
            return new CachedGroup(0, null, EmptyOrdinals);

        // The fingerprint covers the whole group, so it is computed once here rather than once
        // per member. Single-member groups never need one.
        string? fingerprint = null;
        if (members.Count > 1)
        {
            var contents = new StringBuilder();
            foreach (var member in members)
            {
                var text = member.ToString();
                contents.Append(text.Length).Append(':').Append(text);
            }

            fingerprint = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(contents.ToString())));
        }

        var ordinals = new Dictionary<SyntaxNode, int>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < members.Count; i++)
            ordinals[members[i]] = i + 1;

        return new CachedGroup(members.Count, fingerprint, ordinals);
    }

    private static readonly Dictionary<SyntaxNode, int> EmptyOrdinals =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// One parent's children of a single kind, partitioned by declaration header.
    /// </summary>
    private sealed record Partition(CachedGroup Unnamed, Dictionary<string, CachedGroup> ByHeader);

    /// <summary>
    /// Identifies a partition: the parent and the child kind.
    /// </summary>
    private readonly record struct PartitionKey(SyntaxNode Parent, int RawKind);

    /// <summary>
    /// Compares partition keys by parent reference, so distinct parents never share a partition.
    /// </summary>
    private sealed class PartitionKeyComparer : IEqualityComparer<PartitionKey>
    {
        public static readonly PartitionKeyComparer Instance = new();

        public bool Equals(PartitionKey x, PartitionKey y) =>
            ReferenceEquals(x.Parent, y.Parent) && x.RawKind == y.RawKind;

        public int GetHashCode(PartitionKey key) =>
            HashCode.Combine(
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(key.Parent),
                key.RawKind);
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
            return new StructuralPath.DirectiveGroup(new[] { trivia }, null, EmptyDirectiveOrdinals);

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

        var ordinals = new Dictionary<StructuredTriviaSyntax, int>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < members.Length; i++)
            ordinals[members[i]] = i + 1;

        var group = new StructuralPath.DirectiveGroup(members, fingerprint, ordinals);
        _directives[key] = group;
        return group;
    }

    private static readonly Dictionary<StructuredTriviaSyntax, int> EmptyDirectiveOrdinals =
        new(ReferenceEqualityComparer.Instance);

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
    /// Returns the cached sibling group for an XML element.
    /// </summary>
    /// <remarks>
    /// XML has no declaration headers, so a group is simply the same-name siblings under one
    /// parent. Without this cache every finding re-enumerates, serializes, and fingerprints its
    /// whole group: 57 GB at 2,000 findings.
    /// </remarks>
    internal XmlGroup GetXmlGroup(XElement element)
    {
        var parent = element.Parent;
        if (parent is null)
            return new XmlGroup(1, 1, null);

        // Partition the parent's children by qualified name ONCE. Calling
        // parent.Elements(element.Name) per distinct name rescans every child each time, so N
        // distinct names under one parent cost N^2 name comparisons: 1.58 seconds of identity
        // construction at 16,000 mixed-namespace elements.
        if (!_xmlPartitions.TryGetValue(parent, out var partition))
        {
            partition = BuildXmlPartition(parent);
            _xmlPartitions[parent] = partition;
        }

        if (!partition.TryGetValue(element.Name, out var group))
            return new XmlGroup(1, 1, null);

        var ordinal = group.Ordinals is not null && group.Ordinals.TryGetValue(element, out var found)
            ? found
            : 1;
        return new XmlGroup(ordinal, group.Count, group.Fingerprint);
    }

    /// <summary>
    /// Partitions one parent's children by qualified name in a single pass.
    /// </summary>
    private static Dictionary<XName, XmlGroup> BuildXmlPartition(XElement parent)
    {
        var byName = new Dictionary<XName, List<XElement>>();
        foreach (var child in parent.Elements())
        {
            if (!byName.TryGetValue(child.Name, out var list))
            {
                list = new List<XElement>();
                byName[child.Name] = list;
            }

            list.Add(child);
        }

        var result = new Dictionary<XName, XmlGroup>();
        foreach (var (name, members) in byName)
        {
            var fingerprint = members.Count == 1
                ? null
                : StructuralPath.FingerprintForCache(members.Select(m => m.ToString()));

            var ordinals = new Dictionary<XElement, int>(ReferenceEqualityComparer.Instance);
            for (var i = 0; i < members.Count; i++)
                ordinals[members[i]] = i + 1;

            result[name] = new XmlGroup(0, members.Count, fingerprint, ordinals);
        }

        return result;
    }

    /// <summary>
    /// An XML sibling group's shared data plus per-member ordinals.
    /// </summary>
    internal readonly record struct XmlGroup(
        int Ordinal,
        int Count,
        string? Fingerprint,
        Dictionary<XElement, int>? Ordinals = null);

    /// <summary>
    /// Identifies an XML sibling group: its parent and the fully qualified element name.
    /// </summary>
    private readonly record struct XmlKey(XElement Parent, XName Name);

    /// <summary>
    /// Compares XML keys by parent reference, so distinct parents never share a group.
    /// </summary>
    private sealed class XmlKeyComparer : IEqualityComparer<XmlKey>
    {
        public static readonly XmlKeyComparer Instance = new();

        public bool Equals(XmlKey x, XmlKey y) =>
            ReferenceEquals(x.Parent, y.Parent) && x.Name == y.Name;

        public int GetHashCode(XmlKey key) =>
            HashCode.Combine(
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(key.Parent),
                key.Name);
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
