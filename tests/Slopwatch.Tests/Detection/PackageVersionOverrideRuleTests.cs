using Slopwatch.Detection;
using Slopwatch.Detection.Rules;
using Xunit;

namespace Slopwatch.Tests.Detection;

/// <summary>
/// Tests for SW006 - Package Version Override Detection Rule
/// </summary>
public class PackageVersionOverrideRuleTests : IDisposable
{
    private readonly PackageVersionOverrideRule _rule = new();

    public PackageVersionOverrideRuleTests()
    {
        // Clear cache before each test
        PackageVersionOverrideRule.ClearCache();
    }

    public void Dispose()
    {
        // Clean up cache after tests
        PackageVersionOverrideRule.ClearCache();
    }

    #region VersionOverride Tests (Always Slop)

    [Fact]
    public void Test_DetectsVersionOverrideAttribute()
    {
        // Arrange
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <ItemGroup>
    <PackageReference Include=""Newtonsoft.Json"" VersionOverride=""13.0.1"" />
  </ItemGroup>
</Project>";
        var context = CreateContext(xml, "Test.csproj");

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW006", result.RuleId);
        Assert.Contains("VersionOverride", result.Message);
        Assert.Contains("Newtonsoft.Json", result.Message);
        Assert.Contains("13.0.1", result.Message);
        Assert.Equal(DetectionSeverity.Warning, result.Severity);
        Assert.Equal(3, result.LineNumber);
    }

    [Fact]
    public void Test_DetectsVersionOverrideWithUpdateAttribute()
    {
        // Arrange - Using Update instead of Include
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <ItemGroup>
    <PackageReference Update=""xunit"" VersionOverride=""2.6.0"" />
  </ItemGroup>
</Project>";
        var context = CreateContext(xml, "Test.csproj");

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW006", result.RuleId);
        Assert.Contains("xunit", result.Message);
    }

    [Fact]
    public void Test_DetectsVersionOverrideInPropsFile()
    {
        // Arrange
        const string xml = @"<Project>
  <ItemGroup>
    <PackageReference Include=""SomePackage"" VersionOverride=""1.0.0"" />
  </ItemGroup>
</Project>";
        var context = CreateContext(xml, "Directory.Build.props");

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW006", result.RuleId);
    }

    [Fact]
    public void Test_DetectsVersionOverrideInTargetsFile()
    {
        // Arrange
        const string xml = @"<Project>
  <ItemGroup>
    <PackageReference Include=""SomePackage"" VersionOverride=""1.0.0"" />
  </ItemGroup>
</Project>";
        var context = CreateContext(xml, "Custom.targets");

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW006", result.RuleId);
    }

    [Fact]
    public void Test_DetectsMultipleVersionOverrides()
    {
        // Arrange
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <ItemGroup>
    <PackageReference Include=""PackageA"" VersionOverride=""1.0.0"" />
    <PackageReference Include=""PackageB"" VersionOverride=""2.0.0"" />
    <PackageReference Include=""PackageC"" VersionOverride=""3.0.0"" />
  </ItemGroup>
</Project>";
        var context = CreateContext(xml, "Test.csproj");

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Equal("SW006", r.RuleId));
        Assert.Contains(results, r => r.Message.Contains("PackageA"));
        Assert.Contains(results, r => r.Message.Contains("PackageB"));
        Assert.Contains(results, r => r.Message.Contains("PackageC"));
    }

    [Fact]
    public void Test_DetectsVersionOverrideEvenInDirectoryPackagesProps()
    {
        // VersionOverride shouldn't be in Directory.Packages.props but if it is, flag it
        const string xml = @"<Project>
  <ItemGroup>
    <PackageReference Include=""SomePackage"" VersionOverride=""1.0.0"" />
  </ItemGroup>
</Project>";
        var context = CreateContext(xml, "Directory.Packages.props");

        // Act
        var results = RunRule(context);

        // Assert - VersionOverride is always flagged
        var result = Assert.Single(results);
        Assert.Equal("SW006", result.RuleId);
        Assert.Contains("VersionOverride", result.Message);
    }

    #endregion

    #region Version Attribute Tests (Context-Aware)

    [Fact]
    public void Test_DetectsVersionAttributeWhenCpmEnabled()
    {
        // Arrange
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <ItemGroup>
    <PackageReference Include=""Newtonsoft.Json"" Version=""13.0.1"" />
  </ItemGroup>
</Project>";
        var context = CreateContext(xml, "Test.csproj");
        _rule.CpmEnabledOverride = true; // Simulate CPM enabled

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW006", result.RuleId);
        Assert.Contains("Version attribute", result.Message);
        Assert.Contains("Newtonsoft.Json", result.Message);
    }

    [Fact]
    public void Test_IgnoresVersionAttributeWhenNoCpm()
    {
        // Arrange
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <ItemGroup>
    <PackageReference Include=""Newtonsoft.Json"" Version=""13.0.1"" />
  </ItemGroup>
</Project>";
        var context = CreateContext(xml, "Test.csproj");
        _rule.CpmEnabledOverride = false; // Simulate CPM disabled

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Test_NeverFlagsVersionInDirectoryPackagesProps()
    {
        // Arrange - This is the correct place for versions
        const string xml = @"<Project>
  <ItemGroup>
    <PackageVersion Include=""Newtonsoft.Json"" Version=""13.0.1"" />
    <PackageVersion Include=""xunit"" Version=""2.6.0"" />
  </ItemGroup>
</Project>";
        var context = CreateContext(xml, "Directory.Packages.props");
        _rule.CpmEnabledOverride = true;

        // Act
        var results = RunRule(context);

        // Assert - PackageVersion elements are fine, and Version shouldn't be flagged here
        Assert.Empty(results);
    }

    [Fact]
    public void Test_DetectsNestedVersionElement()
    {
        // Arrange - Version as child element instead of attribute
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <ItemGroup>
    <PackageReference Include=""Newtonsoft.Json"">
      <Version>13.0.1</Version>
    </PackageReference>
  </ItemGroup>
</Project>";
        var context = CreateContext(xml, "Test.csproj");
        _rule.CpmEnabledOverride = true;

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal("SW006", result.RuleId);
        Assert.Contains("Version attribute", result.Message);
        Assert.Contains("13.0.1", result.Message);
    }

    [Fact]
    public void Test_IgnoresEmptyVersionAttribute()
    {
        // Arrange
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <ItemGroup>
    <PackageReference Include=""Newtonsoft.Json"" Version="""" />
  </ItemGroup>
</Project>";
        var context = CreateContext(xml, "Test.csproj");
        _rule.CpmEnabledOverride = true;

        // Act
        var results = RunRule(context);

        // Assert - Empty version is ignored
        Assert.Empty(results);
    }

    [Fact]
    public void Test_IgnoresPackageReferenceWithoutVersion()
    {
        // Arrange - Clean CPM usage
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <ItemGroup>
    <PackageReference Include=""Newtonsoft.Json"" />
    <PackageReference Include=""xunit"" />
  </ItemGroup>
</Project>";
        var context = CreateContext(xml, "Test.csproj");
        _rule.CpmEnabledOverride = true;

        // Act
        var results = RunRule(context);

        // Assert - No violations for proper CPM usage
        Assert.Empty(results);
    }

    [Fact]
    public void Test_DoesNotDoubleReportVersionOverrideWithVersion()
    {
        // Arrange - Has both Version and VersionOverride
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <ItemGroup>
    <PackageReference Include=""Newtonsoft.Json"" Version=""13.0.0"" VersionOverride=""13.0.1"" />
  </ItemGroup>
</Project>";
        var context = CreateContext(xml, "Test.csproj");
        _rule.CpmEnabledOverride = true;

        // Act
        var results = RunRule(context);

        // Assert - Only VersionOverride is reported, not both
        var result = Assert.Single(results);
        Assert.Contains("VersionOverride", result.Message);
    }

    #endregion

    #region Suppression Tests

    [Fact]
    public void Test_IgnoresSuppressedVersionOverride()
    {
        // Arrange
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <ItemGroup>
    <!-- slopwatch-ignore: SW006 This package requires a specific version due to API compatibility requirements -->
    <PackageReference Include=""Newtonsoft.Json"" VersionOverride=""13.0.1"" />
  </ItemGroup>
</Project>";
        var context = CreateContext(xml, "Test.csproj");

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Test_IgnoresSuppressedVersionAttribute()
    {
        // Arrange
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <ItemGroup>
    <!-- slopwatch-ignore: SW006 This test project needs an isolated version for testing purposes -->
    <PackageReference Include=""Newtonsoft.Json"" Version=""13.0.1"" />
  </ItemGroup>
</Project>";
        var context = CreateContext(xml, "Test.csproj");
        _rule.CpmEnabledOverride = true;

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Test_RequiresSufficientJustificationLength()
    {
        // Arrange - Short justification (less than 20 chars)
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <ItemGroup>
    <!-- slopwatch-ignore: SW006 short -->
    <PackageReference Include=""Newtonsoft.Json"" VersionOverride=""13.0.1"" />
  </ItemGroup>
</Project>";
        var context = CreateContext(xml, "Test.csproj");

        // Act
        var results = RunRule(context);

        // Assert - Should still detect because justification is too short
        var result = Assert.Single(results);
        Assert.Equal("SW006", result.RuleId);
    }

    [Fact]
    public void Test_CaseInsensitiveSuppression()
    {
        // Arrange
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <ItemGroup>
    <!-- SLOPWATCH-IGNORE: sw006 This package requires a specific version due to API compatibility requirements -->
    <PackageReference Include=""Newtonsoft.Json"" VersionOverride=""13.0.1"" />
  </ItemGroup>
</Project>";
        var context = CreateContext(xml, "Test.csproj");

        // Act
        var results = RunRule(context);

        // Assert
        Assert.Empty(results);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Test_HandlesInvalidXmlGracefully()
    {
        // Arrange - Malformed XML
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <ItemGroup>
    <PackageReference Include=""Newtonsoft.Json"" VersionOverride=""13.0.1""
  </ItemGroup>
</Project>";
        var context = CreateContext(xml, "Test.csproj");

        // Act
        var results = RunRule(context);

        // Assert - Should not throw, should return empty results
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
    public void Test_RespectsLineScope()
    {
        // Arrange
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <ItemGroup>
    <PackageReference Include=""PackageA"" VersionOverride=""1.0.0"" />
    <PackageReference Include=""PackageB"" VersionOverride=""2.0.0"" />
  </ItemGroup>
</Project>";
        // Only line 3 is in scope
        var context = CreateContextWithLineScope(xml, "Test.csproj", addedLines: new[] { 3 });

        // Act
        var results = RunRule(context);

        // Assert - Only line 3 (PackageA) should be detected
        var result = Assert.Single(results);
        Assert.Equal(3, result.LineNumber);
        Assert.Contains("PackageA", result.Message);
    }

    [Fact]
    public void Test_ProvidesCodeSnippet()
    {
        // Arrange
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <ItemGroup>
    <PackageReference Include=""Newtonsoft.Json"" VersionOverride=""13.0.1"" />
  </ItemGroup>
</Project>";
        var context = CreateContext(xml, "Test.csproj");

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.NotNull(result.CodeSnippet);
        Assert.Contains("<PackageReference", result.CodeSnippet);
        Assert.Contains("VersionOverride", result.CodeSnippet);
    }

    [Fact]
    public void Test_ProvidesSuggestedFix()
    {
        // Arrange
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <ItemGroup>
    <PackageReference Include=""Newtonsoft.Json"" VersionOverride=""13.0.1"" />
  </ItemGroup>
</Project>";
        var context = CreateContext(xml, "Test.csproj");

        // Act
        var results = RunRule(context);

        // Assert
        var result = Assert.Single(results);
        Assert.NotNull(result.SuggestedFix);
        Assert.Contains("Directory.Packages.props", result.SuggestedFix);
    }

    [Fact]
    public void Test_HandlesConditionalItemGroups()
    {
        // Arrange
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <ItemGroup Condition=""'$(TargetFramework)' == 'net8.0'"">
    <PackageReference Include=""Newtonsoft.Json"" VersionOverride=""13.0.1"" />
  </ItemGroup>
</Project>";
        var context = CreateContext(xml, "Test.csproj");

        // Act
        var results = RunRule(context);

        // Assert - Should still detect in conditional ItemGroups
        var result = Assert.Single(results);
        Assert.Equal("SW006", result.RuleId);
    }

    #endregion

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
