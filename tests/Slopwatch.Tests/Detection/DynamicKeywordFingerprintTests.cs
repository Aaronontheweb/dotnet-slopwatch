using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
        var contents = new StringBuilder();
        foreach (var text in texts)
            contents.Append(text.Length).Append(':').Append(text);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contents.ToString())));
    }

    /// <summary>
    /// Calls the rule's private streaming fingerprint via reflection.
    /// </summary>
    private static string InvokeFingerprint(List<SyntaxNode> siblings)
    {
        var method = typeof(DynamicKeywordRule).GetMethod(
            "FingerprintSiblings",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("FingerprintSiblings not found.");

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
