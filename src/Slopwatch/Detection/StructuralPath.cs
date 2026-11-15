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
            return ForStructuredTrivia(trivia, cache);

        var segments = new List<string>();
        for (SyntaxNode? current = node; current is not null; current = current.Parent)
        {
            // Collect the ancestor chain, then join the cached segments once. Each segment is
            // rendered at most once per node, so total work is linear in the number of distinct
            // nodes touched rather than quadratic in depth.
            segments.Add(cache is null
                ? RenderSegment(current, cache)
                : cache.GetSegment(current, n => RenderSegment(n, cache)));
        }

        segments.Reverse();
        return string.Join("/", segments);
    }

    /// <summary>
    /// Renders one length-prefixed path segment.
    /// </summary>
    private static string RenderSegment(SyntaxNode node, StructuralPathCache? cache)
    {
        var named = IsNamedDeclaration(node);
        var description = DescribeNode(node);
        var segment = description + SiblingIdentity(node, named ? description : null, cache);
        // Length prefixes make paths unambiguous even if source text contains '/' or ':'.
        return $"{segment.Length}:{segment}";
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
    public static string ForXmlElement(XElement element, StructuralPathCache? cache = null)
    {
        ArgumentNullException.ThrowIfNull(element);

        var segments = new List<string>();
        for (XElement? current = element; current is not null; current = current.Parent)
        {
            var name = current.Name.LocalName;

            // The namespace participates in the segment text. Two elements can share a local name
            // while living in different namespaces; without this they would render identically
            // even though they are distinct siblings.
            var qualified = current.Name.NamespaceName.Length == 0
                ? name
                : $"{{{current.Name.NamespaceName}}}{name}";

            // Select siblings by fully qualified name, not local name. Elements(XName) with a bare
            // string matches only no-namespace elements, so a namespaced document — every real
            // .csproj carries the MSBuild namespace — would see each element as its own sole
            // sibling and every occurrence would collapse to #1of1.
            //
            // Without a cache this enumerates, serializes, and fingerprints the whole group once
            // per finding, which is quadratic: 57 GB at 2,000 findings.
            var group = cache is null
                ? BuildXmlGroup(current)
                : cache.GetXmlGroup(current);

            var position = $"#{group.Ordinal}of{group.Count}";
            var segment = group.Count == 1
                ? $"{qualified}{position}"
                : $"{qualified}{position}@{group.Fingerprint}";
            segments.Add($"{segment.Length}:{segment}");
        }

        segments.Reverse();
        return string.Join("/", segments);
    }

    /// <summary>
    /// Builds an XML element's sibling group without caching.
    /// </summary>
    private static StructuralPathCache.XmlGroup BuildXmlGroup(XElement element)
    {
        var siblings = element.Parent is null
            ? new List<XElement> { element }
            : element.Parent.Elements(element.Name).ToList();

        var ordinal = siblings.FindIndex(sibling => ReferenceEquals(sibling, element)) + 1;
        if (ordinal <= 0)
        {
            // Defensive: the element was not found in its own sibling list.
            siblings = new List<XElement> { element };
            ordinal = 1;
        }

        var fingerprint = siblings.Count == 1
            ? null
            : Fingerprint(siblings.Select(sibling => sibling.ToString()));

        return new StructuralPathCache.XmlGroup(ordinal, siblings.Count, fingerprint);
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
    public static string IdentityFor(XElement element, StructuralPathCache? cache = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        return $"{ForXmlElement(element, cache)}\n{element}";
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
            : FingerprintNodes(siblings);

        return new StructuralPathCache.GroupInfo(ordinal, siblings.Count, fingerprint);
    }

    /// <summary>
    /// Describes a node for sibling grouping. Exposed for the cache, which must apply the same
    /// description rule when it builds a group.
    /// </summary>
    internal static string DescribeNodeForCache(SyntaxNode node) => DescribeNode(node);

    /// <summary>
    /// Collects the directive group for a structured trivia node without caching.
    /// </summary>
    private static DirectiveGroup BuildDirectiveGroup(
        SyntaxNode? owner,
        StructuredTriviaSyntax trivia)
    {
        var sameKind = owner is null
            ? Array.Empty<StructuredTriviaSyntax>()
            : owner.DescendantTrivia()
                .Where(candidate => candidate.RawKind == trivia.RawKind)
                .Select(candidate => candidate.GetStructure())
                .OfType<StructuredTriviaSyntax>()
                .ToArray();

        if (sameKind.Length == 0)
            sameKind = new[] { trivia };

        var fingerprint = sameKind.Length == 1
            ? null
            : FingerprintNodes(sameKind);

        var ordinals = new Dictionary<StructuredTriviaSyntax, int>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < sameKind.Length; i++)
            ordinals[sameKind[i]] = i + 1;

        return new DirectiveGroup(sameKind, fingerprint, ordinals);
    }

    /// <summary>
    /// A directive group's members, its shared fingerprint, and per-member ordinals.
    /// </summary>
    internal readonly record struct DirectiveGroup(
        StructuredTriviaSyntax[] Members,
        string? Fingerprint,
        Dictionary<StructuredTriviaSyntax, int> Ordinals);

    /// <summary>
    /// Builds the path for a structured trivia node, anchored at the node that owns its token.
    /// </summary>
    private static string ForStructuredTrivia(StructuredTriviaSyntax trivia, StructuralPathCache? cache = null)
    {
        var owner = trivia.ParentTrivia.Token.Parent;
        var prefix = owner is null ? string.Empty : ForNode(owner, cache) + "/";

        // Structured trivia reports a null Parent, so it cannot use the ordinary sibling-group
        // cache. Cache the directive group per owner instead: without this, every directive in a
        // file re-enumerates and re-fingerprints the whole group, which is quadratic.
        var group = cache is null
            ? BuildDirectiveGroup(owner, trivia)
            : cache.GetDirectiveGroup(owner, trivia);

        // Use the cached ordinal map rather than scanning the member array. Array.FindIndex over
        // N members, once per directive, is N^2 comparisons: 2,001,000 at 2,000 directives, and
        // 3.47 seconds at 16,000. Allocation tests cannot see this because it allocates nothing.
        var ordinal = group.Ordinals.TryGetValue(trivia, out var found) ? found : 1;

        var description = DescribeNode(trivia);
        var position = $"#{ordinal}of{group.Members.Length}";
        // The group fingerprint participates for the same reason it does for ordinary syntax
        // nodes: without it, deleting one directive and appending an identical one lets the new
        // directive inherit the old position's baseline identity. Ordinal plus count alone cannot
        // detect that the group's contents changed.
        var segment = group.Members.Length == 1
            ? $"{description}{position}"
            : $"{description}{position}@{group.Fingerprint}";
        return prefix + $"{segment.Length}:{segment}";
    }

    /// <summary>
    /// Hashes a sequence of texts into a stable group fingerprint.
    /// </summary>
    internal static string FingerprintForCache(IEnumerable<string> texts) => Fingerprint(texts);

    /// <summary>
    /// Hashes a sibling group's source text into a stable fingerprint without materializing it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The input format is unchanged from the StringBuilder version: for each sibling, the decimal
    /// length, a colon, then the text. Streaming produces byte-identical digests while avoiding the
    /// intermediate copies.
    /// </para>
    /// <para>
    /// This matters for nested groups. A StringBuilder version materializes every sibling's text,
    /// concatenates them, then allocates the UTF-8 encoding of the whole concatenation. Nested
    /// groups overlap, so each ancestor copies its descendants again: measured 30.7 MB at depth
    /// 800 for a single occurrence, versus 2.0 MB streaming. Hashing still processes overlapping
    /// input, but the complete copies are gone.
    /// </para>
    /// </remarks>
    internal static string FingerprintNodes(IReadOnlyList<SyntaxNode> siblings)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> lengthBuffer = stackalloc byte[16];

        // Pull characters straight from the source text instead of materializing each sibling's
        // string. For nested groups a sibling's text contains all of its descendants, so
        // ToString() allocates O(depth) per level and reintroduces quadratic allocation even
        // when the hash itself streams.
        var sourceText = siblings[0].SyntaxTree!.GetText();
        var charBuffer = new char[ChunkChars];

        foreach (var sibling in siblings)
        {
            var span = sibling.Span;
            // Line endings are normalized before hashing. The fingerprint is a hex digest, so the
            // normalization ComputeStructuralHash applies to the identity string cannot reach it:
            // a baseline created on Linux (LF) would never match the same source on Windows
            // (CRLF). Normalizing here keeps the digest platform-independent.
            AppendLengthPrefix(hash, NormalizedLength(sourceText, span.Start, span.Length), lengthBuffer);
            hash.AppendData(":"u8);
            AppendNormalizedUtf8(hash, sourceText, span.Start, span.Length, charBuffer);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    /// <summary>
    /// Counts a span's characters after line-ending normalization.
    /// </summary>
    /// <remarks>
    /// <c>\r\n</c> collapses to a single <c>\n</c>, and a lone <c>\r</c> becomes <c>\n</c>. The
    /// length prefix must describe the normalized text, otherwise the digest would still differ
    /// between platforms even though the bytes matched.
    /// </remarks>
    private static int NormalizedLength(
        Microsoft.CodeAnalysis.Text.SourceText sourceText,
        int start,
        int length)
    {
        var normalized = 0;
        for (var i = 0; i < length; i++)
        {
            normalized++;
            if (sourceText[start + i] == '\r' && i + 1 < length && sourceText[start + i + 1] == '\n')
                i++;
        }

        return normalized;
    }

    /// <summary>
    /// Appends a source span's UTF-8 bytes to an incremental hash in bounded chunks, with line
    /// endings normalized to <c>\n</c>.
    /// </summary>
    /// <remarks>
    /// Chunk boundaries never split a surrogate pair: a trailing high surrogate is carried into
    /// the next chunk.
    /// </remarks>
    private static void AppendNormalizedUtf8(
        IncrementalHash hash,
        Microsoft.CodeAnalysis.Text.SourceText sourceText,
        int start,
        int length,
        char[] charBuffer)
    {
        Span<byte> byteBuffer = stackalloc byte[ChunkChars * 3];
        var count = 0;
        var pendingCarriageReturn = false;

        for (var i = 0; i < length; i++)
        {
            var c = sourceText[start + i];

            if (pendingCarriageReturn)
            {
                pendingCarriageReturn = false;
                // \r\n collapses to one \n; a lone \r already emitted its \n.
                if (c == '\n')
                    continue;
            }

            if (c == '\r')
            {
                pendingCarriageReturn = true;
                c = '\n';
            }

            charBuffer[count++] = c;

            if (count == ChunkChars)
            {
                var flushCount = count;
                // Never split a surrogate pair across a flush.
                if (char.IsHighSurrogate(charBuffer[count - 1]))
                    flushCount--;

                var written = Encoding.UTF8.GetBytes(charBuffer.AsSpan(0, flushCount), byteBuffer);
                hash.AppendData(byteBuffer[..written]);

                if (flushCount != count)
                    charBuffer[0] = charBuffer[count - 1];
                count -= flushCount;
            }
        }

        if (count > 0)
        {
            var written = Encoding.UTF8.GetBytes(charBuffer.AsSpan(0, count), byteBuffer);
            hash.AppendData(byteBuffer[..written]);
        }
    }

    /// <summary>
    /// Appends a decimal length prefix, matching the previous format exactly.
    /// </summary>
    private static void AppendLengthPrefix(IncrementalHash hash, int length, Span<byte> buffer)
    {
        var digits = 0;
        var value = length;
        do
        {
            buffer[digits++] = (byte)('0' + (value % 10));
            value /= 10;
        }
        while (value > 0);

        for (var i = 0; i < digits / 2; i++)
            (buffer[i], buffer[digits - 1 - i]) = (buffer[digits - 1 - i], buffer[i]);

        hash.AppendData(buffer[..digits]);
    }

    /// <summary>
    /// Appends a source span's UTF-8 bytes to an incremental hash in bounded chunks.
    /// </summary>
    /// <remarks>
    /// Chunk boundaries never split a surrogate pair: the encoder stops before a trailing high
    /// surrogate and carries it into the next chunk.
    /// </remarks>
    private static void AppendUtf8FromSource(
        IncrementalHash hash,
        Microsoft.CodeAnalysis.Text.SourceText sourceText,
        int start,
        int length,
        char[] charBuffer)
    {
        Span<byte> byteBuffer = stackalloc byte[ChunkChars * 3];

        var offset = 0;
        while (offset < length)
        {
            var count = Math.Min(ChunkChars, length - offset);

            // Do not split a surrogate pair across chunks.
            if (offset + count < length && char.IsHighSurrogate(sourceText[start + offset + count - 1]))
                count--;

            sourceText.CopyTo(start + offset, charBuffer, 0, count);
            var written = Encoding.UTF8.GetBytes(charBuffer.AsSpan(0, count), byteBuffer);
            hash.AppendData(byteBuffer[..written]);
            offset += count;
        }
    }

    /// <summary>
    /// Characters read per chunk when streaming source text into the hash.
    /// </summary>
    private const int ChunkChars = 4096;

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
