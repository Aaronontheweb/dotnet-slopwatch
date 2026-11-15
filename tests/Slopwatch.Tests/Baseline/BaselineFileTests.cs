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

    [Fact]
    public void SyncWithDetections_RemovesStaleOnlyForScannedFiles()
    {
        var baseline = new BaselineFile();
        var staleScannedFile = CreateResult("SW001", "/root/src/A.cs", 10, "old-a");
        var staleOutsideScopeFile = CreateResult("SW001", "/root/src/B.cs", 20, "keep-b");
        baseline.AddEntry(staleScannedFile, "/root");
        baseline.AddEntry(staleOutsideScopeFile, "/root");

        var currentResults = new[]
        {
            CreateResult("SW001", "/root/src/A.cs", 10, "new-a")
        };

        var (removed, added, kept) = baseline.SyncWithDetections(
            currentResults,
            "/root",
            new[] { "/root/src/A.cs" });

        Assert.Equal(1, removed);
        Assert.Equal(1, added);
        Assert.Equal(0, kept);
        Assert.Equal(2, baseline.Entries.Count);

        Assert.Contains(baseline.Entries, e => e.FilePath == "src/A.cs" && e.RuleId == "SW001" && e.CodeSnippet == "new-a");
        Assert.Contains(baseline.Entries, e => e.FilePath == "src/B.cs" && e.CodeSnippet == "keep-b");
    }

    [Fact]
    public async Task LoadAsync_ThrowsForOlderSchemaVersion()
    {
        // A version 1 baseline hashed SW001-SW006 entries from raw syntax text. The current
        // identity scheme cannot match those hashes, so loading must fail before analysis runs
        // rather than silently reporting the whole baseline as new findings.
        var path = Path.Combine(Path.GetTempPath(), $"slopwatch-v1-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, """
                {
                  "version": 1,
                  "createdAt": "2026-01-01T00:00:00Z",
                  "updatedAt": "2026-01-01T00:00:00Z",
                  "entries": [
                    {
                      "hash": "b9eaa094c24d14bd",
                      "ruleId": "SW004",
                      "filePath": "tests/Foo.cs",
                      "lineNumber": 10,
                      "codeSnippet": "Task.Delay(100)",
                      "message": "old",
                      "baselinedAt": "2026-01-01T00:00:00Z"
                    }
                  ]
                }
                """);

            var ex = await Assert.ThrowsAsync<BaselineVersionException>(
                () => BaselineFile.LoadAsync(path));

            Assert.Equal(1, ex.FoundVersion);
            Assert.Equal(BaselineFile.CurrentVersion, ex.RequiredVersion);
            Assert.Contains("slopwatch init --force", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_ThrowsWhenVersionPropertyIsAbsent()
    {
        // A baseline written before versioning existed has no "version" property at all. The
        // property defaults to CurrentVersion so a freshly constructed baseline serializes
        // correctly, which makes an omitted property indistinguishable from an explicit current
        // version unless presence is checked on the JSON itself. Treating it as current would let
        // it load and silently match nothing.
        var path = Path.Combine(Path.GetTempPath(), $"slopwatch-noversion-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, """
                {
                  "createdAt": "2026-01-01T00:00:00Z",
                  "updatedAt": "2026-01-01T00:00:00Z",
                  "entries": [
                    {
                      "hash": "b9eaa094c24d14bd",
                      "ruleId": "SW004",
                      "filePath": "tests/Foo.cs",
                      "lineNumber": 10,
                      "codeSnippet": "Task.Delay(100)",
                      "message": "old",
                      "baselinedAt": "2026-01-01T00:00:00Z"
                    }
                  ]
                }
                """);

            var ex = await Assert.ThrowsAsync<BaselineVersionException>(
                () => BaselineFile.LoadAsync(path));

            Assert.Equal(BaselineFile.CurrentVersion, ex.RequiredVersion);
            Assert.Contains("slopwatch init --force", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("\"two\"")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[]")]
    public async Task LoadAsync_TreatsUnreadableVersionAsOutdated(string versionJson)
    {
        // A version that is present but not an integer cannot be matched against the current
        // scheme. It must produce the same actionable "regenerate" message as a version 1 file,
        // not a raw JsonException from the serializer. Deserializing before the version check
        // would surface the serializer's error instead.
        var path = Path.Combine(Path.GetTempPath(), $"slopwatch-badversion-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, $$"""
                {
                  "version": {{versionJson}},
                  "entries": []
                }
                """);

            var ex = await Assert.ThrowsAsync<BaselineVersionException>(
                () => BaselineFile.LoadAsync(path));

            Assert.Equal(BaselineFile.CurrentVersion, ex.RequiredVersion);
            Assert.Contains("slopwatch init --force", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_RemedyNamesCustomBaselinePath()
    {
        // init writes .slopwatch/baseline.json by default. A project that runs analyze with a
        // custom --baseline path would follow the default instruction, regenerate the wrong file,
        // and hit the same error again. The remedy must name the file that actually failed.
        var directory = Path.Combine(Path.GetTempPath(), $"slopwatch-custom-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "custom-baseline.json");
        try
        {
            await File.WriteAllTextAsync(path, """
                {
                  "version": 1,
                  "entries": []
                }
                """);

            var ex = await Assert.ThrowsAsync<BaselineVersionException>(
                () => BaselineFile.LoadAsync(path));

            Assert.Contains("--baseline", ex.Message);
            Assert.Contains("custom-baseline.json", ex.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_RemedyOmitsBaselineFlagForDefaultPath()
    {
        // The default path keeps the short form, so the common case stays uncluttered.
        var directory = Path.Combine(Path.GetTempPath(), $"slopwatch-default-{Guid.NewGuid():N}");
        var slopwatchDir = Path.Combine(directory, ".slopwatch");
        Directory.CreateDirectory(slopwatchDir);
        var path = Path.Combine(slopwatchDir, "baseline.json");
        try
        {
            await File.WriteAllTextAsync(path, """
                {
                  "version": 1,
                  "entries": []
                }
                """);

            var ex = await Assert.ThrowsAsync<BaselineVersionException>(
                () => BaselineFile.LoadAsync(path));

            Assert.Contains("slopwatch init --force", ex.Message);
            Assert.DoesNotContain("--baseline", ex.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_AcceptsCurrentSchemaVersion()
    {
        var path = Path.Combine(Path.GetTempPath(), $"slopwatch-v2-{Guid.NewGuid():N}.json");
        try
        {
            var original = new BaselineFile();
            original.Entries.Add(new BaselineEntry
            {
                Hash = "abc123",
                RuleId = "SW004",
                FilePath = "tests/Foo.cs",
                LineNumber = 10,
                CodeSnippet = "Task.Delay(100)",
                Message = "current"
            });
            await original.SaveAsync(path);

            var loaded = await BaselineFile.LoadAsync(path);

            Assert.NotNull(loaded);
            Assert.Equal(BaselineFile.CurrentVersion, loaded.Version);
            Assert.Single(loaded.Entries);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SyncWithDetections_PreservesEntriesForSkippedFiles()
    {
        // A generated file that is skipped rather than analyzed must keep its baseline entries,
        // otherwise flipping generatedCode.mode back to "analyze" resurfaces a wall of findings.
        var baseline = new BaselineFile();
        var generatedFile = CreateResult("SW001", "/root/src/Model.g.cs", 10, "generated-snippet");
        var handWrittenFile = CreateResult("SW001", "/root/src/A.cs", 20, "old-a");
        baseline.AddEntry(generatedFile, "/root");
        baseline.AddEntry(handWrittenFile, "/root");

        var currentResults = Array.Empty<DetectionResult>();

        var (removed, added, kept) = baseline.SyncWithDetections(
            currentResults,
            "/root",
            new[] { "/root/src/Model.g.cs", "/root/src/A.cs" },
            new[] { "/root/src/Model.g.cs" });

        Assert.Equal(1, removed);
        Assert.Equal(0, added);
        Assert.Equal(1, kept);
        Assert.Single(baseline.Entries);
        Assert.Contains(baseline.Entries, e => e.FilePath == "src/Model.g.cs");
    }

    [Fact]
    public void SyncWithDetections_WithoutSkippedFilesRemovesGeneratedEntries()
    {
        // Control case: without the skipped-file set, the same scan prunes the entry.
        var baseline = new BaselineFile();
        baseline.AddEntry(CreateResult("SW001", "/root/src/Model.g.cs", 10, "generated-snippet"), "/root");

        var (removed, added, kept) = baseline.SyncWithDetections(
            Array.Empty<DetectionResult>(),
            "/root",
            new[] { "/root/src/Model.g.cs" });

        Assert.Equal(1, removed);
        Assert.Equal(0, added);
        Assert.Equal(0, kept);
        Assert.Empty(baseline.Entries);
    }

    [Fact]
    public void SyncWithDetections_PrunesPartiallyAnalyzedFilesNormally()
    {
        // A file that was analyzed but had individual findings filtered out as generated is
        // pruned normally. Preserving per-finding entries is unsound: the hash cannot distinguish
        // identical snippets, and line numbers are neither stable nor unique. Pruning a live
        // generated entry is noise; shielding a stale hand-written entry is a false negative.
        var baseline = new BaselineFile();
        var generatedFinding = CreateResult("SW003", "/root/src/Mixed.cs", 10, "catch { }");
        var fixedHandWrittenFinding = CreateResult("SW003", "/root/src/Mixed.cs", 40, "catch { throw; }");
        baseline.AddEntry(generatedFinding, "/root");
        baseline.AddEntry(fixedHandWrittenFinding, "/root");

        var (removed, added, kept) = baseline.SyncWithDetections(
            Array.Empty<DetectionResult>(),
            "/root",
            new[] { "/root/src/Mixed.cs" });

        Assert.Equal(2, removed);
        Assert.Equal(0, added);
        Assert.Equal(0, kept);
        Assert.Empty(baseline.Entries);
    }

    [Fact]
    public void SyncWithDetections_ThreeParameterOverloadRemainsCallable()
    {
        // Source compatibility: the two-argument and three-argument forms must still compile.
        var baseline = new BaselineFile();

        var twoArg = baseline.SyncWithDetections(Array.Empty<DetectionResult>(), "/root");
        var threeArg = baseline.SyncWithDetections(Array.Empty<DetectionResult>(), "/root", null);

        Assert.Equal(0, twoArg.removed);
        Assert.Equal(0, threeArg.removed);
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
