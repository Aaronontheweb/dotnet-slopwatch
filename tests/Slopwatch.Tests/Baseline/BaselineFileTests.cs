using Slopwatch.Baseline;
using Slopwatch.Detection;
using Xunit;

namespace Slopwatch.Tests.Baseline;

public class BaselineFileTests
{
    private static DetectionResult CreateResult(
        string ruleId,
        string filePath,
        int lineNumber,
        string? codeSnippet = null,
        string message = "Test message")
    {
        return new DetectionResult(
            RuleId: ruleId,
            RuleName: "Test Rule",
            Severity: DetectionSeverity.Warning,
            FilePath: filePath,
            LineNumber: lineNumber,
            Column: 1,
            Message: message,
            CodeSnippet: codeSnippet);
    }

    [Fact]
    public void ComputeHash_SameInput_ProducesSameHash()
    {
        var hash1 = BaselineEntry.ComputeHash("SW001", "src/Test.cs", "[Fact(Skip = \"test\")]");
        var hash2 = BaselineEntry.ComputeHash("SW001", "src/Test.cs", "[Fact(Skip = \"test\")]");

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_DifferentRuleId_ProducesDifferentHash()
    {
        var hash1 = BaselineEntry.ComputeHash("SW001", "src/Test.cs", "[Fact(Skip = \"test\")]");
        var hash2 = BaselineEntry.ComputeHash("SW002", "src/Test.cs", "[Fact(Skip = \"test\")]");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_DifferentFilePath_ProducesDifferentHash()
    {
        var hash1 = BaselineEntry.ComputeHash("SW001", "src/Test.cs", "[Fact(Skip = \"test\")]");
        var hash2 = BaselineEntry.ComputeHash("SW001", "src/Other.cs", "[Fact(Skip = \"test\")]");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_DifferentCodeSnippet_ProducesDifferentHash()
    {
        var hash1 = BaselineEntry.ComputeHash("SW001", "src/Test.cs", "[Fact(Skip = \"test\")]");
        var hash2 = BaselineEntry.ComputeHash("SW001", "src/Test.cs", "[Fact(Skip = \"other\")]");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_NormalizesWhitespace()
    {
        var hash1 = BaselineEntry.ComputeHash("SW001", "src/Test.cs", "  [Fact(Skip = \"test\")]  ");
        var hash2 = BaselineEntry.ComputeHash("SW001", "src/Test.cs", "[Fact(Skip = \"test\")]");

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_NormalizesLineEndings()
    {
        var hash1 = BaselineEntry.ComputeHash("SW001", "src/Test.cs", "line1\r\nline2");
        var hash2 = BaselineEntry.ComputeHash("SW001", "src/Test.cs", "line1\nline2");

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_NormalizesPathSeparators()
    {
        var hash1 = BaselineEntry.ComputeHash("SW001", "src\\Test.cs", "code");
        var hash2 = BaselineEntry.ComputeHash("SW001", "src/Test.cs", "code");

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void IsBaselined_ReturnsTrueForMatchingEntry()
    {
        var baseline = new BaselineFile();
        var result = CreateResult("SW001", "/root/src/Test.cs", 10, "[Fact(Skip = \"test\")]");

        baseline.AddEntry(result, "/root");

        Assert.True(baseline.IsBaselined("SW001", "src/Test.cs", "[Fact(Skip = \"test\")]"));
    }

    [Fact]
    public void IsBaselined_ReturnsFalseForNonMatchingEntry()
    {
        var baseline = new BaselineFile();
        var result = CreateResult("SW001", "/root/src/Test.cs", 10, "[Fact(Skip = \"test\")]");

        baseline.AddEntry(result, "/root");

        Assert.False(baseline.IsBaselined("SW002", "src/Test.cs", "[Fact(Skip = \"test\")]"));
    }

    [Fact]
    public void IsBaselined_WithDetectionResult_MatchesCorrectly()
    {
        var baseline = new BaselineFile();
        var result = CreateResult("SW001", "/root/src/Test.cs", 10, "[Fact(Skip = \"test\")]");

        baseline.AddEntry(result, "/root");

        Assert.True(baseline.IsBaselined(result, "/root"));
    }

    [Fact]
    public void AddEntry_ReturnsTrue_WhenNewEntry()
    {
        var baseline = new BaselineFile();
        var result = CreateResult("SW001", "/root/src/Test.cs", 10, "[Fact(Skip = \"test\")]");

        var added = baseline.AddEntry(result, "/root");

        Assert.True(added);
        Assert.Single(baseline.Entries);
    }

    [Fact]
    public void AddEntry_ReturnsFalse_WhenDuplicateEntry()
    {
        var baseline = new BaselineFile();
        var result = CreateResult("SW001", "/root/src/Test.cs", 10, "[Fact(Skip = \"test\")]");

        baseline.AddEntry(result, "/root");
        var added = baseline.AddEntry(result, "/root");

        Assert.False(added);
        Assert.Single(baseline.Entries);
    }

    [Fact]
    public void AddEntry_IgnoresLineNumber_ForDuplicateDetection()
    {
        var baseline = new BaselineFile();
        var result1 = CreateResult("SW001", "/root/src/Test.cs", 10, "[Fact(Skip = \"test\")]");
        var result2 = CreateResult("SW001", "/root/src/Test.cs", 20, "[Fact(Skip = \"test\")]");

        baseline.AddEntry(result1, "/root");
        var added = baseline.AddEntry(result2, "/root");

        // Same code, same file, same rule = duplicate (line number doesn't matter)
        Assert.False(added);
        Assert.Single(baseline.Entries);
    }

    [Fact]
    public void Create_CreatesBaselineFromResults()
    {
        var results = new[]
        {
            CreateResult("SW001", "/root/src/Test1.cs", 10, "code1"),
            CreateResult("SW002", "/root/src/Test2.cs", 20, "code2"),
            CreateResult("SW001", "/root/src/Test3.cs", 30, "code3")
        };

        var baseline = BaselineFile.Create(results, "/root", "Test baseline");

        Assert.Equal(3, baseline.Entries.Count);
        Assert.Equal("Test baseline", baseline.Description);
    }

    [Fact]
    public void GetEntriesByRule_GroupsCorrectly()
    {
        var results = new[]
        {
            CreateResult("SW001", "/root/src/Test1.cs", 10, "code1"),
            CreateResult("SW002", "/root/src/Test2.cs", 20, "code2"),
            CreateResult("SW001", "/root/src/Test3.cs", 30, "code3")
        };

        var baseline = BaselineFile.Create(results, "/root");

        var byRule = baseline.GetEntriesByRule();

        Assert.Equal(2, byRule["SW001"]);
        Assert.Equal(1, byRule["SW002"]);
    }

    [Fact]
    public async Task FilterNewDetectionsAsync_FiltersBaselinedResults()
    {
        var baselinedResults = new[]
        {
            CreateResult("SW001", "/root/src/Test1.cs", 10, "code1"),
            CreateResult("SW002", "/root/src/Test2.cs", 20, "code2")
        };
        var baseline = BaselineFile.Create(baselinedResults, "/root");

        var allResults = new[]
        {
            CreateResult("SW001", "/root/src/Test1.cs", 10, "code1"),  // baselined
            CreateResult("SW002", "/root/src/Test2.cs", 20, "code2"),  // baselined
            CreateResult("SW003", "/root/src/Test3.cs", 30, "code3")   // new
        };

        var filtered = new List<DetectionResult>();
        await foreach (var result in baseline.FilterNewDetectionsAsync(ToAsyncEnumerable(allResults), "/root"))
        {
            filtered.Add(result);
        }

        Assert.Single(filtered);
        Assert.Equal("SW003", filtered[0].RuleId);
    }

    [Fact]
    public async Task SaveAsync_And_LoadAsync_RoundTrips()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"slopwatch-test-{Guid.NewGuid()}.json");
        try
        {
            var results = new[]
            {
                CreateResult("SW001", "/root/src/Test.cs", 10, "code1"),
                CreateResult("SW002", "/root/src/Other.cs", 20, "code2")
            };
            var original = BaselineFile.Create(results, "/root", "Test baseline");

            await original.SaveAsync(tempPath);
            var loaded = await BaselineFile.LoadAsync(tempPath);

            Assert.NotNull(loaded);
            Assert.Equal(original.Entries.Count, loaded.Entries.Count);
            Assert.Equal(original.Description, loaded.Description);
            Assert.Equal(original.Version, loaded.Version);

            // Verify entries match
            for (int i = 0; i < original.Entries.Count; i++)
            {
                Assert.Equal(original.Entries[i].Hash, loaded.Entries[i].Hash);
                Assert.Equal(original.Entries[i].RuleId, loaded.Entries[i].RuleId);
                Assert.Equal(original.Entries[i].FilePath, loaded.Entries[i].FilePath);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task LoadAsync_ReturnsNull_WhenFileNotFound()
    {
        var result = await BaselineFile.LoadAsync("/nonexistent/path/baseline.json");

        Assert.Null(result);
    }

    [Fact]
    public void IsBaselined_HandlesNullCodeSnippet()
    {
        var baseline = new BaselineFile();
        var result = CreateResult("SW001", "/root/src/Test.cs", 10, codeSnippet: null);

        baseline.AddEntry(result, "/root");

        Assert.True(baseline.IsBaselined(result, "/root"));
    }

    [Fact]
    public void Create_HandlesEmptyResults()
    {
        var baseline = BaselineFile.Create(Array.Empty<DetectionResult>(), "/root");

        Assert.Empty(baseline.Entries);
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> source)
    {
        foreach (var item in source)
        {
            yield return item;
        }
        await Task.CompletedTask;
    }
}
