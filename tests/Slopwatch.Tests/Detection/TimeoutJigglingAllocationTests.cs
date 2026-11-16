using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Slopwatch.Detection;
using Slopwatch.Detection.Rules;
using Xunit;

namespace Slopwatch.Tests.Detection;

/// <summary>
/// Guards SW004 against quadratic allocation growth in the number of <c>Task.Delay</c> occurrences.
/// </summary>
/// <remarks>
/// <para>
/// Issue #142: <see cref="TimeoutJigglingRule"/> allocated quadratically because every occurrence
/// rebuilt the whole structural path and re-hashed the concatenation of all same-kind siblings.
/// For a group of N siblings that meant N hashes of N sibling texts — O(N²) in both allocation
/// and hashing.
/// </para>
/// <para>
/// These tests assert the <em>shape</em> of the growth curve rather than absolute byte counts, so
/// they stay meaningful across runtimes and GC configurations. A quadratic regression makes the
/// 2,000-occurrence case roughly four times the 1,000-occurrence case; linear growth keeps it near
/// two.
/// </para>
/// </remarks>
public class TimeoutJigglingAllocationTests
{
    private const string FilePath = "/tmp/slopwatch-alloc/TestFile.cs";

    [Theory]
    [InlineData(500, 1000)]
    [InlineData(1000, 2000)]
    public void SeparateDelayStatementsAllocateLinearly(int smaller, int larger)
    {
        var small = MeasureRuleAllocation(BuildSeparateDelays(smaller));
        var large = MeasureRuleAllocation(BuildSeparateDelays(larger));

        // Linear growth doubles; quadratic growth quadruples. Allow generous headroom for
        // runtime noise while still failing decisively on a quadratic regression.
        Assert.True(
            large < small * 3.0,
            $"Allocation grew super-linearly from {small:N0} bytes at {smaller} occurrences to "
            + $"{large:N0} bytes at {larger} occurrences (ratio {large / (double)small:F2}, "
            + "expected < 3.0). SW004 has likely regressed to quadratic allocation.");
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

    private static int RunRule(SyntaxTree tree, string code)
        => RunRuleCollect(tree, code).Count;

    private static List<DetectionResult> RunRuleCollect(SyntaxTree tree, string code)
    {
        var context = new DetectionContext(FilePath, "TestFile.cs", code, tree, true);
        var results = new List<DetectionResult>();
        var enumerator = new TimeoutJigglingRule().AnalyzeAsync(context).GetAsyncEnumerator();
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
    /// N occurrences in separate statements: N same-kind statement siblings.
    /// </summary>
    private static string BuildSeparateDelays(int count)
    {
        var body = string.Concat(Enumerable.Repeat(
            "await System.Threading.Tasks.Task.Delay(100);", count));

        return "class C{async System.Threading.Tasks.Task M(){" + body + "}}";
    }

    /// <summary>
    /// N occurrences that are all siblings in one wide group, which is the shape that made the
    /// sibling fingerprint quadratic.
    /// </summary>
    private static string BuildWideSiblingGroup(int count)
    {
        var args = string.Join(", ", Enumerable.Range(0, count)
            .Select(i => $"System.Threading.Tasks.Task.Delay({i})"));

        return "class C{async System.Threading.Tasks.Task M(){await System.Threading.Tasks.Task.WhenAll("
            + args + ");}}";
    }
}
