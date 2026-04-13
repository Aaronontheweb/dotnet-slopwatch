using Slopwatch.Analysis;
using Slopwatch.Baseline;
using Slopwatch.Detection;
using Slopwatch.Detection.Rules;
using Xunit;

namespace Slopwatch.Tests.Detection;

public class ProjectFileRuleConfigSuppressionTests
{
    private readonly ProjectFileRule _rule = new();

    [Fact]
    public void Test_ConfigSuppression_IgnoresProjectFileIssue()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var slopwatchDir = Path.Combine(tempRoot, ".slopwatch");
            Directory.CreateDirectory(slopwatchDir);

            var configPath = Path.Combine(slopwatchDir, "config.json");
            const string configJson = @"{
  ""suppressions"": [
    {
      ""ruleId"": ""SW005"",
      ""pattern"": ""Directory.Build.props"",
      ""justification"": ""CS1591 is no-warned due to documentation file generation requirements""
    }
  ],
  ""globalSuppressions"": []
}";
            File.WriteAllText(configPath, configJson);

            const string xml = @"<Project>
  <PropertyGroup>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
  </PropertyGroup>
</Project>";

            var filePath = Path.Combine(tempRoot, "Directory.Build.props");
            var context = new DetectionContext(
                FilePath: filePath,
                FileName: Path.GetFileName(filePath),
                Content: xml,
                SyntaxTree: null,
                IsTestFile: false
            );

            var results = RunRule(context);

            Assert.Empty(results);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [Fact]
    public async Task Test_ConfigSuppression_PreventsBaselineEntry()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var slopwatchDir = Path.Combine(tempRoot, ".slopwatch");
            Directory.CreateDirectory(slopwatchDir);

            var configPath = Path.Combine(slopwatchDir, "config.json");
            const string configJson = @"{
  ""suppressions"": [
    {
      ""ruleId"": ""SW005"",
      ""pattern"": ""Directory.Build.props"",
      ""justification"": ""CS1591 is no-warned due to documentation file generation requirements""
    }
  ],
  ""globalSuppressions"": []
}";
            File.WriteAllText(configPath, configJson);

            var propsPath = Path.Combine(tempRoot, "Directory.Build.props");
            const string xml = @"<Project>
  <PropertyGroup>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
  </PropertyGroup>
</Project>";
            File.WriteAllText(propsPath, xml);

            var analyzer = new FileAnalyzer(new IDetectionRule[] { new ProjectFileRule() });
            var results = new List<DetectionResult>();

            await foreach (var result in analyzer.AnalyzeDirectoryAsync(tempRoot, new[] { "**/*.props" }))
            {
                results.Add(result);
            }

            var baseline = BaselineFile.Create(results, tempRoot, "Baseline for suppression test");

            Assert.DoesNotContain(baseline.Entries, entry => entry.RuleId == "SW005");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
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
}
