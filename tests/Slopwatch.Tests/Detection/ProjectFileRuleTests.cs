using Slopwatch.Detection;
using Slopwatch.Detection.Rules;
using Xunit;

namespace Slopwatch.Tests.Detection;

/// <summary>
/// Tests for SW005 - Project File Slop Detection Rule
/// </summary>
public class ProjectFileRuleTests
{
    private readonly ProjectFileRule _rule = new();

    [Fact]
    public void Test_DetectsNoWarnAddition()
    {
        // Arrange
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <NoWarn>$(NoWarn);CS0618</NoWarn>
  </PropertyGroup>
</Project>";
        var context = CreateContext(xml, "Test.csproj");

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW005", result.RuleId);
        Assert.Contains("CS0618", result.Message);
        Assert.Contains("NoWarn", result.Message);
        Assert.Equal(4, result.LineNumber);
    }

    [Fact]
    public void Test_DetectsNoWarnWithMultipleWarnings()
    {
        // Arrange
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <NoWarn>$(NoWarn);CS0618;CS0612;CS1591</NoWarn>
  </PropertyGroup>
</Project>";
        var context = CreateContext(xml, "Test.csproj");

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW005", result.RuleId);
        Assert.Contains("CS0618;CS0612;CS1591", result.Message);
    }

    [Fact]
    public void Test_DetectsDirectNoWarnValue()
    {
        // Arrange
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <NoWarn>CS0618;CS0612</NoWarn>
  </PropertyGroup>
</Project>";
        var context = CreateContext(xml, "Test.csproj");

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW005", result.RuleId);
        Assert.Contains("CS0618;CS0612", result.Message);
    }

    [Fact]
    public void Test_DetectsTreatWarningsAsErrorsDisabled()
    {
        // Arrange
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>";
        var context = CreateContext(xml, "Test.csproj");

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW005", result.RuleId);
        Assert.Contains("TreatWarningsAsErrors is disabled", result.Message);
        Assert.Equal(DetectionSeverity.Warning, result.Severity);
        Assert.Equal(4, result.LineNumber);
    }

    [Fact]
    public void Test_IgnoresTreatWarningsAsErrorsEnabled()
    {
        // Arrange
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>";
        var context = CreateContext(xml, "Test.csproj");

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Test_DetectsNullableDisabled()
    {
        // Arrange
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>disable</Nullable>
  </PropertyGroup>
</Project>";
        var context = CreateContext(xml, "Test.csproj");

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW005", result.RuleId);
        Assert.Contains("Nullable reference types are disabled", result.Message);
        Assert.Equal(DetectionSeverity.Info, result.Severity); // Info severity for Nullable
        Assert.Equal(4, result.LineNumber);
    }

    [Fact]
    public void Test_IgnoresNullableEnabled()
    {
        // Arrange
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>";
        var context = CreateContext(xml, "Test.csproj");

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Test_IgnoresNullableAnnotations()
    {
        // Arrange
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <Nullable>annotations</Nullable>
  </PropertyGroup>
</Project>";
        var context = CreateContext(xml, "Test.csproj");

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Test_DetectsEmptyWarningsAsErrors()
    {
        // Arrange
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <WarningsAsErrors></WarningsAsErrors>
  </PropertyGroup>
</Project>";
        var context = CreateContext(xml, "Test.csproj");

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW005", result.RuleId);
        Assert.Contains("WarningsAsErrors is empty", result.Message);
        Assert.Equal(4, result.LineNumber);
    }

    [Fact]
    public void Test_IgnoresWarningsAsErrorsWithValues()
    {
        // Arrange
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <WarningsAsErrors>CS0618;CS0612</WarningsAsErrors>
  </PropertyGroup>
</Project>";
        var context = CreateContext(xml, "Test.csproj");

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Test_DetectsMultipleIssuesInSameFile()
    {
        // Arrange
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <NoWarn>$(NoWarn);CS0618</NoWarn>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <Nullable>disable</Nullable>
    <WarningsAsErrors></WarningsAsErrors>
  </PropertyGroup>
</Project>";
        var context = CreateContext(xml, "Test.csproj");

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Equal(4, results.Count);
        Assert.All(results, r => Assert.Equal("SW005", r.RuleId));

        // Verify we detect each issue
        Assert.Contains(results, r => r.Message.Contains("NoWarn"));
        Assert.Contains(results, r => r.Message.Contains("TreatWarningsAsErrors"));
        Assert.Contains(results, r => r.Message.Contains("Nullable"));
        Assert.Contains(results, r => r.Message.Contains("WarningsAsErrors"));
    }

    [Fact]
    public void Test_WorksWithPropsFiles()
    {
        // Arrange
        const string xml = @"<Project>
  <PropertyGroup>
    <NoWarn>$(NoWarn);CS0618</NoWarn>
  </PropertyGroup>
</Project>";
        var context = CreateContext(xml, "Directory.Build.props");

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW005", result.RuleId);
    }

    [Fact]
    public void Test_WorksWithTargetsFiles()
    {
        // Arrange
        const string xml = @"<Project>
  <PropertyGroup>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>";
        var context = CreateContext(xml, "Custom.targets");

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW005", result.RuleId);
    }

    [Fact]
    public void Test_IgnoresSuppressedNoWarn()
    {
        // Arrange
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <!-- slopwatch-ignore: SW005 This is a legacy project that requires warning suppression for compatibility -->
    <NoWarn>$(NoWarn);CS0618</NoWarn>
  </PropertyGroup>
</Project>";
        var context = CreateContext(xml, "Test.csproj");

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Test_IgnoresSuppressedTreatWarningsAsErrors()
    {
        // Arrange
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <!-- slopwatch-ignore: SW005 Third-party generated code requires warnings disabled to build successfully -->
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>";
        var context = CreateContext(xml, "Test.csproj");

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Test_RequiresSufficientJustificationLength()
    {
        // Arrange
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <!-- slopwatch-ignore: SW005 short -->
    <NoWarn>$(NoWarn);CS0618</NoWarn>
  </PropertyGroup>
</Project>";
        var context = CreateContext(xml, "Test.csproj");

        // Act
        var results = RunRule(context);

        // Assert
        // Should still detect because justification is too short
        var result = Assert.Single(results);
        Assert.Equal("SW005", result.RuleId);
    }

    [Fact]
    public void Test_HandlesInvalidXmlGracefully()
    {
        // Arrange
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <NoWarn>$(NoWarn);CS0618
  </PropertyGroup>
</Project>";
        var context = CreateContext(xml, "Test.csproj");

        // Act
        var results = RunRule(context);

        // Assert
        // Should not throw, should return empty results
        Assert.Empty(results);
    }

    [Fact]
    public void Test_HandlesEmptyFile()
    {
        // Arrange
        const string xml = "";
        var context = CreateContext(xml, "Test.csproj");

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Test_ProvidesCodeSnippet()
    {
        // Arrange
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <NoWarn>$(NoWarn);CS0618</NoWarn>
  </PropertyGroup>
</Project>";
        var context = CreateContext(xml, "Test.csproj");

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.NotNull(result.CodeSnippet);
        Assert.Contains("<NoWarn>", result.CodeSnippet);
    }

    [Fact]
    public void Test_ProvidesSuggestedFix()
    {
        // Arrange
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>";
        var context = CreateContext(xml, "Test.csproj");

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.NotNull(result.SuggestedFix);
        Assert.Contains("TreatWarningsAsErrors", result.SuggestedFix);
    }

    [Fact]
    public void Test_RespectsLineScope()
    {
        // Arrange
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <NoWarn>$(NoWarn);CS0618</NoWarn>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>";
        var context = CreateContextWithLineScope(xml, "Test.csproj", addedLines: new[] { 3 });

        // Act
        var results = RunRule(context);

        // Assert
        // Only line 3 (NoWarn) should be detected
        var result = Assert.Single(results);
        Assert.Equal(3, result.LineNumber);
        Assert.Contains("NoWarn", result.Message);
    }

    [Fact]
    public void Test_DetectsInMultiplePropertyGroups()
    {
        // Arrange
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <NoWarn>$(NoWarn);CS0618</NoWarn>
  </PropertyGroup>
  <PropertyGroup Condition=""'$(Configuration)' == 'Debug'"">
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>";
        var context = CreateContext(xml, "Test.csproj");

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Message.Contains("NoWarn") && r.LineNumber == 4);
        Assert.Contains(results, r => r.Message.Contains("TreatWarningsAsErrors") && r.LineNumber == 7);
    }

    [Fact]
    public void Test_CaseInsensitiveSuppression()
    {
        // Arrange
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <!-- SLOPWATCH-IGNORE: sw005 This legacy code requires specific warning configuration for backward compatibility -->
    <NoWarn>$(NoWarn);CS0618</NoWarn>
  </PropertyGroup>
</Project>";
        var context = CreateContext(xml, "Test.csproj");

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Test_ReturnsCorrectSeverities()
    {
        // Arrange
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <NoWarn>$(NoWarn);CS0618</NoWarn>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <Nullable>disable</Nullable>
    <WarningsAsErrors></WarningsAsErrors>
  </PropertyGroup>
</Project>";
        var context = CreateContext(xml, "Test.csproj");

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Equal(4, results.Count);

        // NoWarn, TreatWarningsAsErrors, and WarningsAsErrors should be Warning
        Assert.Equal(3, results.Count(r => r.Severity == DetectionSeverity.Warning));

        // Nullable should be Info
        Assert.Single(results, r => r.Severity == DetectionSeverity.Info && r.Message.Contains("Nullable"));
    }

    #region Helper Methods

    private DetectionContext CreateContext(string xml, string fileName)
    {
        return new DetectionContext(
            FilePath: $"/src/{fileName}",
            FileName: fileName,
            Content: xml,
            SyntaxTree: null, // No Roslyn syntax tree for XML files
            IsTestFile: false
        );
    }

    private DetectionContext CreateContextWithLineScope(string xml, string fileName, int[]? addedLines = null, int[]? modifiedLines = null)
    {
        return new DetectionContext(
            FilePath: $"/src/{fileName}",
            FileName: fileName,
            Content: xml,
            SyntaxTree: null,
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
