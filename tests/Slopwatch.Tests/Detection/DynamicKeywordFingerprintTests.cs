using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Slopwatch.Baseline;
using Slopwatch.Detection;
using Slopwatch.Detection.Rules;
using Xunit;

namespace Slopwatch.Tests.Detection;

/// <summary>
/// Pins the sibling-group fingerprint to the exact digest the previous StringBuilder
/// implementation produced, and guards against nested-group allocation regressions.
/// </summary>
/// <remarks>
/// <para>
/// The fingerprint is part of the baseline identity. If streaming changes even one byte of the
/// hash input, every existing baseline entry for a multi-sibling group silently stops matching.
/// These tests compare against a reference implementation of the old algorithm rather than
/// against the new code's own output, so a change in either direction fails.
/// </para>
/// </remarks>
public class DynamicKeywordFingerprintTests
{
    private const string FilePath = "/tmp/slopwatch-alloc/TestFile.cs";

    [Theory]
    [InlineData("class C{void M(){dynamic a=1;dynamic b=2;}}")]
    [InlineData("class C{void M(){dynamic a=1;dynamic b=2;dynamic c=3;}}")]
    [InlineData("class C{void M(){dynamic a=\"héllo\";dynamic b=\"wörld\";}}")]
    [InlineData("class C{void M(){dynamic a=\"😀\";dynamic b=\"🎉\";}}")]
    [InlineData("class C{void M(){dynamic a=\"\";dynamic b=\"x\";}}")]
    public void FingerprintMatchesReferenceImplementation(string code)
    {
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();

        // Every multi-sibling group in the tree must fingerprint identically to the reference.
        var compared = 0;
        foreach (var parent in root.DescendantNodes())
        {
            var byKind = parent.ChildNodes().GroupBy(child => child.RawKind);
            foreach (var group in byKind)
            {
                var siblings = group.ToList();
                if (siblings.Count < 2)
                    continue;

                var expected = ReferenceFingerprint(siblings.Select(s => s.ToString()));
                var actual = InvokeFingerprint(siblings);
                Assert.Equal(expected, actual);
                compared++;
            }
        }

        Assert.True(compared > 0, "Fixture produced no multi-sibling groups to compare.");
    }

    [Fact]
    public void FingerprintHandlesSurrogatePairsAcrossChunkBoundaries()
    {
        // A string of astral-plane characters is 2 UTF-16 chars each. With a 4096-char chunk,
        // a naive split lands mid-surrogate and corrupts the encoding.
        //
        // The sibling text is `dynamic a="` + filler + emoji + `";`. The prefix is 11 chars, so
        // the filler must be 4095 - 11 = 4084 chars for the high surrogate to land at offset 4095
        // (the last char of chunk 1) and the low surrogate at 4096 (the first char of chunk 2).
        // Getting this offset wrong silently produces a test that never splits a pair.
        const string prefix = "dynamic a=\"";
        var filler = new string('a', 4095 - prefix.Length);
        var code = "class C{void M(){" + prefix + filler + "😀\";dynamic b=\"x\";}}";

        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();

        var compared = 0;
        foreach (var parent in root.DescendantNodes())
        {
            foreach (var group in parent.ChildNodes().GroupBy(child => child.RawKind))
            {
                var siblings = group.ToList();
                if (siblings.Count < 2)
                    continue;

                Assert.Equal(
                    ReferenceFingerprint(siblings.Select(s => s.ToString())),
                    InvokeFingerprint(siblings));
                compared++;
            }
        }

        Assert.True(compared > 0, "Fixture produced no multi-sibling groups to compare.");
    }

    [Fact]
    public void NestedGroupsAllocateSubLinearlyInDepth()
    {
        // Nested sibling groups overlap: each ancestor's fingerprint input contains its
        // descendants. A StringBuilder implementation copies those descendants again at every
        // level, so allocation grows quadratically with depth. Streaming keeps it near linear.
        var shallow = MeasureRuleAllocation(BuildNested(100));
        var deep = MeasureRuleAllocation(BuildNested(400));

        // Depth quadruples; quadratic growth would be ~16x. Allow generous headroom.
        Assert.True(
            deep < shallow * 8.0,
            $"Allocation grew from {shallow:N0} bytes at depth 100 to {deep:N0} bytes at depth 400 "
            + $"(ratio {deep / (double)shallow:F2}, expected < 8.0 for a 4x depth increase). "
            + "Sibling fingerprints are likely copying nested group text again.");
    }

    /// <summary>
    /// The previous implementation, kept verbatim as the reference for digest equality.
    /// </summary>
    private static string ReferenceFingerprint(IEnumerable<string> texts)
    {
        // Line endings are normalized before hashing. The fingerprint is a hex digest, so the
        // normalization ComputeStructuralHash applies to the identity string cannot reach inside
        // it: without this, a baseline created on Linux (LF) would never match the same source on
        // Windows (CRLF). This is a deliberate deviation from the original StringBuilder
        // implementation, which hashed raw spans.
        var contents = new StringBuilder();
        foreach (var text in texts)
        {
            var normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
            contents.Append(normalized.Length).Append(':').Append(normalized);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contents.ToString())));
    }

    [Fact]
    public void FingerprintIsIdenticalForLfAndCrlfSources()
    {
        // A baseline created on Linux must match the same source on Windows. The fingerprint is a
        // hex digest, so the line-ending normalization ComputeStructuralHash applies to the
        // identity string cannot reach inside it. Without normalization here, every multi-sibling
        // group would produce a different digest per platform and baselines would not be portable.
        //
        // The siblings must SPAN MULTIPLE LINES for this to be meaningful: a group of one-line
        // statements contains no line breaks inside its spans, so it fingerprints identically
        // whether or not normalization happens. Two multiline methods are the shape that exposes
        // the difference.
        //
        // The literal is normalized to LF first. A raw string literal inherits the line endings of
        // the source file, so on a Windows checkout it already contains CRLF and a bare
        // Replace("\n", "\r\n") would produce CRCRLF.
        const string raw = """
            class C
            {
                void A()
                {
                    dynamic a = 1;
                }

                void B()
                {
                    dynamic b = 2;
                }
            }
            """;
        var lf = raw.Replace("\r\n", "\n");
        var crlf = lf.Replace("\n", "\r\n");

        Assert.DoesNotContain('\r', lf);
        Assert.Contains("\r\n", crlf);

        var lfSiblings = MultiSiblingGroups(lf);
        var crlfSiblings = MultiSiblingGroups(crlf);

        Assert.NotEmpty(lfSiblings);
        Assert.Equal(lfSiblings.Count, crlfSiblings.Count);

        // Guard the premise: at least one compared group must actually contain a line break,
        // otherwise this test would pass for the wrong reason.
        var comparedMultiline = 0;
        for (var i = 0; i < lfSiblings.Count; i++)
        {
            if (lfSiblings[i].Any(sibling => sibling.ToString().Contains('\n')))
                comparedMultiline++;

            Assert.Equal(
                InvokeFingerprint(lfSiblings[i]),
                InvokeFingerprint(crlfSiblings[i]));
        }

        Assert.True(
            comparedMultiline > 0,
            "No compared sibling group spanned multiple lines, so line-ending normalization was "
            + "never exercised.");
    }

    /// <summary>
    /// Collects every multi-sibling group in a source text, in a stable order.
    /// </summary>
    private static List<List<SyntaxNode>> MultiSiblingGroups(string code)
    {
        var root = CSharpSyntaxTree.ParseText(code).GetRoot();
        var groups = new List<List<SyntaxNode>>();
        foreach (var parent in root.DescendantNodes())
        {
            foreach (var group in parent.ChildNodes().GroupBy(child => child.RawKind))
            {
                var siblings = group.ToList();
                if (siblings.Count >= 2)
                    groups.Add(siblings);
            }
        }

        return groups;
    }

    [Fact]
    public void StructuralPathIsIdenticalForLfAndCrlfSources()
    {
        // A path segment can contain a multiline literal, so the raw segment text differs between
        // an LF and a CRLF checkout. The length prefix must describe the NORMALIZED text:
        // normalizing only the final identity string would leave a numeric prefix that still
        // differs per platform, and the same source would produce a different baseline identity.
        //
        // The literal sits in a catch filter rather than an attribute argument. Attributes are
        // excluded from declaration headers (see DeclarationHeader), so an attribute fixture would
        // no longer put a newline in any segment and the length prefix would go unexercised.
        const string raw = """
            class C
            {
                void M()
                {
                    try { }
                    catch (System.Exception e) when (e.Message == @"line1
            line2")
                    {
                        dynamic a = 1;
                    }
                }
            }
            """;
        var lf = raw.Replace("\r\n", "\n");
        var crlf = lf.Replace("\n", "\r\n");

        Assert.DoesNotContain('\r', lf);

        var lfPaths = DynamicPaths(lf);
        var crlfPaths = DynamicPaths(crlf);

        Assert.NotEmpty(lfPaths);
        Assert.Equal(lfPaths.Count, crlfPaths.Count);

        // Guard the premise: at least one path must contain a segment whose text spans lines,
        // otherwise the length prefix was never exercised.
        Assert.Contains(lfPaths, path => path.Contains('\n'));

        for (var i = 0; i < lfPaths.Count; i++)
            Assert.Equal(lfPaths[i], crlfPaths[i]);
    }

    [Fact]
    public void AddingUnrelatedAttributeDoesNotChangeNestedIdentity()
    {
        // A declaration header is an ancestor segment for every finding nested inside it. If
        // attributes participate in that header, adding an unrelated [Collection], [Trait], or
        // [SuppressMessage] to a class silently un-baselines every finding in the class -- the
        // user regenerates a baseline that disagrees with the next analysis.
        const string withoutAttribute = """
            class C
            {
                void M()
                {
                    try { }
                    catch (System.IO.IOException) { }
                }
            }
            """;
        const string withAttribute = """
            [System.Diagnostics.CodeAnalysis.SuppressMessage("A", "B")]
            class C
            {
                void M()
                {
                    try { }
                    catch (System.IO.IOException) { }
                }
            }
            """;

        var before = CatchIdentities(withoutAttribute);
        var after = CatchIdentities(withAttribute);

        Assert.Single(before);
        Assert.Single(after);
        Assert.Equal(before[0], after[0]);
    }

    [Fact]
    public void ChangingTheReportedNodesOwnAttributeChangesItsIdentity()
    {
        // Excluding attributes from ANCESTOR headers must not make attributes invisible
        // everywhere. The reported node's own text still carries its attributes, so a finding
        // reported on an attribute changes identity when that attribute changes.
        const string first = """
            class C
            {
                [System.Diagnostics.CodeAnalysis.SuppressMessage("A", "B")]
                void M() { }
            }
            """;
        const string second = """
            class C
            {
                [System.Diagnostics.CodeAnalysis.SuppressMessage("A", "C")]
                void M() { }
            }
            """;

        var firstIdentity = MethodIdentity(first);
        var secondIdentity = MethodIdentity(second);

        Assert.NotEqual(firstIdentity, secondIdentity);
    }

    /// <summary>
    /// Collects the structural identity of every catch clause in a source text.
    /// </summary>
    private static List<string> CatchIdentities(string code)
    {
        var root = CSharpSyntaxTree.ParseText(code).GetRoot();
        return root.DescendantNodes()
            .OfType<CatchClauseSyntax>()
            .Select(catchClause => StructuralPath.IdentityFor(catchClause))
            .ToList();
    }

    /// <summary>
    /// Collects the structural identity of every method declaration in a source text.
    /// </summary>
    private static List<string> MethodIdentities(string code)
    {
        var root = CSharpSyntaxTree.ParseText(code).GetRoot();
        return root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Select(method => StructuralPath.IdentityFor(method))
            .ToList();
    }

    /// <summary>
    /// Returns the single method declaration's structural identity in a source text.
    /// </summary>
    private static string MethodIdentity(string code)
    {
        var identities = MethodIdentities(code);
        Assert.Single(identities);
        return identities[0];
    }

    [Fact]
    public void MixedLineEndingsDoNotSplitSiblingGroups()
    {
        // Two partial declarations whose headers differ ONLY by LF versus CRLF must land in the
        // SAME sibling group. If the grouping key stays raw, each becomes a singleton whose
        // rendered paths then collapse to one hash -- baselining one silently exempts the other.
        //
        // The attribute must sit on the CLASS, so it participates in the class's declaration
        // header, which is the grouping key. An attribute on a member would not exercise this.
        const string lfAttribute =
            "[System.Diagnostics.CodeAnalysis.SuppressMessage(\"A\", \"B\", Justification = @\"line1\nline2\")]";
        var crlfAttribute = lfAttribute.Replace("\n", "\r\n");

        var code =
            lfAttribute + "\npartial class C\n{\n    void M() { }\n}\n"
            + crlfAttribute + "\npartial class C\n{\n    void M() { }\n}\n";

        var root = CSharpSyntaxTree.ParseText(code).GetRoot();
        var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>().ToList();
        Assert.Equal(2, classes.Count);

        // Guard the premise: the raw headers really do differ only by line endings.
        Assert.NotEqual(classes[0].ToString(), classes[1].ToString());
        Assert.Equal(
            classes[0].ToString().Replace("\r\n", "\n"),
            classes[1].ToString().Replace("\r\n", "\n"));

        var identities = classes.Select(c => StructuralPath.IdentityFor(c)).ToList();
        var hashes = identities
            .Select(i => BaselineEntry.ComputeStructuralHash("SW002", "Test.cs", i))
            .ToList();

        // The hashes must differ. If they collide, baselining the first occurrence exempts the
        // second -- the exact false negative this identity scheme exists to prevent.
        Assert.NotEqual(hashes[0], hashes[1]);
    }

    /// <summary>
    /// Collects the structural path of every <c>dynamic</c> occurrence in a source text.
    /// </summary>
    private static List<string> DynamicPaths(string code)
    {
        var root = CSharpSyntaxTree.ParseText(code).GetRoot();
        return root.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Where(identifier => identifier.Identifier.ValueText == "dynamic")
            .Select(identifier => StructuralPath.ForNode(identifier))
            .ToList();
    }

    /// <summary>
    /// Calls the rule's private streaming fingerprint via reflection.
    /// </summary>
    private static string InvokeFingerprint(List<SyntaxNode> siblings)
    {
        // The fingerprint moved to the shared StructuralPath helper when the identity logic was
        // extracted out of DynamicKeywordRule. It is internal, so reach it through the assembly
        // rather than through the rule type.
        var method = typeof(StructuralPath).GetMethod(
            "FingerprintNodes",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("FingerprintNodes not found.");

        return (string)method.Invoke(null, new object[] { siblings })!;
    }

    private static long MeasureRuleAllocation(string code)
    {
        var tree = CSharpSyntaxTree.ParseText(code);
        RunRule(tree, code);
        RunRule(tree, code);

        var best = long.MaxValue;
        for (var i = 0; i < 3; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            RunRule(tree, code);
            var after = GC.GetAllocatedBytesForCurrentThread();
            best = Math.Min(best, after - before);
        }

        return best;
    }

    private static int RunRule(SyntaxTree tree, string code)
    {
        var context = new DetectionContext(FilePath, "TestFile.cs", code, tree, false);
        var count = 0;
        var enumerator = new DynamicKeywordRule().AnalyzeAsync(context).GetAsyncEnumerator();
        try
        {
            while (enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult())
                count++;
        }
        finally
        {
            enumerator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        return count;
    }

    /// <summary>
    /// One occurrence inside <paramref name="depth"/> nested blocks, so every level contributes a
    /// sibling group whose text contains all deeper levels.
    /// </summary>
    private static string BuildNested(int depth)
    {
        var open = string.Concat(Enumerable.Repeat("{ {} ", depth));
        var close = new string('}', depth);
        return "class C { void M(){" + open + "dynamic x=1;" + close + "} }";
    }
}
