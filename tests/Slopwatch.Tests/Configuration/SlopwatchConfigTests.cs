using System.Text.Json;
using Slopwatch.Configuration;
using Xunit;

namespace Slopwatch.Tests.Configuration;

/// <summary>
/// Tests for Slopwatch configuration file parsing and validation.
/// </summary>
public class SlopwatchConfigTests
{
    [Fact]
    public void Deserialize_ValidConfig_ParsesCorrectly()
    {
        // Arrange
        const string json = @"{
            ""suppressions"": [
                {
                    ""ruleId"": ""SW002"",
                    ""pattern"": ""**/Generated/**"",
                    ""justification"": ""Generated code from protobuf compiler"",
                    ""issueUrl"": ""https://github.com/org/repo/issues/123""
                }
            ],
            ""globalSuppressions"": [
                {
                    ""ruleId"": ""SW004"",
                    ""justification"": ""Integration test project uses real async coordination"",
                    ""issueUrl"": ""https://github.com/org/repo/issues/456""
                }
            ]
        }";

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        // Act
        var config = JsonSerializer.Deserialize<SlopwatchConfig>(json, options);

        // Assert
        Assert.NotNull(config);
        Assert.Single(config.Suppressions);
        Assert.Single(config.GlobalSuppressions);

        var suppression = config.Suppressions[0];
        Assert.Equal("SW002", suppression.RuleId);
        Assert.Equal("**/Generated/**", suppression.Pattern);
        Assert.Equal("Generated code from protobuf compiler", suppression.Justification);
        Assert.Equal("https://github.com/org/repo/issues/123", suppression.IssueUrl);

        var globalSuppression = config.GlobalSuppressions[0];
        Assert.Equal("SW004", globalSuppression.RuleId);
        Assert.Equal("Integration test project uses real async coordination", globalSuppression.Justification);
    }

    [Fact]
    public void Deserialize_ConfigWithComments_ParsesCorrectly()
    {
        // Arrange
        const string json = @"{
            // This is a comment
            ""suppressions"": [
                {
                    ""ruleId"": ""SW001"",
                    ""pattern"": ""**/*.Designer.cs"",
                    ""justification"": ""Designer-generated files"" // inline comment
                }
            ],
            ""globalSuppressions"": []
        }";

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        // Act
        var config = JsonSerializer.Deserialize<SlopwatchConfig>(json, options);

        // Assert
        Assert.NotNull(config);
        Assert.Single(config.Suppressions);
    }

    [Fact]
    public void PathSuppression_IsExpired_WithFutureDate_ReturnsFalse()
    {
        // Arrange
        var suppression = new PathSuppression
        {
            RuleId = "SW001",
            Pattern = "**/*.cs",
            Justification = "Test suppression with sufficient length",
            ExpiresAt = "2099-12-31"
        };

        // Act
        var isExpired = suppression.IsExpired();

        // Assert
        Assert.False(isExpired);
    }

    [Fact]
    public void PathSuppression_IsExpired_WithPastDate_ReturnsTrue()
    {
        // Arrange
        var suppression = new PathSuppression
        {
            RuleId = "SW001",
            Pattern = "**/*.cs",
            Justification = "Test suppression with sufficient length",
            ExpiresAt = "2020-01-01"
        };

        // Act
        var isExpired = suppression.IsExpired();

        // Assert
        Assert.True(isExpired);
    }

    [Fact]
    public void PathSuppression_IsExpired_WithNoDate_ReturnsFalse()
    {
        // Arrange
        var suppression = new PathSuppression
        {
            RuleId = "SW001",
            Pattern = "**/*.cs",
            Justification = "Test suppression with sufficient length"
        };

        // Act
        var isExpired = suppression.IsExpired();

        // Assert
        Assert.False(isExpired);
    }

    [Fact]
    public void PathSuppression_IsExpired_WithInvalidDate_ReturnsFalse()
    {
        // Arrange
        var suppression = new PathSuppression
        {
            RuleId = "SW001",
            Pattern = "**/*.cs",
            Justification = "Test suppression with sufficient length",
            ExpiresAt = "invalid-date"
        };

        // Act
        var isExpired = suppression.IsExpired();

        // Assert
        Assert.False(isExpired); // Invalid date treated as no expiration
    }

    [Fact]
    public void GlobalSuppression_IsExpired_WithFutureDate_ReturnsFalse()
    {
        // Arrange
        var suppression = new GlobalSuppression
        {
            RuleId = "SW004",
            Justification = "Test suppression with sufficient length",
            ExpiresAt = "2099-12-31"
        };

        // Act
        var isExpired = suppression.IsExpired();

        // Assert
        Assert.False(isExpired);
    }

    [Fact]
    public void GlobalSuppression_IsExpired_WithPastDate_ReturnsTrue()
    {
        // Arrange
        var suppression = new GlobalSuppression
        {
            RuleId = "SW004",
            Justification = "Test suppression with sufficient length",
            ExpiresAt = "2020-01-01"
        };

        // Act
        var isExpired = suppression.IsExpired();

        // Assert
        Assert.True(isExpired);
    }

    [Fact]
    public void Deserialize_ComplexConfig_WithAllFields_ParsesCorrectly()
    {
        // Arrange
        const string json = @"{
            ""suppressions"": [
                {
                    ""ruleId"": ""SW003"",
                    ""pattern"": ""src/Legacy/**"",
                    ""justification"": ""Legacy code scheduled for refactoring in Q2 - JIRA-1234"",
                    ""issueUrl"": ""https://jira.company.com/browse/PROJ-1234"",
                    ""expiresAt"": ""2026-06-01"",
                    ""reviewer"": ""john.doe@company.com""
                }
            ],
            ""globalSuppressions"": [
                {
                    ""ruleId"": ""SW004"",
                    ""justification"": ""Integration test project uses real async coordination"",
                    ""issueUrl"": ""https://github.com/org/repo/issues/123"",
                    ""expiresAt"": ""2027-01-01"",
                    ""reviewer"": ""security-team@company.com""
                }
            ]
        }";

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        // Act
        var config = JsonSerializer.Deserialize<SlopwatchConfig>(json, options);

        // Assert
        Assert.NotNull(config);

        var suppression = config.Suppressions[0];
        Assert.Equal("SW003", suppression.RuleId);
        Assert.Equal("src/Legacy/**", suppression.Pattern);
        Assert.Equal("Legacy code scheduled for refactoring in Q2 - JIRA-1234", suppression.Justification);
        Assert.Equal("https://jira.company.com/browse/PROJ-1234", suppression.IssueUrl);
        Assert.Equal("2026-06-01", suppression.ExpiresAt);
        Assert.Equal("john.doe@company.com", suppression.Reviewer);

        var globalSuppression = config.GlobalSuppressions[0];
        Assert.Equal("SW004", globalSuppression.RuleId);
        Assert.Equal("Integration test project uses real async coordination", globalSuppression.Justification);
        Assert.Equal("https://github.com/org/repo/issues/123", globalSuppression.IssueUrl);
        Assert.Equal("2027-01-01", globalSuppression.ExpiresAt);
        Assert.Equal("security-team@company.com", globalSuppression.Reviewer);
    }

    [Fact]
    public void Deserialize_EmptyConfig_ParsesCorrectly()
    {
        // Arrange
        const string json = @"{
            ""suppressions"": [],
            ""globalSuppressions"": []
        }";

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        // Act
        var config = JsonSerializer.Deserialize<SlopwatchConfig>(json, options);

        // Assert
        Assert.NotNull(config);
        Assert.Empty(config.Suppressions);
        Assert.Empty(config.GlobalSuppressions);
    }

    [Fact]
    public void Deserialize_MissingOptionalFields_ParsesCorrectly()
    {
        // Arrange
        const string json = @"{
            ""suppressions"": [
                {
                    ""ruleId"": ""SW001"",
                    ""pattern"": ""**/*.cs"",
                    ""justification"": ""Test suppression""
                }
            ],
            ""globalSuppressions"": []
        }";

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        // Act
        var config = JsonSerializer.Deserialize<SlopwatchConfig>(json, options);

        // Assert
        Assert.NotNull(config);
        var suppression = config.Suppressions[0];
        Assert.Null(suppression.IssueUrl);
        Assert.Null(suppression.ExpiresAt);
        Assert.Null(suppression.Reviewer);
    }
}
