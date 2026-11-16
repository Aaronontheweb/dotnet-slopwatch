using Microsoft.CodeAnalysis.CSharp;
using Slopwatch.Baseline;
using Slopwatch.Detection;
using Slopwatch.Detection.Rules;
using Xunit;

namespace Slopwatch.Tests.Baseline;

/// <summary>
/// End-to-end guards that baselining one finding never exempts a different one.
/// </summary>
/// <remarks>
/// <para>
/// These tests go through the real baseline machinery rather than comparing identity strings.
/// That distinction matters: the structural identities were already distinct, but the baseline
/// hash normalized them through a line-trimming normalizer, so two findings whose only difference
/// was whitespace inside a multiline literal collapsed to one hash. Baselining the first silently
/// exempted the second — the exact collision issue #142 exists to fix.
/// </para>
/// <para>
/// A test that only compares identity strings passes while the bug is live. Only an end-to-end
/// membership assertion catches it.
/// </para>
/// </remarks>
public class BaselineMembershipTests
{
    private const string RootDirectory = "/tmp/slopwatch-baseline";
    private const string FilePath = RootDirectory + "/Test.cs";

    [Fact]
    public void DistinctFindingsDifferingOnlyInLiteralWhitespaceAreNotBothBaselined()
    {
        // Two [SuppressMessage] attributes whose arguments differ only in whitespace inside a
        // multiline verbatim string literal. The structural identities differ; the baseline
        // hashes must differ too.
        var source = """
using System.Diagnostics.CodeAnalysis;
partial class C {
  [SuppressMessage("x", FIRST)] partial void M();
  [SuppressMessage("x", SECOND)]
  partial void M() => System.Console.WriteLine();
}
""".Replace("FIRST", "@\"\n a\nb\"")
   .Replace("SECOND", "@\"\na \nb\"");

        var results = RunRule(source);
        Assert.Equal(2, results.Count);

        // The identities themselves must differ.
        Assert.NotEqual(results[0].BaselineIdentity, results[1].BaselineIdentity);

        // And the baseline hashes must differ, which is what actually decides membership.
        var hashes = results
            .Select(r => BaselineEntry.ComputeStructuralHash(r.RuleId, "Test.cs", r.BaselineIdentity))
            .ToList();
        Assert.NotEqual(hashes[0], hashes[1]);

        // End-to-end: baselining the first must not exempt the second. Both calls must use the
        // same root directory, or the relative paths differ and the assertion passes vacuously.
        var baseline = new BaselineFile();
        Assert.True(baseline.AddEntry(results[0], RootDirectory));
        Assert.True(baseline.IsBaselined(results[0], RootDirectory), "The baselined finding is not reported as baselined.");
        Assert.False(baseline.IsBaselined(results[1], RootDirectory), "Baselining the first finding exempted the second.");
    }

    [Fact]
    public void BaseliningOneFindingDoesNotExemptAnIdenticalSibling()
    {
        // Two byte-identical statements. Their identities differ only by ordinal, so baselining
        // one must leave the other unbaselined.
        const string source = "class C{void M(){Task.Delay(100);Task.Delay(100);}}";

        var results = RunRule(source, isTestFile: true);
        Assert.Equal(2, results.Count);

        var baseline = new BaselineFile();
        Assert.True(baseline.AddEntry(results[0], RootDirectory));
        Assert.True(baseline.IsBaselined(results[0], RootDirectory));
        Assert.False(baseline.IsBaselined(results[1], RootDirectory));
    }

    [Fact]
    public void StructuralHashPreservesWhitespaceInsideLiterals()
    {
        // Direct guard on the hash function: trimming lines must not erase literal content.
        var withTwoSpaces = "class C{void M(){F(@\"\n  \n\");}}";
        var withOneSpace = "class C{void M(){F(@\"\n \n\");}}";

        Assert.NotEqual(
            BaselineEntry.ComputeStructuralHash("SW001", "Test.cs", withTwoSpaces),
            BaselineEntry.ComputeStructuralHash("SW001", "Test.cs", withOneSpace));
    }

    [Fact]
    public void StructuralHashIsStableAcrossLineEndings()
    {
        // CRLF and LF must hash identically so baselines survive a platform change.
        var lf = "class C{void M(){F(@\"\na\n\");}}";
        var crlf = "class C{void M(){F(@\"\r\na\r\n\");}}";

        Assert.Equal(
            BaselineEntry.ComputeStructuralHash("SW001", "Test.cs", lf),
            BaselineEntry.ComputeStructuralHash("SW001", "Test.cs", crlf));
    }

    private static List<DetectionResult> RunRule(string source, bool isTestFile = false)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var context = new DetectionContext(FilePath, "Test.cs", source, tree, isTestFile);
        var rule = isTestFile ? (IDetectionRule)new TimeoutJigglingRule() : new WarningSuppressRule();

        var results = new List<DetectionResult>();
        var enumerator = rule.AnalyzeAsync(context).GetAsyncEnumerator();
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
}
