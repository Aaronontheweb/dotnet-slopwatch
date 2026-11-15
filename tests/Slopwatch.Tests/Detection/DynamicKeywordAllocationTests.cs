using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Slopwatch.Detection;
using Slopwatch.Detection.Rules;
using Xunit;

namespace Slopwatch.Tests.Detection;

/// <summary>
/// Guards SW021 against quadratic allocation growth in the number of <c>dynamic</c> occurrences.
/// </summary>
/// <remarks>
/// <para>
/// Issue #144: <see cref="DynamicKeywordRule"/> allocated quadratically because every occurrence
/// rebuilt the whole structural path and re-hashed the concatenation of all same-kind siblings.
/// For a group of N siblings that meant N hashes of N sibling texts — O(N²) in both allocation
/// and hashing.
/// </para>
/// <para>
/// These tests assert the <em>shape</em> of the growth curve rather than absolute byte counts,
/// so they stay meaningful across runtimes and GC configurations. A quadratic regression makes
/// the 2,000-occurrence case roughly four times the 1,000-occurrence case; linear growth keeps
/// it near two.
/// </para>
/// <para>
/// One shape is deliberately excluded from the strict linear assertion: occurrences that all
/// share a single enclosing scope. Each emitted snippet embeds that scope's full text, so the
/// total emitted payload is inherently O(N²) characters. That text is the baseline hash input
/// and cannot be shortened without changing hashes, so the test only bounds its constant factor.
/// </para>
/// </remarks>
public class DynamicKeywordAllocationTests
{
    private const string FilePath = "/tmp/slopwatch-alloc/TestFile.cs";

    [Theory]
    [InlineData(500, 1000)]
    [InlineData(1000, 2000)]
    public void SeparateLocalDeclarationsAllocateLinearly(int smaller, int larger)
    {
        var small = MeasureRuleAllocation(BuildSeparateLocals(smaller));
        var large = MeasureRuleAllocation(BuildSeparateLocals(larger));

        // Linear growth doubles; quadratic growth quadruples. Allow generous headroom for
        // runtime noise while still failing decisively on a quadratic regression.
        Assert.True(
            large < small * 3.0,
            $"Allocation grew super-linearly from {small:N0} bytes at {smaller} occurrences to "
            + $"{large:N0} bytes at {larger} occurrences (ratio {large / (double)small:F2}, "
            + "expected < 3.0). SW021 has likely regressed to quadratic allocation.");
    }

    [Theory]
    [InlineData(500, 1000)]
    [InlineData(1000, 2000)]
    public void NestedBlocksAllocateLinearly(int smaller, int larger)
    {
        var small = MeasureRuleAllocation(BuildNestedBlocks(smaller));
        var large = MeasureRuleAllocation(BuildNestedBlocks(larger));

        Assert.True(
            large < small * 3.0,
            $"Allocation grew super-linearly from {small:N0} bytes at {smaller} occurrences to "
            + $"{large:N0} bytes at {larger} occurrences (ratio {large / (double)small:F2}, "
            + "expected < 3.0). SW021 has likely regressed to quadratic allocation.");
    }

    [Fact]
    public void SharedScopeAllocationStaysWithinConstantFactorOfEmittedPayload()
    {
        // Every occurrence in one signature shares a single scope, so the emitted snippets
        // themselves are quadratic. Bound the overhead above that unavoidable payload: the
        // rule must not allocate several times the text it actually emits.
        const int count = 2000;
        var code = BuildOneSignature(count);

        var payloadBytes = MeasureEmittedPayloadBytes(code);
        var allocated = MeasureRuleAllocation(code);

        Assert.True(
            allocated < payloadBytes * 3.0,
            $"Rule allocated {allocated:N0} bytes for {count} occurrences in one signature, more "
            + $"than 3x the {payloadBytes:N0} bytes of snippet text it emitted. Path or sibling "
            + "caching has likely regressed.");
    }

    [Fact]
    public void SiblingFingerprintIsComputedOncePerGroup()
    {
        // A wide sibling group is the shape that made the fingerprint O(N^2): each of the N
        // members used to hash all N sibling texts. With per-group caching the cost is linear.
        var small = MeasureRuleAllocation(BuildWideSiblingGroup(500));
        var large = MeasureRuleAllocation(BuildWideSiblingGroup(2000));

        Assert.True(
            large < small * 6.0,
            $"Allocation grew from {small:N0} bytes at 500 siblings to {large:N0} bytes at 2000 "
            + $"(ratio {large / (double)small:F2}, expected < 6.0 for a 4x input increase). "
            + "Sibling fingerprints are likely being recomputed per member.");
    }

    /// <summary>
    /// Measures bytes allocated on the current thread by one rule analysis, after warm-up.
    /// </summary>
    private static long MeasureRuleAllocation(string code)
    {
        var tree = CSharpSyntaxTree.ParseText(code);

        // Warm up JIT and Roslyn's lazy structures so they do not land in the measured window.
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

    /// <summary>
    /// Measures the UTF-16 byte size of every snippet the rule emits.
    /// </summary>
    private static long MeasureEmittedPayloadBytes(string code)
    {
        var tree = CSharpSyntaxTree.ParseText(code);
        long chars = 0;
        foreach (var result in RunRuleCollect(tree, code))
            chars += result.CodeSnippet?.Length ?? 0;

        return chars * sizeof(char);
    }

    private static int RunRule(Microsoft.CodeAnalysis.SyntaxTree tree, string code)
        => RunRuleCollect(tree, code).Count;

    private static List<DetectionResult> RunRuleCollect(
        Microsoft.CodeAnalysis.SyntaxTree tree, string code)
    {
        var context = new DetectionContext(FilePath, "TestFile.cs", code, tree, false);
        var results = new List<DetectionResult>();
        var enumerator = new DynamicKeywordRule().AnalyzeAsync(context).GetAsyncEnumerator();
        try
        {
            while (enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult())
                results.Add(enumerator.Current);
        }
        finally
        {
            enumerator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        return results;
    }

    /// <summary>
    /// N occurrences in separate local declarations: N same-kind statement siblings.
    /// </summary>
    private static string BuildSeparateLocals(int count)
    {
        var sb = new StringBuilder("class C { void M() {");
        for (var i = 0; i < count; i++)
            sb.Append(" dynamic x").Append(i).Append(" = ").Append(i).Append(';');
        sb.Append(" } }");
        return sb.ToString();
    }

    /// <summary>
    /// N occurrences spread across N sibling blocks: exercises the shared ancestor prefix.
    /// </summary>
    private static string BuildNestedBlocks(int count)
    {
        var sb = new StringBuilder("class C { void M() {");
        for (var i = 0; i < count; i++)
            sb.Append(" { dynamic x").Append(i).Append(" = ").Append(i).Append("; }");
        sb.Append(" } }");
        return sb.ToString();
    }

    /// <summary>
    /// N occurrences inside one method signature: one wide same-kind sibling group.
    /// </summary>
    private static string BuildOneSignature(int count)
    {
        var sb = new StringBuilder("class C { void M(");
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append("dynamic p").Append(i);
        }

        sb.Append(") { } }");
        return sb.ToString();
    }

    /// <summary>
    /// N occurrences in N sibling blocks, each holding one occurrence: a wide group of blocks.
    /// </summary>
    private static string BuildWideSiblingGroup(int count)
    {
        var sb = new StringBuilder("class C { void M() {");
        for (var i = 0; i < count; i++)
            sb.Append(" { dynamic x").Append(i).Append(" = ").Append(i).Append("; }");
        sb.Append(" } }");
        return sb.ToString();
    }
}
