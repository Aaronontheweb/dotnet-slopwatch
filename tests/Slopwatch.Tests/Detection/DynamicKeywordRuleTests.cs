using Microsoft.CodeAnalysis.CSharp;
using Slopwatch.Detection;
using Slopwatch.Detection.Rules;
using Xunit;

namespace Slopwatch.Tests.Detection;

/// <summary>
/// Tests for SW021 - Dynamic Keyword Ban
/// </summary>
public class DynamicKeywordRuleTests
{
    private readonly DynamicKeywordRule _rule = new();

    [Fact]
    public void Test_DetectsDynamicParameter()
    {
        // Arrange
        const string code = @"
public class TestClass
{
    public void Process(dynamic input)
    {
    }
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW021", result.RuleId);
        Assert.Contains("dynamic", result.Message);
        Assert.Equal(DetectionSeverity.Error, result.Severity);
        Assert.Equal(4, result.LineNumber);
    }

    [Fact]
    public void Test_DetectsDynamicReturnType()
    {
        // Arrange
        const string code = @"
public class TestClass
{
    public dynamic GetValue()
    {
        return 42;
    }
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW021", result.RuleId);
        Assert.Equal(4, result.LineNumber);
    }

    [Fact]
    public void Test_DetectsDynamicLocalVariable()
    {
        // Arrange
        const string code = @"
public class TestClass
{
    public void Run()
    {
        dynamic value = 42;
    }
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW021", result.RuleId);
        Assert.Equal(6, result.LineNumber);
    }

    [Fact]
    public void Test_DetectsDynamicField()
    {
        // Arrange
        const string code = @"
public class TestClass
{
    private dynamic _value;
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW021", result.RuleId);
        Assert.Equal(4, result.LineNumber);
    }

    [Fact]
    public void Test_DetectsDynamicProperty()
    {
        // Arrange
        const string code = @"
public class TestClass
{
    public dynamic Value { get; set; }
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW021", result.RuleId);
        Assert.Equal(4, result.LineNumber);
    }

    [Fact]
    public void Test_DetectsDynamicGenericTypeArgument()
    {
        // Arrange
        const string code = @"
using System.Collections.Generic;
public class TestClass
{
    public List<dynamic> Values { get; set; } = new();
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW021", result.RuleId);
        Assert.Equal(5, result.LineNumber);
    }

    [Fact]
    public void Test_DetectsDynamicCast()
    {
        // Arrange
        const string code = @"
public class TestClass
{
    public void Run(object input)
    {
        var value = (dynamic)input;
    }
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW021", result.RuleId);
        Assert.Equal(6, result.LineNumber);
    }

    [Fact]
    public void Test_DetectsAsDynamic()
    {
        // Arrange
        const string code = @"
public class TestClass
{
    public void Run(object input)
    {
        var value = input as dynamic;
    }
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW021", result.RuleId);
        Assert.Equal(6, result.LineNumber);
    }

    [Fact]
    public void Test_DetectsDynamicArrayElementType()
    {
        // Arrange
        const string code = @"
public class TestClass
{
    public void Run()
    {
        dynamic[] values = new dynamic[1];
    }
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal("SW021", r.RuleId));
    }

    [Fact]
    public void Test_DoesNotFlagObjectOrVar()
    {
        // Arrange
        const string code = @"
using System.Collections.Generic;
public class TestClass
{
    public object GetValue(object input)
    {
        var value = input;
        List<object> items = new();
        return value;
    }
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Test_DoesNotFlagIdentifierNamedDynamic()
    {
        // Arrange - 'dynamic' used as a variable name, not a type
        const string code = @"
public class TestClass
{
    public void Run()
    {
        var dynamic = 42;
        var text = dynamic.ToString();
    }
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Test_RespectsLineScope()
    {
        // Arrange
        const string code = @"
public class TestClass
{
    public void First(dynamic a)
    {
    }

    public void Second(dynamic b)
    {
    }
}";
        var context = CreateTestContextWithLineScope(code, addedLines: new[] { 4 });

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal(4, result.LineNumber);
    }

    [Fact]
    public void Test_RespectsSuppressionAttribute()
    {
        // Arrange
        const string code = @"
public class TestClass
{
    [SlopwatchSuppress(""SW021"", ""COM interop requires late-bound dispatch here"")]
    public void Run(dynamic comObject)
    {
    }
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Test_SuppressionRequiresJustification()
    {
        // Arrange - justification too short, so the suppression is invalid
        const string code = @"
public class TestClass
{
    [SlopwatchSuppress(""SW021"", ""too short"")]
    public void Run(dynamic comObject)
    {
    }
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW021", result.RuleId);
    }

    [Fact]
    public void Test_DetectsDynamicInTestFile()
    {
        // Arrange - the ban applies to test code too
        const string code = @"
using Xunit;
public class TestClass
{
    [Fact]
    public void TestMethod()
    {
        dynamic value = 42;
        Assert.NotNull(value);
    }
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW021", result.RuleId);
    }

    [Fact]
    public void Test_DetectsDynamicInGenericConstraint()
    {
        // Arrange
        const string code = @"
public class TestClass<T> where T : dynamic
{
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW021", result.RuleId);
        Assert.Equal(2, result.LineNumber);
    }

    [Fact]
    public void Test_DetectsDynamicInIsPattern()
    {
        // Arrange
        const string code = @"
public class TestClass
{
    public void Run(object input)
    {
        if (input is dynamic d)
        {
        }
    }
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW021", result.RuleId);
        Assert.Equal(6, result.LineNumber);
    }

    [Fact]
    public void Test_DetectsDynamicInIsTypeCheck()
    {
        // Arrange
        const string code = @"
public class TestClass
{
    public bool Run(object input)
    {
        return input is dynamic;
    }
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW021", result.RuleId);
        Assert.Equal(6, result.LineNumber);
    }

    [Fact]
    public void Test_DetectsDynamicInSwitchPattern()
    {
        // Arrange
        const string code = @"
public class TestClass
{
    public int Run(object input)
    {
        return input switch
        {
            dynamic d => 1,
            _ => 2
        };
    }
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW021", result.RuleId);
        Assert.Equal(8, result.LineNumber);
    }

    [Fact]
    public void Test_DetectsDynamicUsingAlias()
    {
        // Arrange
        const string code = @"
using D = dynamic;
public class TestClass
{
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW021", result.RuleId);
        Assert.Equal(2, result.LineNumber);
    }

    [Fact]
    public void Test_DetectsDynamicIndexerReturnType()
    {
        // Arrange
        const string code = @"
public class TestClass
{
    public dynamic this[int index] => null;
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW021", result.RuleId);
        Assert.Equal(4, result.LineNumber);
    }

    [Fact]
    public void Test_DetectsDynamicInFunctionPointer()
    {
        // Arrange
        const string code = @"
public unsafe class TestClass
{
    public delegate*<dynamic, void> Pointer;
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW021", result.RuleId);
        Assert.Equal(4, result.LineNumber);
    }

    [Fact]
    public void Test_DetectsDynamicEventType()
    {
        // Arrange
        const string code = @"
public class TestClass
{
    public event dynamic? Changed;
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW021", result.RuleId);
        Assert.Equal(4, result.LineNumber);
    }

    [Fact]
    public void Test_DetectsDynamicInTopLevelStatement()
    {
        // Arrange - top-level statements have no enclosing declaration
        const string code = @"
dynamic value = 42;
System.Console.WriteLine(value);
";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW021", result.RuleId);
        Assert.Equal(2, result.LineNumber);
    }

    [Fact]
    public void Test_TopLevelSnippetsAreDistinct()
    {
        // Arrange - two top-level dynamics must not share a baseline hash
        const string code = @"
dynamic first = 1;
dynamic second = 2;
";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.NotEqual(results[0].CodeSnippet, results[1].CodeSnippet);
    }

    [Fact]
    public void Test_SameDeclarationSnippetsAreDistinct()
    {
        // Arrange - two dynamics in one signature must not share a baseline hash
        const string code = @"
public class TestClass
{
    public dynamic Convert(dynamic input) => input;
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.NotEqual(results[0].CodeSnippet, results[1].CodeSnippet);
    }

    [Fact]
    public void Test_SuppressionOnConstructor()
    {
        // Arrange
        const string code = @"
public class TestClass
{
    [SlopwatchSuppress(""SW021"", ""COM interop requires late-bound dispatch here"")]
    public TestClass(dynamic comObject)
    {
    }
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Test_SuppressionOnConversionOperator()
    {
        // Arrange
        const string code = @"
public class TestClass
{
    [SlopwatchSuppress(""SW021"", ""COM interop requires late-bound dispatch here"")]
    public static implicit operator dynamic(TestClass value) => null!;
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Test_SuppressionOnDelegate()
    {
        // Arrange
        const string code = @"
[SlopwatchSuppress(""SW021"", ""COM interop requires late-bound dispatch here"")]
public delegate dynamic Handler(dynamic input);
";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Test_SuppressionOnIndexer()
    {
        // Arrange
        const string code = @"
public class TestClass
{
    [SlopwatchSuppress(""SW021"", ""COM interop requires late-bound dispatch here"")]
    public dynamic this[int index] => null!;
}";
        var context = CreateTestContext(code);

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Empty(results);
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
