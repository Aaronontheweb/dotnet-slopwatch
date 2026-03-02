using Microsoft.CodeAnalysis.CSharp;
using Slopwatch.Detection;
using Slopwatch.Detection.Rules;
using Slopwatch.Suppression;
using Xunit;

namespace Slopwatch.Tests.Detection;

/// <summary>
/// Tests for SW003 - Empty Catch Block Detection Rule
/// </summary>
public class EmptyCatchBlockRuleTests
{
    private readonly EmptyCatchBlockRule _rule = new();

    [Fact]
    public void Test_DetectsEmptyCatch()
    {
        // Arrange
        const string code = @"
public class TestClass
{
    public void Method()
    {
        try
        {
            DoSomething();
        }
        catch (Exception)
        {
        }
    }

    private void DoSomething() { }
}";
        var context = CreateContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW003", result.RuleId);
        Assert.Contains("Empty catch block", result.Message);
        Assert.Equal(DetectionSeverity.Error, result.Severity);
        Assert.Equal(10, result.LineNumber);
    }

    [Fact]
    public void Test_DetectsCommentOnlyCatch()
    {
        // Arrange
        const string code = @"
public class TestClass
{
    public void Method()
    {
        try
        {
            DoSomething();
        }
        catch (Exception)
        {
            // TODO: Handle this exception properly
        }
    }

    private void DoSomething() { }
}";
        var context = CreateContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW003", result.RuleId);
        Assert.Contains("Empty catch block", result.Message);
        Assert.Equal(DetectionSeverity.Error, result.Severity);
    }

    [Fact]
    public void Test_IgnoresLoggingOnlyCatch()
    {
        // Arrange - catch blocks with logging are NOT flagged
        // Logging IS handling for fire-and-forget operations, background jobs, etc.
        const string code = @"
public class TestClass
{
    public void Method()
    {
        try
        {
            DoSomething();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }

    private void DoSomething() { }
}";
        var context = CreateContext(code);

        // Act
        var results = RunRule(context);

        // Assert - logging is considered valid handling, not flagged
        Assert.Empty(results);
    }

    [Fact]
    public void Test_IgnoresConsoleWriteOnlyCatch()
    {
        // Arrange - catch blocks with console output are NOT flagged
        // Logging to console IS handling for debugging and simple scenarios
        const string code = @"
public class TestClass
{
    public void Method()
    {
        try
        {
            DoSomething();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }

    private void DoSomething() { }
}";
        var context = CreateContext(code);

        // Act
        var results = RunRule(context);

        // Assert - console output is considered valid handling, not flagged
        Assert.Empty(results);
    }

    [Fact]
    public void Test_IgnoresBroadCatchWithHandling()
    {
        // Arrange - catch(Exception) with actual handling is legitimate
        const string code = @"
public class TestClass
{
    public void Method()
    {
        try
        {
            DoSomething();
        }
        catch (Exception ex)
        {
            HandleError(ex);
        }
    }

    private void DoSomething() { }
    private void HandleError(Exception ex) { }
}";
        var context = CreateContext(code);

        // Act
        var results = RunRule(context);

        // Assert - Catching Exception with actual handling code is NOT flagged
        // This is a legitimate pattern for top-level handlers, plugin systems, etc.
        Assert.Empty(results);
    }

    [Fact]
    public void Test_IgnoresCatchWithoutTypeButWithHandling()
    {
        // Arrange - catch without type but with actual handling is legitimate
        const string code = @"
public class TestClass
{
    public void Method()
    {
        try
        {
            DoSomething();
        }
        catch
        {
            HandleError();
        }
    }

    private void DoSomething() { }
    private void HandleError() { }
}";
        var context = CreateContext(code);

        // Act
        var results = RunRule(context);

        // Assert - bare catch with actual handling code is NOT flagged
        Assert.Empty(results);
    }

    [Fact]
    public void Test_IgnoresProperExceptionHandling()
    {
        // Arrange
        const string code = @"
public class TestClass
{
    public void Method()
    {
        try
        {
            DoSomething();
        }
        catch (InvalidOperationException ex)
        {
            Log.Error(""Operation failed"", ex);
            CleanupResources();
            throw;
        }
    }

    private void DoSomething() { }
    private void CleanupResources() { }
}";
        var context = CreateContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Test_IgnoresSpecificExceptionTypes()
    {
        // Arrange
        const string code = @"
public class TestClass
{
    public void Method()
    {
        try
        {
            DoSomething();
        }
        catch (InvalidOperationException ex)
        {
            HandleSpecificError(ex);
        }
        catch (ArgumentException ex)
        {
            HandleSpecificError(ex);
        }
    }

    private void DoSomething() { }
    private void HandleSpecificError(Exception ex) { }
}";
        var context = CreateContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Test_IgnoresLogWithRethrow()
    {
        // Arrange
        const string code = @"
public class TestClass
{
    public void Method()
    {
        try
        {
            DoSomething();
        }
        catch (IOException ex)
        {
            Logger.Error(""Failed to do something"", ex);
            throw;
        }
    }

    private void DoSomething() { }
}";
        var context = CreateContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        // Should not detect because we're using a specific exception type and rethrowing
        Assert.Empty(results);
    }

    [Fact]
    public void Test_IgnoresValidSlopwatchSuppress()
    {
        // Arrange
        const string code = @"
using Slopwatch.Suppression;
public class TestClass
{
    [SlopwatchSuppress(""SW003"", ""This method attempts to load optional configuration file. Empty catch is acceptable because missing config is a valid scenario."")]
    public void LoadOptionalConfig()
    {
        try
        {
            LoadConfigFile();
        }
        catch (FileNotFoundException)
        {
            // Optional file, ignore if not present
        }
    }

    private void LoadConfigFile() { }
}";
        var context = CreateContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Test_DetectsMultipleEmptyCatchBlocks()
    {
        // Arrange
        const string code = @"
public class TestClass
{
    public void Method1()
    {
        try { DoSomething(); }
        catch (Exception) { }
    }

    public void Method2()
    {
        try { DoSomething(); }
        catch (Exception) { }
    }

    private void DoSomething() { }
}";
        var context = CreateContext(code);

        // Act
        var results = RunRule(context);

        // Assert - only empty catches are flagged, logging is allowed
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal("SW003", r.RuleId));
    }

    [Fact]
    public void Test_OnlyFlagsEmptyCatchNotLogging()
    {
        // Arrange - one empty catch and one logging catch
        const string code = @"
public class TestClass
{
    public void Method1()
    {
        try { DoSomething(); }
        catch (Exception) { }
    }

    public void Method2()
    {
        try { DoSomething(); }
        catch (Exception ex) { Log.Error(ex); }
    }

    private void DoSomething() { }
}";
        var context = CreateContext(code);

        // Act
        var results = RunRule(context);

        // Assert - only the empty catch is flagged, logging is allowed
        var result = Assert.Single(results);
        Assert.Equal("SW003", result.RuleId);
        Assert.Equal(7, result.LineNumber);
    }

    [Fact]
    public void Test_ProvidesHelpfulSuggestedFix()
    {
        // Arrange
        const string code = @"
public class TestClass
{
    public void Method()
    {
        try { DoSomething(); }
        catch (Exception) { }
    }
    private void DoSomething() { }
}";
        var context = CreateContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.NotNull(result.SuggestedFix);
        Assert.Contains("exception handling", result.SuggestedFix);
    }

    [Fact]
    public void Test_IncludesCodeSnippet()
    {
        // Arrange
        const string code = @"
public class TestClass
{
    public void Method()
    {
        try { DoSomething(); }
        catch (Exception) { }
    }
    private void DoSomething() { }
}";
        var context = CreateContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.NotNull(result.CodeSnippet);
    }

    [Fact]
    public void Test_IgnoresSystemExceptionWithHandling()
    {
        // Arrange - System.Exception with actual handling is legitimate
        const string code = @"
public class TestClass
{
    public void Method()
    {
        try
        {
            DoSomething();
        }
        catch (System.Exception ex)
        {
            HandleError(ex);
        }
    }

    private void DoSomething() { }
    private void HandleError(Exception ex) { }
}";
        var context = CreateContext(code);

        // Act
        var results = RunRule(context);

        // Assert - System.Exception with actual handling code is NOT flagged
        Assert.Empty(results);
    }

    [Fact]
    public void Test_IgnoresCatchWithOtherStatements()
    {
        // Arrange - catch with logging AND other statements is proper handling
        const string code = @"
public class TestClass
{
    public void Method()
    {
        try
        {
            DoSomething();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            CleanupResources();
            return;
        }
    }

    private void DoSomething() { }
    private void CleanupResources() { }
}";
        var context = CreateContext(code);

        // Act
        var results = RunRule(context);

        // Assert - catch with logging + other handling is NOT flagged
        // The rule only flags empty catches and logging-only catches (without rethrow)
        Assert.Empty(results);
    }

    [Fact]
    public void Test_RespectsLineScope()
    {
        // Arrange
        const string code = @"
public class TestClass
{
    public void Method1()
    {
        try { DoSomething(); }
        catch (Exception) { }
    }

    public void Method2()
    {
        try { DoSomething(); }
        catch (Exception) { }
    }

    private void DoSomething() { }
}";
        var context = CreateContextWithLineScope(code, addedLines: new[] { 7 });

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal(7, result.LineNumber);
    }

    [Fact]
    public void Test_ConfigSuppression_FromCustomConfigPath_SuppressesDetection()
    {
        // Arrange
        const string code = @"
public class TestClass
{
    public void Method()
    {
        try
        {
            DoSomething();
        }
        catch (Exception)
        {
        }
    }

    private void DoSomething() { }
}";

        var configPath = Path.Combine(Path.GetTempPath(), $"slopwatch-config-{Guid.NewGuid():N}.json");
        const string configJson = @"{
  ""suppressions"": [],
  ""globalSuppressions"": [
    {
      ""ruleId"": ""SW003"",
      ""justification"": ""Test-only global suppression to validate custom config loading path""
    }
  ]
}";

        File.WriteAllText(configPath, configJson);
        SuppressionChecker.SetConfigPathOverride(configPath);

        try
        {
            var context = CreateContext(code);

            // Act
            var results = RunRule(context);

            // Assert
            Assert.Empty(results);
        }
        finally
        {
            SuppressionChecker.SetConfigPathOverride(null);
            if (File.Exists(configPath))
            {
                File.Delete(configPath);
            }
        }
    }

    #region Helper Methods

    private DetectionContext CreateContext(string code)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        return new DetectionContext(
            FilePath: "/src/TestFile.cs",
            FileName: "TestFile.cs",
            Content: code,
            SyntaxTree: syntaxTree,
            IsTestFile: false
        );
    }

    private DetectionContext CreateContextWithLineScope(string code, int[]? addedLines = null, int[]? modifiedLines = null)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        return new DetectionContext(
            FilePath: "/src/TestFile.cs",
            FileName: "TestFile.cs",
            Content: code,
            SyntaxTree: syntaxTree,
            IsTestFile: false,
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
