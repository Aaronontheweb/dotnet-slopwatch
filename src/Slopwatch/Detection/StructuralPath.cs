// -----------------------------------------------------------------------
// <copyright file="StructuralPath.cs" company="Aaron Stannard">
//     Copyright (C) 2025 - 2025 Aaron Stannard
// </copyright>
// -----------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Slopwatch.Detection;

/// <summary>
/// Builds a total structural identity for a syntax node or an XML element.
/// </summary>
/// <remarks>
/// <para>
/// Baseline entries hash on <c>RuleId + path + identity</c>. When a rule feeds raw syntax text
/// into that hash, two identical occurrences in the same file collapse to one identity and
/// baselining one silently exempts the other. That is a false negative: the tool reports fewer
/// problems than exist. This helper supplies the missing positional component.
/// </para>
/// <para>
/// <strong>The path is total.</strong> Every ancestor contributes a segment, including blocks,
/// argument wrappers and other unnamed nodes; unknown syntax falls back to
/// <see cref="SyntaxNode.Kind"/>. A hand-maintained enumeration of "interesting" node kinds was
/// deliberately avoided: enumerations silently drop the cases nobody anticipated, which is the
/// same failure mode this helper exists to prevent.
/// </para>
/// <para>
/// Named declarations use their complete header (modifiers, explicit interface qualification,
/// generic parameters, parameter modifiers, ref kinds, checked operators) rather than a
/// per-member-kind switch. Unnamed nodes use kind plus same-kind sibling ordinal. A multi-sibling
/// group's count and content fingerprint also participate, so inserting, replacing or reordering
/// siblings invalidates the old group instead of lending its old ordinal to newly introduced code.
/// </para>
/// <para>
/// This deliberately trades baseline churn for rejecting false exemptions. Identical before/after
/// source cannot reveal that an occurrence was deleted and recreated; no stateless syntax identity
/// can recover that history. Hashes retain SHA256's usual collision assumption.
/// </para>
/// <para>
/// <strong>Identity is path plus content.</strong> The path alone is not enough: a single-child
/// element keeps the same path when its text changes, so a baselined <c>&lt;NoWarn&gt;CS1&lt;/NoWarn&gt;</c>
/// would keep exempting a rewritten <c>&lt;NoWarn&gt;CS2&lt;/NoWarn&gt;</c>. Callers should use
/// <see cref="IdentityFor(SyntaxNode)"/> / <see cref="IdentityFor(XElement)"/> for the hash input
/// and keep their own readable text for display.
/// </para>
/// </remarks>
public static class StructuralPath
{
    /// <summary>
    /// Builds a total path from the compilation unit to <paramref name="node"/>.
    /// </summary>
    /// <param name="node">The node to identify.</param>
    /// <param name="cache">
    /// Optional per-analysis cache. Supplying one avoids re-enumerating and re-fingerprinting the
    /// same sibling group for every occurrence, which is quadratic without it.
    /// </param>
    /// <returns>A length-prefixed, slash-separated path with one segment per ancestor-or-self.</returns>
    public static string ForNode(SyntaxNode node, StructuralPathCache? cache = null)
    {
        ArgumentNullException.ThrowIfNull(node);

        // Structured trivia (for example #pragma directives) report a null Parent even though
        // AncestorsAndSelf() walks through ParentTrivia. Walking Parent alone would collapse every
        // directive in a file to a single-segment path, reintroducing the collision this helper
        // exists to remove. Anchor them explicitly instead.
        if (node is StructuredTriviaSyntax trivia)
            return ForStructuredTrivia(trivia);

        var segments = new List<string>();
        for (SyntaxNode? current = node; current is not null; current = current.Parent)
        {
            var named = IsNamedDeclaration(current);
            var description = DescribeNode(current);
            var segment = description + SiblingIdentity(current, named ? description : null, cache);
            // Length prefixes make paths unambiguous even if source text contains '/' or ':'.
            segments.Add($"{segment.Length}:{segment}");
        }

        segments.Reverse();
        return string.Join("/", segments);
    }

    /// <summary>
    /// Builds a total path from the document root to <paramref name="element"/>.
    /// </summary>
    /// <param name="element">The XML element to identify.</param>
    /// <returns>A length-prefixed, slash-separated path with one segment per ancestor-or-self.</returns>
    /// <remarks>
    /// XML has no declaration headers, so every element uses local name plus same-name sibling
    /// ordinal, with a group fingerprint when the group has more than one member. The same
    /// totality argument applies: an element name nobody anticipated still contributes a segment.
    /// </remarks>
    public static string ForXmlElement(XElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        var segments = new List<string>();
        for (XElement? current = element; current is not null; current = current.Parent)
        {
            var name = current.Name.LocalName;
            // Select siblings by fully qualified name, not local name. Elements(XName) with a bare
            // string matches only no-namespace elements, so a namespaced document — every real
            // .csproj carries the MSBuild namespace — would see each element as its own sole
            // sibling and every occurrence would collapse to #1of1.
            var siblings = current.Parent is null
                ? new List<XElement> { current }
                : current.Parent.Elements(current.Name).ToList();

            // The namespace participates in the segment text. Two elements can share a local name
            // while living in different namespaces; without this they would render identically
            // even though they are distinct siblings.
            var qualified = current.Name.NamespaceName.Length == 0
                ? name
                : $"{{{current.Name.NamespaceName}}}{name}";

            var ordinal = siblings.FindIndex(sibling => ReferenceEquals(sibling, current)) + 1;
            if (ordinal <= 0)
            {
                // Defensive: the element was not found in its own sibling list.
                siblings = new List<XElement> { current };
                ordinal = 1;
            }

            var position = $"#{ordinal}of{siblings.Count}";
            var segment = siblings.Count == 1
                ? $"{qualified}{position}"
                : $"{qualified}{position}@{Fingerprint(siblings.Select(sibling => sibling.ToString()))}";
            segments.Add($"{segment.Length}:{segment}");
        }

        segments.Reverse();
        return string.Join("/", segments);
    }

    /// <summary>
    /// Builds the baseline identity for a syntax node: structural path plus full node text.
    /// </summary>
    /// <param name="node">The node to identify.</param>
    /// <param name="cache">
    /// Optional per-analysis cache. Supplying one avoids re-enumerating and re-fingerprinting the
    /// same sibling group for every occurrence, which is quadratic without it.
    /// </param>
    /// <returns>A string suitable for hashing as a baseline identity.</returns>
    /// <remarks>
    /// The full node text is used, never a display-truncated snippet. Rules that truncate for
    /// readability must pass the untruncated node here.
    /// </remarks>
    public static string IdentityFor(SyntaxNode node, StructuralPathCache? cache = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        return $"{ForNode(node, cache)}\n{node}";
    }

    /// <summary>
    /// Builds the baseline identity for an XML element: structural path plus full element text.
    /// </summary>
    /// <param name="element">The element to identify.</param>
    /// <returns>A string suitable for hashing as a baseline identity.</returns>
    public static string IdentityFor(XElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return $"{ForXmlElement(element)}\n{element}";
    }

    /// <summary>
    /// Determines whether a node is a declaration whose header identifies it.
    /// </summary>
    private static bool IsNamedDeclaration(SyntaxNode node) =>
        node is MemberDeclarationSyntax and not GlobalStatementSyntax
        or LocalFunctionStatementSyntax;

    /// <summary>
    /// Describes every syntax node, with a generic header identity for named declarations.
    /// </summary>
    private static string DescribeNode(SyntaxNode node)
    {
        if (IsNamedDeclaration(node))
            return $"{node.Kind()} {DeclarationHeader(node)}";

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
    /// Identifies a node's position and the contents of its same-kind sibling group.
    /// </summary>
    /// <param name="node">The node to identify.</param>
    /// <param name="namedIdentity">When non-null, only siblings matching this description are grouped.</param>
    /// <param name="cache">Optional per-analysis cache for the sibling group work.</param>
    private static string SiblingIdentity(
        SyntaxNode node,
        string? namedIdentity = null,
        StructuralPathCache? cache = null)
    {
        if (node.Parent is null)
            return "#1of1";

        // Without a cache this enumerates the parent's children and fingerprints the whole group
        // once per occurrence, which is quadratic for a group with many members.
        var group = cache is null
            ? ComputeGroup(node, namedIdentity)
            : cache.GetGroup(node, namedIdentity);

        if (namedIdentity is not null && group.Count == 1)
            return string.Empty;

        var position = $"#{group.Ordinal}of{group.Count}";
        if (group.Count == 1)
            return position;

        return $"{position}@{group.Fingerprint}";
    }

    /// <summary>
    /// Computes a node's sibling group without caching.
    /// </summary>
    private static StructuralPathCache.GroupInfo ComputeGroup(SyntaxNode node, string? namedIdentity)
    {
        var siblings = node.Parent!.ChildNodes().Where(sibling => sibling.RawKind == node.RawKind
            && (namedIdentity is null || DescribeNode(sibling) == namedIdentity)).ToList();

        var ordinal = siblings.IndexOf(node) + 1;
        if (ordinal <= 0)
        {
            siblings = new List<SyntaxNode> { node };
            ordinal = 1;
        }

        var fingerprint = siblings.Count == 1
            ? null
            : Fingerprint(siblings.Select(sibling => sibling.ToString()));

        return new StructuralPathCache.GroupInfo(ordinal, siblings.Count, fingerprint);
    }

    /// <summary>
    /// Describes a node for sibling grouping. Exposed for the cache, which must apply the same
    /// description rule when it builds a group.
    /// </summary>
    internal static string DescribeNodeForCache(SyntaxNode node) => DescribeNode(node);

    /// <summary>
    /// Builds the path for a structured trivia node, anchored at the node that owns its token.
    /// </summary>
    private static string ForStructuredTrivia(StructuredTriviaSyntax trivia)
    {
        var owner = trivia.ParentTrivia.Token.Parent;
        var prefix = owner is null ? string.Empty : ForNode(owner) + "/";

        var sameKind = owner is null
            ? Array.Empty<StructuredTriviaSyntax>()
            : owner.DescendantTrivia()
                .Where(candidate => candidate.RawKind == trivia.RawKind)
                .Select(candidate => candidate.GetStructure())
                .OfType<StructuredTriviaSyntax>()
                .ToArray();

        if (sameKind.Length == 0)
            sameKind = new[] { trivia };

        var ordinal = Array.FindIndex(sameKind, candidate => candidate.Span == trivia.Span) + 1;
        if (ordinal <= 0)
            ordinal = 1;

        var description = DescribeNode(trivia);
        var position = $"#{ordinal}of{sameKind.Length}";
        // The group fingerprint participates for the same reason it does for ordinary syntax
        // nodes: without it, deleting one directive and appending an identical one lets the new
        // directive inherit the old position's baseline identity. Ordinal plus count alone cannot
        // detect that the group's contents changed.
        var segment = sameKind.Length == 1
            ? $"{description}{position}"
            : $"{description}{position}@{Fingerprint(sameKind.Select(candidate => candidate.ToString()))}";
        return prefix + $"{segment.Length}:{segment}";
    }

    /// <summary>
    /// Hashes a sequence of texts into a stable group fingerprint.
    /// </summary>
    private static string Fingerprint(IEnumerable<string> texts)
    {
        var contents = new StringBuilder();
        foreach (var text in texts)
            contents.Append(text.Length).Append(':').Append(text);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contents.ToString())));
    }
}
