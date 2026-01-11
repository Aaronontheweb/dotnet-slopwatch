using Microsoft.CodeAnalysis.CSharp;
using Slopwatch.Detection;
using Slopwatch.Detection.Rules;
using Xunit;

namespace Slopwatch.Tests.Detection;

/// <summary>
/// Tests for SW002 - Warning Suppression Detection Rule
/// </summary>
public class WarningSuppressRuleTests
{
    private readonly WarningSuppressRule _rule = new();

    [Fact]
    public void Test_DetectsPragmaWithoutRestore()
    {
        // Arrange
        const string code = @"
#pragma warning disable CS8600
public class TestClass
{
    public void Method()
    {
        string? nullable = null;
        string value = nullable; // Nullability warning suppressed
    }
}";
        var context = CreateContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW002", result.RuleId);
        Assert.Contains("CS8600", result.Message);
        Assert.Contains("without matching restore", result.Message);
        Assert.Equal(2, result.LineNumber);
    }

    [Fact]
    public void Test_DetectsMultiplePragmaWithoutRestore()
    {
        // Arrange
        const string code = @"
#pragma warning disable CS8600
#pragma warning disable CS0168
public class TestClass
{
    public void Method()
    {
        string? nullable = null;
        int unused = 0;
    }
}";
        var context = CreateContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal("SW002", r.RuleId));
        Assert.All(results, r => Assert.Contains("without matching restore", r.Message));
    }

    [Fact]
    public void Test_IgnoresMatchedPragmaPairs()
    {
        // Arrange
        const string code = @"
public class TestClass
{
    public void Method()
    {
#pragma warning disable CS8600
        string? nullable = null;
        string value = nullable;
#pragma warning restore CS8600
    }
}";
        var context = CreateContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Test_DetectsPragmaDisableAll()
    {
        // Arrange
        const string code = @"
#pragma warning disable
public class TestClass
{
    public void Method() { }
}";
        var context = CreateContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW002", result.RuleId);
        Assert.Contains("all warnings", result.Message);
    }

    [Fact]
    public void Test_DetectsSuppressMessageAttribute()
    {
        // Arrange
        const string code = @"
using System.Diagnostics.CodeAnalysis;
public class TestClass
{
    [SuppressMessage(""Microsoft.Design"", ""CA1031:DoNotCatchGeneralExceptionTypes"")]
    public void Method()
    {
        try { }
        catch (Exception) { }
    }
}";
        var context = CreateContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW002", result.RuleId);
        Assert.Contains("SuppressMessage", result.Message);
        Assert.Contains("Microsoft.Design:CA1031", result.Message);
        Assert.Equal(5, result.LineNumber);
    }

    [Fact]
    public void Test_DetectsSuppressMessageOnClass()
    {
        // Arrange
        const string code = @"
using System.Diagnostics.CodeAnalysis;
[SuppressMessage(""StyleCop.CSharp.DocumentationRules"", ""SA1600:ElementsMustBeDocumented"")]
public class TestClass
{
    public void Method() { }
}";
        var context = CreateContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW002", result.RuleId);
        Assert.Contains("SA1600", result.Message);
    }

    [Fact]
    public void Test_DetectsSuppressMessageOnProperty()
    {
        // Arrange
        const string code = @"
using System.Diagnostics.CodeAnalysis;
public class TestClass
{
    [SuppressMessage(""Performance"", ""CA1819:PropertiesShouldNotReturnArrays"")]
    public int[] Values { get; set; }
}";
        var context = CreateContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW002", result.RuleId);
        Assert.Contains("CA1819", result.Message);
    }

    [Fact]
    public void Test_IgnoresValidSlopwatchSuppress()
    {
        // Arrange
        const string code = @"
using System.Diagnostics.CodeAnalysis;
using Slopwatch.Suppression;
public class TestClass
{
    [SuppressMessage(""Microsoft.Design"", ""CA1031:DoNotCatchGeneralExceptionTypes"")]
    [SlopwatchSuppress(""SW002"", ""This catch block handles all exceptions from third-party library that throws various exception types unpredictably."")]
    public void Method()
    {
        try { }
        catch (Exception) { }
    }
}";
        var context = CreateContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Test_IgnoresValidSlopwatchSuppressOnClass()
    {
        // Arrange
        const string code = @"
using Slopwatch.Suppression;
[SlopwatchSuppress(""SW002"", ""This class contains generated code that cannot be modified and has unavoidable warning suppressions."")]
#pragma warning disable CS8600
public class GeneratedClass
{
    public void Method() { }
}
#pragma warning restore CS8600
";
        var context = CreateContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        // The pragma pair is matched, so no results expected
        Assert.Empty(results);
    }

    [Fact]
    public void Test_ReturnsCorrectSeverity()
    {
        // Arrange
        const string code = @"
#pragma warning disable CS8600
public class TestClass { }";
        var context = CreateContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal(DetectionSeverity.Warning, result.Severity);
    }

    [Fact]
    public void Test_DetectsNestedPragmaScopes()
    {
        // Arrange
        const string code = @"
#pragma warning disable CS8600
public class TestClass
{
    public void Method1()
    {
#pragma warning disable CS0168
        int unused = 0;
#pragma warning restore CS0168
    }
}
// Missing restore for CS8600
";
        var context = CreateContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW002", result.RuleId);
        Assert.Contains("CS8600", result.Message);
    }

    [Fact]
    public void Test_IgnoresCompletelyMatchedNestedPragmas()
    {
        // Arrange
        const string code = @"
#pragma warning disable CS8600
public class TestClass
{
    public void Method1()
    {
#pragma warning disable CS0168
        int unused = 0;
#pragma warning restore CS0168
    }
}
#pragma warning restore CS8600
";
        var context = CreateContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Test_ProvidesHelpfulSuggestedFix()
    {
        // Arrange
        const string code = @"
#pragma warning disable CS8600
public class TestClass { }";
        var context = CreateContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.NotNull(result.SuggestedFix);
        Assert.Contains("restore", result.SuggestedFix, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("suppression", result.SuggestedFix);
    }

    [Fact]
    public void Test_IncludesCodeSnippet()
    {
        // Arrange
        const string code = @"
#pragma warning disable CS8600
public class TestClass { }";
        var context = CreateContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.NotNull(result.CodeSnippet);
        Assert.Contains("#pragma warning disable", result.CodeSnippet);
    }

    [Fact]
    public void Test_DetectsMultipleSuppressMessageAttributes()
    {
        // Arrange
        const string code = @"
using System.Diagnostics.CodeAnalysis;
[SuppressMessage(""Category1"", ""Rule1"")]
[SuppressMessage(""Category2"", ""Rule2"")]
public class TestClass
{
    [SuppressMessage(""Category3"", ""Rule3"")]
    public void Method() { }
}";
        var context = CreateContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Equal("SW002", r.RuleId));
    }

    [Fact]
    public void Test_DetectsFieldWithSuppressMessage()
    {
        // Arrange
        const string code = @"
using System.Diagnostics.CodeAnalysis;
public class TestClass
{
    [SuppressMessage(""Design"", ""CA1051:DoNotDeclareVisibleInstanceFields"")]
    public int Field;
}";
        var context = CreateContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW002", result.RuleId);
    }

    [Fact]
    public void Test_RespectsLineScope()
    {
        // Arrange
        const string code = @"
#pragma warning disable CS8600
#pragma warning disable CS0168
public class TestClass { }";
        var context = CreateContextWithLineScope(code, addedLines: new[] { 2 });

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal(2, result.LineNumber); // Only line 2 is in scope
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
