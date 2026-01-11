using Slopwatch.Suppression;
using Xunit;

namespace Slopwatch.Tests.Suppression;

/// <summary>
/// Tests for inline comment suppression parsing and matching.
/// </summary>
public class InlineCommentSuppressionTests
{
    [Fact]
    public void Parse_SingleLineComment_WithValidJustification_ParsesCorrectly()
    {
        // Arrange
        const string sourceCode = @"
public class TestClass
{
    // slopwatch-ignore: SW001 This is a platform-specific test requiring Windows COM
    [Fact(Skip = ""Windows only"")]
    public void TestMethod()
    {
    }
}";

        // Act
        var suppressions = InlineCommentSuppressionParser.Parse(sourceCode);

        // Assert
        Assert.Single(suppressions);
        var suppression = suppressions[0];
        Assert.Equal("SW001", suppression.RuleId);
        Assert.Equal("This is a platform-specific test requiring Windows COM", suppression.Justification);
        Assert.Equal(4, suppression.StartLine);
        Assert.Equal(5, suppression.EndLine); // Should cover the next line
        Assert.False(suppression.IsBlock);
    }

    [Fact]
    public void Parse_SingleLineComment_CaseInsensitive_ParsesCorrectly()
    {
        // Arrange
        const string sourceCode = @"
public class TestClass
{
    // SLOPWATCH-IGNORE: SW002 Legacy interop code requires unsafe pointer manipulation
    unsafe void ProcessMemory() { }
}";

        // Act
        var suppressions = InlineCommentSuppressionParser.Parse(sourceCode);

        // Assert
        Assert.Single(suppressions);
        var suppression = suppressions[0];
        Assert.Equal("SW002", suppression.RuleId);
        Assert.Equal("Legacy interop code requires unsafe pointer manipulation", suppression.Justification);
    }

    [Fact]
    public void Parse_SingleLineComment_TooShortJustification_Ignored()
    {
        // Arrange
        const string sourceCode = @"
public class TestClass
{
    // slopwatch-ignore: SW001 Too short
    [Fact(Skip = ""Test"")]
    public void TestMethod() { }
}";

        // Act
        var suppressions = InlineCommentSuppressionParser.Parse(sourceCode);

        // Assert
        Assert.Empty(suppressions); // Should be ignored because justification is too short
    }

    [Fact]
    public void Parse_BlockComment_WithValidJustification_ParsesCorrectly()
    {
        // Arrange
        const string sourceCode = @"
public class TestClass
{
    // slopwatch-ignore-start: SW002 Legacy code requires multiple warning suppressions
    #pragma warning disable CS0618
    void OldMethod()
    {
        ObsoleteApi();
    }
    #pragma warning restore CS0618
    // slopwatch-ignore-end

    void NewMethod() { }
}";

        // Act
        var suppressions = InlineCommentSuppressionParser.Parse(sourceCode);

        // Assert
        Assert.Single(suppressions);
        var suppression = suppressions[0];
        Assert.Equal("SW002", suppression.RuleId);
        Assert.Equal("Legacy code requires multiple warning suppressions", suppression.Justification);
        Assert.Equal(4, suppression.StartLine);
        Assert.Equal(11, suppression.EndLine); // Line 11 is where slopwatch-ignore-end appears
        Assert.True(suppression.IsBlock);
    }

    [Fact]
    public void Parse_MultipleSuppressions_ParsesAllCorrectly()
    {
        // Arrange
        const string sourceCode = @"
public class TestClass
{
    // slopwatch-ignore: SW001 Platform-specific test for Windows COM interop
    [Fact(Skip = ""Windows only"")]
    public void WindowsTest() { }

    // slopwatch-ignore: SW004 Testing rate limiter requires actual time delays
    [Fact]
    public async Task RateLimitTest()
    {
        await Task.Delay(100);
    }
}";

        // Act
        var suppressions = InlineCommentSuppressionParser.Parse(sourceCode);

        // Assert
        Assert.Equal(2, suppressions.Count);

        var first = suppressions[0];
        Assert.Equal("SW001", first.RuleId);
        Assert.Equal(4, first.StartLine);

        var second = suppressions[1];
        Assert.Equal("SW004", second.RuleId);
        Assert.Equal(8, second.StartLine);
    }

    [Fact]
    public void Parse_NestedBlocks_ParsesCorrectly()
    {
        // Arrange
        const string sourceCode = @"
public class TestClass
{
    // slopwatch-ignore-start: SW002 Outer suppression for legacy module integration
    void OuterMethod()
    {
        #pragma warning disable CS0618
    }
    // slopwatch-ignore-end

    void NormalMethod() { }
}";

        // Act
        var suppressions = InlineCommentSuppressionParser.Parse(sourceCode);

        // Assert
        Assert.Single(suppressions);
        var suppression = suppressions[0];
        Assert.Equal(4, suppression.StartLine);
        Assert.Equal(9, suppression.EndLine);
    }

    [Fact]
    public void IsSuppressed_LineInRange_ReturnsTrue()
    {
        // Arrange
        var suppressions = new List<InlineCommentSuppression>
        {
            new()
            {
                RuleId = "SW001",
                Justification = "Test suppression with sufficient length",
                StartLine = 10,
                EndLine = 15,
                IsBlock = true
            }
        };

        // Act & Assert
        Assert.True(InlineCommentSuppressionParser.IsSuppressed(suppressions, "SW001", 10));
        Assert.True(InlineCommentSuppressionParser.IsSuppressed(suppressions, "SW001", 12));
        Assert.True(InlineCommentSuppressionParser.IsSuppressed(suppressions, "SW001", 15));
    }

    [Fact]
    public void IsSuppressed_LineOutOfRange_ReturnsFalse()
    {
        // Arrange
        var suppressions = new List<InlineCommentSuppression>
        {
            new()
            {
                RuleId = "SW001",
                Justification = "Test suppression with sufficient length",
                StartLine = 10,
                EndLine = 15,
                IsBlock = true
            }
        };

        // Act & Assert
        Assert.False(InlineCommentSuppressionParser.IsSuppressed(suppressions, "SW001", 9));
        Assert.False(InlineCommentSuppressionParser.IsSuppressed(suppressions, "SW001", 16));
    }

    [Fact]
    public void IsSuppressed_WrongRuleId_ReturnsFalse()
    {
        // Arrange
        var suppressions = new List<InlineCommentSuppression>
        {
            new()
            {
                RuleId = "SW001",
                Justification = "Test suppression with sufficient length",
                StartLine = 10,
                EndLine = 15,
                IsBlock = true
            }
        };

        // Act & Assert
        Assert.False(InlineCommentSuppressionParser.IsSuppressed(suppressions, "SW002", 12));
    }

    [Fact]
    public void IsSuppressed_CaseInsensitiveRuleId_ReturnsTrue()
    {
        // Arrange
        var suppressions = new List<InlineCommentSuppression>
        {
            new()
            {
                RuleId = "SW001",
                Justification = "Test suppression with sufficient length",
                StartLine = 10,
                EndLine = 15,
                IsBlock = true
            }
        };

        // Act & Assert
        Assert.True(InlineCommentSuppressionParser.IsSuppressed(suppressions, "sw001", 12));
        Assert.True(InlineCommentSuppressionParser.IsSuppressed(suppressions, "Sw001", 12));
    }

    [Fact]
    public void CoversLine_SingleLineSuppression_CoversSingleLine()
    {
        // Arrange
        var suppression = new InlineCommentSuppression
        {
            RuleId = "SW001",
            Justification = "Test suppression with sufficient length",
            StartLine = 10,
            EndLine = 11,
            IsBlock = false
        };

        // Act & Assert
        Assert.False(suppression.CoversLine(9));
        Assert.True(suppression.CoversLine(10));
        Assert.True(suppression.CoversLine(11));
        Assert.False(suppression.CoversLine(12));
    }

    [Fact]
    public void Parse_WithExtraWhitespace_ParsesCorrectly()
    {
        // Arrange
        const string sourceCode = @"
public class TestClass
{
    //    slopwatch-ignore:    SW001    Platform-specific test requiring Windows COM components
    [Fact(Skip = ""Windows only"")]
    public void TestMethod() { }
}";

        // Act
        var suppressions = InlineCommentSuppressionParser.Parse(sourceCode);

        // Assert
        Assert.Single(suppressions);
        var suppression = suppressions[0];
        Assert.Equal("SW001", suppression.RuleId);
        Assert.Equal("Platform-specific test requiring Windows COM components", suppression.Justification);
    }
}
