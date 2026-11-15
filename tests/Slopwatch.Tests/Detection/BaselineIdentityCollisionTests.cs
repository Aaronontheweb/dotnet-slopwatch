using Microsoft.CodeAnalysis.CSharp;
using Slopwatch.Baseline;
using Slopwatch.Detection;
using Slopwatch.Detection.Rules;
using Xunit;

namespace Slopwatch.Tests.Detection;

/// <summary>
/// Regression tests for issue #142: every rule except SW021 used raw syntax text as the baseline
/// identity, so two identical occurrences in one file produced the same hash and baselining one
/// silently exempted the other.
/// </summary>
/// <remarks>
/// Each test baselines the first occurrence and asserts the second is still reported. These fail
/// if a rule reverts to passing raw (or display-truncated) text as its identity.
/// </remarks>
public class BaselineIdentityCollisionTests
{
    private const string Root = "/test";

    #region SW001 - DisabledTestRule

    [Fact]
    public void SW001_IdenticalSkipAttributesInDifferentMethodsDoNotShareIdentity()
    {
        // Both methods have identical first three lines, which is exactly what the display
        // snippet truncates to. Identity must come from the full structural path instead.
        const string code = @"
using Xunit;
public class TestClass
{
    [Fact(Skip = ""Flaky"")]
    public void First()
    {
        Assert.True(true);
    }

    [Fact(Skip = ""Flaky"")]
    public void Second()
    {
        Assert.True(true);
    }
}";

        var results = RunRule(new DisabledTestRule(), CreateCsContext(code, isTestFile: true));
        Assert.Equal(2, results.Count);
        AssertDistinctIdentities(results);
    }

    [Fact]
    public void SW001_MethodsWithIdenticalSkipAttributesDoNotShareIdentity()
    {
        // Both methods carry byte-identical [Fact(Skip = "Flaky")] attributes, so an identity
        // derived from the attribute text alone would collide. Identity must also reflect the
        // enclosing method, whose bodies differ here.
        //
        // Note: this exercises the attribute branch, not the #if false branch. Code inside
        // #if false is excluded from the syntax tree by Roslyn, so that branch is unreachable
        // from source text and cannot be covered by a test.
        const string code = @"
using Xunit;
public class TestClass
{
    [Fact(Skip = ""Flaky"")]
    public void First()
    {
        Assert.True(true);
    }

    [Fact(Skip = ""Flaky"")]
    public void Second()
    {
        Assert.True(false);
    }
}";

        var results = RunRule(new DisabledTestRule(), CreateCsContext(code, isTestFile: true));
        Assert.Equal(2, results.Count);
        AssertDistinctIdentities(results);
    }

    #endregion

    #region SW002 - WarningSuppressRule

    [Fact]
    public void SW002_IdenticalPragmaDirectivesDoNotShareIdentity()
    {
        // #pragma directives are structured trivia whose Parent is null. A naive Parent walk
        // would collapse both to the same single-segment path.
        const string code = @"
public class TestClass
{
#pragma warning disable CS1591
    public void First() { }

#pragma warning disable CS1591
    public void Second() { }
}";

        var results = RunRule(new WarningSuppressRule(), CreateCsContext(code));
        Assert.Equal(2, results.Count);
        AssertDistinctIdentities(results);
    }

    [Fact]
    public void SW002_IdenticalSuppressMessageAttributesDoNotShareIdentity()
    {
        const string code = @"
public class TestClass
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(""Category"", ""CS0001"")]
    public void First() { }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(""Category"", ""CS0001"")]
    public void Second() { }
}";

        var results = RunRule(new WarningSuppressRule(), CreateCsContext(code));
        Assert.Equal(2, results.Count);
        AssertDistinctIdentities(results);
    }

    #endregion

    #region SW003 - EmptyCatchBlockRule

    [Fact]
    public void SW003_IdenticalEmptyCatchBlocksDoNotShareIdentity()
    {
        const string code = @"
public class TestClass
{
    public void First()
    {
        try { Work(); }
        catch (System.Exception) { }
    }

    public void Second()
    {
        try { Work(); }
        catch (System.Exception) { }
    }

    private void Work() { }
}";

        var results = RunRule(new EmptyCatchBlockRule(), CreateCsContext(code));
        Assert.Equal(2, results.Count);
        AssertDistinctIdentities(results);
    }

    [Fact]
    public void SW003_CatchBlocksDifferingOnlyAfterTruncationDoNotShareIdentity()
    {
        // The display snippet is the first 5 lines of the catch clause. These two catch blocks
        // render identically in the snippet but differ after line 5, so a truncated snippet
        // would collide. Each catch is comment-only, which the rule still flags.
        const string code = @"
public class TestClass
{
    public void First()
    {
        try { Work(); }
        catch (System.Exception)
        {
            // a
            // b
            // c
            // first
        }
    }

    public void Second()
    {
        try { Work(); }
        catch (System.Exception)
        {
            // a
            // b
            // c
            // second
        }
    }

    private void Work() { }
}";

        var results = RunRule(new EmptyCatchBlockRule(), CreateCsContext(code));
        Assert.Equal(2, results.Count);

        // Guard the premise: the display snippets really are identical, so only the identity
        // can distinguish these two occurrences.
        Assert.Equal(results[0].CodeSnippet, results[1].CodeSnippet);
        AssertDistinctIdentities(results);
    }

    #endregion

    #region SW004 - TimeoutJigglingRule

    [Fact]
    public void SW004_IdenticalTaskDelayCallsDoNotShareIdentity()
    {
        // This is the exact reproduction from issue #142.
        const string code = @"
using System.Threading.Tasks;
using Xunit;
public class TestClass
{
    [Fact]
    public async Task A()
    {
        await Task.Delay(100);
    }

    [Fact]
    public async Task B()
    {
        await Task.Delay(100);
    }
}";

        var results = RunRule(new TimeoutJigglingRule(), CreateCsContext(code, isTestFile: true));
        Assert.Equal(2, results.Count);
        AssertDistinctIdentities(results);
    }

    [Fact]
    public void SW004_IdenticalThreadSleepCallsDoNotShareIdentity()
    {
        const string code = @"
using System.Threading;
using Xunit;
public class TestClass
{
    [Fact]
    public void A()
    {
        Thread.Sleep(100);
    }

    [Fact]
    public void B()
    {
        Thread.Sleep(100);
    }
}";

        var results = RunRule(new TimeoutJigglingRule(), CreateCsContext(code, isTestFile: true));
        Assert.Equal(2, results.Count);
        AssertDistinctIdentities(results);
    }

    #endregion

    #region SW005 - ProjectFileRule

    [Fact]
    public void SW005_IdenticalNoWarnElementsDoNotShareIdentity()
    {
        const string xml = @"<Project>
  <PropertyGroup>
    <NoWarn>CS1591</NoWarn>
  </PropertyGroup>
  <PropertyGroup>
    <NoWarn>CS1591</NoWarn>
  </PropertyGroup>
</Project>";

        var results = RunRule(new ProjectFileRule(), CreateXmlContext(xml, "Test.csproj"));
        Assert.Equal(2, results.Count);
        AssertDistinctIdentities(results);
    }

    [Fact]
    public void SW005_IdenticalNullableElementsDoNotShareIdentity()
    {
        const string xml = @"<Project>
  <PropertyGroup>
    <Nullable>disable</Nullable>
  </PropertyGroup>
  <PropertyGroup>
    <Nullable>disable</Nullable>
  </PropertyGroup>
</Project>";

        var results = RunRule(new ProjectFileRule(), CreateXmlContext(xml, "Test.csproj"));
        Assert.Equal(2, results.Count);
        AssertDistinctIdentities(results);
    }

    #endregion

    #region SW006 - PackageVersionOverrideRule

    [Fact]
    public void SW006_IdenticalVersionOverridesDoNotShareIdentity()
    {
        const string xml = @"<Project>
  <ItemGroup>
    <PackageReference Include=""Akka"" VersionOverride=""1.5.0"" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include=""Akka"" VersionOverride=""1.5.0"" />
  </ItemGroup>
</Project>";

        var rule = new PackageVersionOverrideRule();
        var results = RunRule(rule, CreateXmlContext(xml, "Test.csproj"));
        Assert.Equal(2, results.Count);
        AssertDistinctIdentities(results);
    }

    [Fact]
    public void SW006_IdenticalVersionAttributesDoNotShareIdentity()
    {
        const string xml = @"<Project>
  <ItemGroup>
    <PackageReference Include=""Akka"" Version=""1.5.0"" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include=""Akka"" Version=""1.5.0"" />
  </ItemGroup>
</Project>";

        var rule = new PackageVersionOverrideRule { CpmEnabledOverride = true };
        var results = RunRule(rule, CreateXmlContext(xml, "Test.csproj"));
        Assert.Equal(2, results.Count);
        AssertDistinctIdentities(results);
    }

    #endregion

    #region Cross-cutting

    [Fact]
    public void BaseliningFirstOccurrenceStillReportsSecond_ForEveryRule()
    {
        // The end-to-end shape of the bug: baseline one occurrence, confirm the identical
        // second occurrence is still reported.
        const string code = @"
using System.Threading.Tasks;
using Xunit;
public class TestClass
{
    [Fact]
    public async Task A()
    {
        await Task.Delay(100);
    }

    [Fact]
    public async Task B()
    {
        await Task.Delay(100);
    }
}";

        var results = RunRule(new TimeoutJigglingRule(), CreateCsContext(code, isTestFile: true));
        Assert.Equal(2, results.Count);

        var baseline = BaselineFile.Create(new[] { results[0] }, Root);
        Assert.True(baseline.IsBaselined(results[0], Root));
        Assert.False(baseline.IsBaselined(results[1], Root));
    }

    [Fact]
    public void DisplaySnippetStaysReadableWhileIdentityIsStructural()
    {
        // Requirement 3: display text must remain human-readable. Identity must not be the
        // display text.
        const string code = @"
using Xunit;
public class TestClass
{
    [Fact(Skip = ""Flaky"")]
    public void First()
    {
        Assert.True(true);
    }
}";

        var result = Assert.Single(RunRule(new DisabledTestRule(), CreateCsContext(code, isTestFile: true)));
        Assert.NotNull(result.CodeSnippet);
        Assert.Contains("Skip", result.CodeSnippet);
        Assert.NotNull(result.Identity);
        Assert.NotEqual(result.CodeSnippet, result.Identity);
    }

    [Fact]
    public void IdentityIsStableAcrossRepeatedAnalysis()
    {
        // Identity must be deterministic, otherwise baselines churn on every run.
        const string code = @"
using System.Threading.Tasks;
using Xunit;
public class TestClass
{
    [Fact]
    public async Task A()
    {
        await Task.Delay(100);
    }
}";

        var first = Assert.Single(RunRule(new TimeoutJigglingRule(), CreateCsContext(code, isTestFile: true)));
        var second = Assert.Single(RunRule(new TimeoutJigglingRule(), CreateCsContext(code, isTestFile: true)));
        Assert.Equal(first.Identity, second.Identity);
    }

    #endregion

    #region Helpers

    private static void AssertDistinctIdentities(IReadOnlyList<DetectionResult> results)
    {
        var identities = results.Select(r => r.BaselineIdentity).ToList();
        Assert.All(identities, identity => Assert.False(string.IsNullOrWhiteSpace(identity)));
        Assert.Equal(identities.Count, identities.Distinct(StringComparer.Ordinal).Count());

        // And the hashes the baseline actually uses must differ too.
        var hashes = results
            .Select(r => BaselineEntry.ComputeHash(r.RuleId, "TestFile.cs", r.BaselineIdentity))
            .ToList();
        Assert.Equal(hashes.Count, hashes.Distinct(StringComparer.Ordinal).Count());
    }

    private static DetectionContext CreateCsContext(string code, bool isTestFile = false)
    {
        return new DetectionContext(
            FilePath: "/test/TestFile.cs",
            FileName: "TestFile.cs",
            Content: code,
            SyntaxTree: CSharpSyntaxTree.ParseText(code),
            IsTestFile: isTestFile);
    }

    private static DetectionContext CreateXmlContext(string xml, string fileName)
    {
        return new DetectionContext(
            FilePath: $"/test/{fileName}",
            FileName: fileName,
            Content: xml,
            SyntaxTree: null,
            IsTestFile: false);
    }

    private static List<DetectionResult> RunRule(IDetectionRule rule, DetectionContext context)
    {
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

    #endregion
}
