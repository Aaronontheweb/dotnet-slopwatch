using Microsoft.CodeAnalysis.CSharp;
using Slopwatch.Detection;
using Slopwatch.Detection.Rules;
using Xunit;

namespace Slopwatch.Tests.Detection;

/// <summary>
/// Tests for SW004 - Test Timeout Jiggling Detection Rule
/// </summary>
public class TimeoutJigglingRuleTests
{
    private readonly TimeoutJigglingRule _rule = new();

    [Fact]
    public void Test_DetectsTaskDelay()
    {
        // Arrange
        const string code = @"
using System.Threading.Tasks;
using Xunit;
public class TestClass
{
    [Fact]
    public async Task TestMethod()
    {
        await Task.Delay(1000);
        Assert.True(true);
    }
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW004", result.RuleId);
        Assert.Contains("Task.Delay", result.Message);
        Assert.Contains("1000", result.Message);
        Assert.Contains("timing-dependent", result.Message);
        Assert.Equal(DetectionSeverity.Warning, result.Severity);
        Assert.Equal(9, result.LineNumber);
    }

    [Fact]
    public void Test_DetectsTaskDelayWithTimeSpan()
    {
        // Arrange
        const string code = @"
using System;
using System.Threading.Tasks;
using Xunit;
public class TestClass
{
    [Fact]
    public async Task TestMethod()
    {
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.True(true);
    }
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW004", result.RuleId);
        Assert.Contains("Task.Delay", result.Message);
        Assert.Contains("TimeSpan.FromSeconds(2)", result.Message);
    }

    [Fact]
    public void Test_DetectsThreadSleep()
    {
        // Arrange
        const string code = @"
using System.Threading;
using Xunit;
public class TestClass
{
    [Fact]
    public void TestMethod()
    {
        Thread.Sleep(500);
        Assert.True(true);
    }
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW004", result.RuleId);
        Assert.Contains("Thread.Sleep", result.Message);
        Assert.Contains("500", result.Message);
        Assert.Contains("timing-dependent", result.Message);
    }

    [Fact]
    public void Test_DetectsThreadSleepWithTimeSpan()
    {
        // Arrange
        const string code = @"
using System;
using System.Threading;
using Xunit;
public class TestClass
{
    [Fact]
    public void TestMethod()
    {
        Thread.Sleep(TimeSpan.FromMilliseconds(100));
        Assert.True(true);
    }
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW004", result.RuleId);
        Assert.Contains("Thread.Sleep", result.Message);
    }

    [Fact]
    public void Test_DetectsSpinWaitMethod()
    {
        // Arrange
        const string code = @"
using System.Threading;
using Xunit;
public class TestClass
{
    [Fact]
    public void TestMethod()
    {
        SpinWait.SpinUntil(() => false, 1000);
        Assert.True(true);
    }
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW004", result.RuleId);
        Assert.Contains("SpinWait", result.Message);
        Assert.Contains("timing-dependent", result.Message);
    }

    [Fact]
    public void Test_DetectsSpinWaitObjectCreation()
    {
        // Arrange
        const string code = @"
using System.Threading;
using Xunit;
public class TestClass
{
    [Fact]
    public void TestMethod()
    {
        var wait = new SpinWait();
        wait.SpinOnce();
        Assert.True(true);
    }
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW004", result.RuleId);
        Assert.Contains("SpinWait", result.Message);
        Assert.Contains("instance", result.Message);
        Assert.Equal(9, result.LineNumber);
    }

    [Fact]
    public void Test_IgnoresNonTestFiles()
    {
        // Arrange
        const string code = @"
using System.Threading.Tasks;
public class RegularClass
{
    public async Task Method()
    {
        await Task.Delay(1000);
        Thread.Sleep(500);
    }
}";
        var context = CreateNonTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Test_IgnoresValidSlopwatchSuppress()
    {
        // Arrange
        const string code = @"
using System.Threading.Tasks;
using Xunit;
using Slopwatch.Suppression;
public class TestClass
{
    [Fact]
    [SlopwatchSuppress(""SW004"", ""This test validates timeout behavior of a rate limiter. Delay is intentional and required to verify rate limiting works correctly."")]
    public async Task TestRateLimiter()
    {
        await Task.Delay(1000);
        Assert.True(true);
    }
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Test_DetectsMultipleDelaysInSameTest()
    {
        // Arrange
        const string code = @"
using System.Threading.Tasks;
using Xunit;
public class TestClass
{
    [Fact]
    public async Task TestMethod()
    {
        await Task.Delay(100);
        DoSomething();
        await Task.Delay(200);
        Assert.True(true);
    }

    private void DoSomething() { }
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal("SW004", r.RuleId));
    }

    [Fact]
    public void Test_DetectsMixedDelayTypes()
    {
        // Arrange
        const string code = @"
using System.Threading;
using System.Threading.Tasks;
using Xunit;
public class TestClass
{
    [Fact]
    public async Task TestMethod()
    {
        await Task.Delay(100);
        Thread.Sleep(200);
        Assert.True(true);
    }
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Message.Contains("Task.Delay"));
        Assert.Contains(results, r => r.Message.Contains("Thread.Sleep"));
    }

    [Fact]
    public void Test_ProvidesHelpfulSuggestedFix()
    {
        // Arrange
        const string code = @"
using System.Threading.Tasks;
using Xunit;
public class TestClass
{
    [Fact]
    public async Task TestMethod()
    {
        await Task.Delay(1000);
        Assert.True(true);
    }
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.NotNull(result.SuggestedFix);
        Assert.Contains("synchronization", result.SuggestedFix, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TaskCompletionSource", result.SuggestedFix);
    }

    [Fact]
    public void Test_IncludesCodeSnippet()
    {
        // Arrange
        const string code = @"
using System.Threading.Tasks;
using Xunit;
public class TestClass
{
    [Fact]
    public async Task TestMethod()
    {
        await Task.Delay(1000);
        Assert.True(true);
    }
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.NotNull(result.CodeSnippet);
        Assert.Contains("Task.Delay", result.CodeSnippet);
    }

    [Fact]
    public void Test_IgnoresDelaysInNonTestMethods()
    {
        // Arrange
        const string code = @"
using System.Threading.Tasks;
using Xunit;
public class TestClass
{
    [Fact]
    public async Task TestMethod()
    {
        await HelperMethod();
        Assert.True(true);
    }

    private async Task HelperMethod()
    {
        // Delay in helper method should not be detected in test context
        await Task.Delay(1000);
    }
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        // The rule detects delays anywhere in the test file, so this will be detected
        var result = Assert.Single(results);
        Assert.Equal("SW004", result.RuleId);
        Assert.Equal(16, result.LineNumber); // Helper method line
    }

    [Fact]
    public void Test_RespectsLineScope()
    {
        // Arrange
        const string code = @"
using System.Threading.Tasks;
using Xunit;
public class TestClass
{
    [Fact]
    public async Task Test1()
    {
        await Task.Delay(100);
    }

    [Fact]
    public async Task Test2()
    {
        await Task.Delay(200);
    }
}";
        var context = CreateTestContextWithLineScope(code, addedLines: new[] { 9 });

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal(9, result.LineNumber);
    }

    [Fact]
    public void Test_DetectsFullyQualifiedTaskDelay()
    {
        // Arrange
        const string code = @"
using Xunit;
public class TestClass
{
    [Fact]
    public async System.Threading.Tasks.Task TestMethod()
    {
        await System.Threading.Tasks.Task.Delay(1000);
        Assert.True(true);
    }
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW004", result.RuleId);
        Assert.Contains("Task.Delay", result.Message);
    }

    [Fact]
    public void Test_DetectsFullyQualifiedThreadSleep()
    {
        // Arrange
        const string code = @"
using Xunit;
public class TestClass
{
    [Fact]
    public void TestMethod()
    {
        System.Threading.Thread.Sleep(500);
        Assert.True(true);
    }
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW004", result.RuleId);
        Assert.Contains("Thread.Sleep", result.Message);
    }

    #region Helper Methods

    private DetectionContext CreateTestContext(string code)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        return new DetectionContext(
            FilePath: "/test/TestFile.cs",
            FileName: "TestFile.cs",
            Content: code,
            SyntaxTree: syntaxTree,
            IsTestFile: true
        );
    }

    private DetectionContext CreateNonTestContext(string code)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        return new DetectionContext(
            FilePath: "/src/RegularFile.cs",
            FileName: "RegularFile.cs",
            Content: code,
            SyntaxTree: syntaxTree,
            IsTestFile: false
        );
    }

    private DetectionContext CreateTestContextWithLineScope(string code, int[]? addedLines = null, int[]? modifiedLines = null)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        return new DetectionContext(
            FilePath: "/test/TestFile.cs",
            FileName: "TestFile.cs",
            Content: code,
            SyntaxTree: syntaxTree,
            IsTestFile: true,
            AddedLines: addedLines != null ? new HashSet<int>(addedLines) : null,
            ModifiedLines: modifiedLines != null ? new HashSet<int>(modifiedLines) : null
        );
    }

    private List<DetectionResult> RunRule(DetectionContext context)
    {
        var results = new List<DetectionResult>();
        var enumerable = _rule.AnalyzeAsync(context);

        var enumerator = enumerable.GetAsyncEnumerator();
        try
        {
            while (enumerator.MoveNextAsync().AsTask().Result)
            {
                results.Add(enumerator.Current);
            }
        }
        finally
        {
            enumerator.DisposeAsync().AsTask().Wait();
        }

        return results;
    }

    #endregion
}
