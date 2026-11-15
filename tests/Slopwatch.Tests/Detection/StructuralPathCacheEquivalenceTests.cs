using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Slopwatch.Detection;
using Slopwatch.Detection.Rules;
using Xunit;

namespace Slopwatch.Tests.Detection;

/// <summary>
/// Asserts that <see cref="StructuralPathCache"/> is a pure optimization: every identity it
/// produces must be byte-identical to the uncached path.
/// </summary>
/// <remarks>
/// <para>
/// This is the guard that was missing when the cache was introduced. The cache key identifies a
/// sibling <em>group</em>, but the first version stored the first requested node's ordinal in the
/// cached entry, so every later sibling in that group received the same ordinal. Two identical
/// statements then produced one identity instead of two, which reintroduced exactly the baseline
/// collision issue #142 exists to fix — and the existing tests still passed, because they only
/// checked that identities were distinct within a single uncached run.
/// </para>
/// <para>
/// These tests compare cached against uncached output for the same input, so any divergence
/// between the two paths fails regardless of which one is "correct".
/// </para>
/// </remarks>
public class StructuralPathCacheEquivalenceTests
{
    private const string FilePath = "/tmp/slopwatch-alloc/TestFile.cs";

    [Theory]
    [InlineData("class C{void M(){Task.Delay(100);Task.Delay(100);}}")]
    [InlineData("class C{void M(){Task.Delay(100);Task.Delay(200);Task.Delay(100);}}")]
    [InlineData("class C{void A(){Task.Delay(1);}void B(){Task.Delay(1);}}")]
    [InlineData("class C{void M(){{Task.Delay(1);}{Task.Delay(1);}}}")]
    [InlineData("class C{void M(){F(Task.Delay(1),Task.Delay(1));}}")]
    [InlineData("class C{void M(){if(true){Task.Delay(1);}else{Task.Delay(1);}}}")]
    [InlineData("class C{void M(){Task.Delay(1);Task.Delay(1);Task.Delay(1);Task.Delay(1);}}")]
    public void CachedIdentitiesMatchUncachedIdentities(string code)
    {
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();

        var cache = new StructuralPathCache();
        var compared = 0;

        foreach (var node in root.DescendantNodes())
        {
            var uncached = StructuralPath.IdentityFor(node);
            var cached = StructuralPath.IdentityFor(node, cache);

            Assert.Equal(uncached, cached);
            compared++;
        }

        Assert.True(compared > 0, "Fixture produced no nodes to compare.");
    }

    [Fact]
    public void IdenticalSiblingsReceiveDistinctIdentities()
    {
        // The exact reproduction from the review: two byte-identical statements. With the ordinal
        // bug, both received ordinal #1 and produced one identity.
        const string code = "class C{void M(){Task.Delay(100);Task.Delay(100);}}";
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();

        var cache = new StructuralPathCache();
        var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();

        Assert.Equal(2, invocations.Count);

        var identities = invocations
            .Select(node => StructuralPath.IdentityFor(node, cache))
            .ToList();

        Assert.NotEqual(identities[0], identities[1]);
    }

    [Fact]
    public void RepeatedLookupsOfTheSameNodeAreStable()
    {
        // A cache must not change its answer for a node it has already seen.
        const string code = "class C{void M(){Task.Delay(1);Task.Delay(2);}}";
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();

        var cache = new StructuralPathCache();
        var node = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Last();

        var first = StructuralPath.IdentityFor(node, cache);
        var second = StructuralPath.IdentityFor(node, cache);
        var third = StructuralPath.IdentityFor(node, cache);

        Assert.Equal(first, second);
        Assert.Equal(second, third);
        Assert.Equal(StructuralPath.IdentityFor(node), first);
    }

    [Fact]
    public void SeparateCachesDoNotShareState()
    {
        // Two analyses must not influence each other. A cache that leaked across analyses would
        // produce identities that depend on analysis order.
        const string code = "class C{void M(){Task.Delay(1);Task.Delay(1);}}";
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();

        var nodes = root.DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();

        var firstRun = nodes.Select(n => StructuralPath.IdentityFor(n, new StructuralPathCache())).ToList();
        var secondRun = nodes.Select(n => StructuralPath.IdentityFor(n, new StructuralPathCache())).ToList();

        Assert.Equal(firstRun, secondRun);
    }

    [Fact]
    public void PragmaDirectivesMatchUncachedIdentities()
    {
        // Structured trivia reports a null Parent, so it takes a separate cache path. Verify that
        // path agrees with the uncached one.
        const string code = @"
class C
{
#pragma warning disable CS1591
    void A() { }
#pragma warning disable CS1591
    void B() { }
}";
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();

        var cache = new StructuralPathCache();
        var directives = root.DescendantTrivia()
            .Where(t => t.IsKind(SyntaxKind.PragmaWarningDirectiveTrivia))
            .Select(t => t.GetStructure())
            .OfType<StructuredTriviaSyntax>()
            .ToList();

        Assert.Equal(2, directives.Count);

        var cachedIdentities = new List<string>();
        foreach (var directive in directives)
        {
            Assert.Equal(
                StructuralPath.IdentityFor(directive),
                StructuralPath.IdentityFor(directive, cache));
            cachedIdentities.Add(StructuralPath.IdentityFor(directive, cache));
        }

        Assert.NotEqual(cachedIdentities[0], cachedIdentities[1]);
    }

    [Fact]
    public void PragmaDirectivesAllocateLinearly()
    {
        // Structured trivia reports a null Parent, so it takes a separate cache path. Without
        // caching that path, every directive re-enumerates and re-fingerprints the whole group:
        // measured 1.1 GB at 2,000 directives with 3.7x/3.8x growth per doubling.
        var small = MeasureRuleAllocation(BuildPragmas(500), new WarningSuppressRule());
        var large = MeasureRuleAllocation(BuildPragmas(2000), new WarningSuppressRule());

        Assert.True(
            large < small * 6.0,
            $"Allocation grew from {small:N0} bytes at 500 directives to {large:N0} bytes at 2000 "
            + $"(ratio {large / (double)small:F2}, expected < 6.0 for a 4x input increase). "
            + "Structured-trivia fingerprints are likely being recomputed per directive.");
    }

    [Fact]
    public void DifferentlyNamedMethodsAllocateLinearly()
    {
        // Each method has a distinct declaration header, so keying the cache on the header made
        // every method its own group — and each group rescanned all N siblings to build itself.
        // One delay per method therefore triggered N^2 header work: 3.47 GB at 2,000 methods.
        var small = MeasureRuleAllocation(BuildNamedMethods(500), new TimeoutJigglingRule());
        var large = MeasureRuleAllocation(BuildNamedMethods(2000), new TimeoutJigglingRule());

        Assert.True(
            large < small * 6.0,
            $"Allocation grew from {small:N0} bytes at 500 methods to {large:N0} bytes at 2000 "
            + $"(ratio {large / (double)small:F2}, expected < 6.0 for a 4x input increase). "
            + "Sibling partitions are likely being rebuilt per declaration header.");
    }

    private static string BuildNamedMethods(int count)
    {
        var methods = string.Concat(Enumerable.Range(0, count).Select(i =>
            $"[Fact]async System.Threading.Tasks.Task T{i}()" +
            "{await System.Threading.Tasks.Task.Delay(100);}"));

        return "class C{" + methods + "}";
    }

    [Fact]
    public void XmlIdentitiesMatchUncachedIdentities()
    {
        // SW005/SW006 use the XML path, which had no cache at all: every finding re-enumerated,
        // serialized, and fingerprinted its whole sibling group.
        const string xml = @"<Project>
  <PropertyGroup>
    <NoWarn>CS1591</NoWarn>
    <NoWarn>CS0618</NoWarn>
    <NoWarn>CS1591</NoWarn>
  </PropertyGroup>
</Project>";

        var doc = System.Xml.Linq.XDocument.Parse(xml);
        var cache = new StructuralPathCache();
        var compared = 0;

        foreach (var element in doc.Descendants())
        {
            Assert.Equal(
                StructuralPath.IdentityFor(element),
                StructuralPath.IdentityFor(element, cache));
            compared++;
        }

        Assert.True(compared > 0, "Fixture produced no elements to compare.");
    }

    [Fact]
    public void XmlIdenticalSiblingsReceiveDistinctIdentities()
    {
        const string xml = @"<Project>
  <PropertyGroup>
    <NoWarn>CS1591</NoWarn>
    <NoWarn>CS1591</NoWarn>
  </PropertyGroup>
</Project>";

        var doc = System.Xml.Linq.XDocument.Parse(xml);
        var cache = new StructuralPathCache();
        var elements = doc.Descendants()
            .Where(e => e.Name.LocalName == "NoWarn")
            .ToList();

        Assert.Equal(2, elements.Count);

        var identities = elements
            .Select(e => StructuralPath.IdentityFor(e, cache))
            .ToList();

        Assert.NotEqual(identities[0], identities[1]);
    }

    [Fact]
    public void XmlFindingsAllocateLinearly()
    {
        // 57 GB at 2,000 findings before the XML cache existed.
        var small = MeasureXmlAllocation(BuildXml(500));
        var large = MeasureXmlAllocation(BuildXml(2000));

        Assert.True(
            large < small * 6.0,
            $"Allocation grew from {small:N0} bytes at 500 findings to {large:N0} bytes at 2000 "
            + $"(ratio {large / (double)small:F2}, expected < 6.0 for a 4x input increase). "
            + "XML sibling groups are likely being re-fingerprinted per finding.");
    }

    private static long MeasureXmlAllocation(string xml)
    {
        RunXmlRule(xml);
        RunXmlRule(xml);

        var best = long.MaxValue;
        for (var i = 0; i < 3; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            RunXmlRule(xml);
            var after = GC.GetAllocatedBytesForCurrentThread();
            best = Math.Min(best, after - before);
        }

        return best;
    }

    private static int RunXmlRule(string xml)
    {
        var context = new DetectionContext("/tmp/slopwatch-alloc/Test.csproj", "Test.csproj", xml, null, false);
        var count = 0;
        var enumerator = new ProjectFileRule().AnalyzeAsync(context).GetAsyncEnumerator();
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

    private static string BuildXml(int count)
    {
        var elements = string.Concat(Enumerable.Repeat("<NoWarn>CS1591</NoWarn>", count));
        return "<Project><PropertyGroup>" + elements + "</PropertyGroup></Project>";
    }

    [Fact]
    public void PragmaOrdinalLookupIsNotQuadraticInCpu()
    {
        // Fingerprinting was cached but every directive still ran Array.FindIndex over the same
        // member array: 2,001,000 comparisons at 2,000 directives, 3.47 seconds at 16,000.
        // Allocation tests cannot see this because the scan allocates nothing, so measure time.
        var small = MeasureRuleTime(BuildPragmas(2000), new WarningSuppressRule());
        var large = MeasureRuleTime(BuildPragmas(8000), new WarningSuppressRule());

        // 4x input. Linear work is ~4x; quadratic is ~16x. Allow generous headroom for noise.
        Assert.True(
            large < small * 8.0,
            $"Analysis took {small.TotalMilliseconds:F0} ms at 2,000 directives and "
            + $"{large.TotalMilliseconds:F0} ms at 8,000 (ratio {large.TotalMilliseconds / small.TotalMilliseconds:F2}, "
            + "expected < 8.0 for a 4x input increase). Directive ordinals are likely being "
            + "rescanned per directive.");
    }

    private static TimeSpan MeasureRuleTime(string code, IDetectionRule rule)
    {
        var tree = CSharpSyntaxTree.ParseText(code);
        RunRule(tree, code, rule);
        RunRule(tree, code, rule);

        var best = TimeSpan.MaxValue;
        for (var i = 0; i < 3; i++)
        {
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            RunRule(tree, code, rule);
            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(start);
            if (elapsed < best)
                best = elapsed;
        }

        return best;
    }

    private static long MeasureRuleAllocation(string code, IDetectionRule rule)
    {
        var tree = CSharpSyntaxTree.ParseText(code);
        RunRule(tree, code, rule);
        RunRule(tree, code, rule);

        var best = long.MaxValue;
        for (var i = 0; i < 3; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            RunRule(tree, code, rule);
            var after = GC.GetAllocatedBytesForCurrentThread();
            best = Math.Min(best, after - before);
        }

        return best;
    }

    private static int RunRule(SyntaxTree tree, string code, IDetectionRule rule)
    {
        var context = new DetectionContext(FilePath, "TestFile.cs", code, tree, true);
        var count = 0;
        var enumerator = rule.AnalyzeAsync(context).GetAsyncEnumerator();
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

    private static string BuildPragmas(int count)
    {
        var directives = string.Concat(Enumerable.Repeat("#pragma warning disable CS1591\n", count));
        return "class C{\n" + directives + "void M(){}\n}";
    }
}
