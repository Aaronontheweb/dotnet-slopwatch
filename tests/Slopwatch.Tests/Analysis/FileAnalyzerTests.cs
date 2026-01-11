using Slopwatch.Analysis;
using Slopwatch.Detection;
using Slopwatch.Detection.Rules;
using Xunit;

namespace Slopwatch.Tests.Analysis;

/// <summary>
/// Tests for FileAnalyzer
/// </summary>
public class FileAnalyzerTests
{
    [Fact]
    public async Task Test_AnalyzesSingleFile()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "TestFile.cs");
        var testCode = @"
using Xunit;
public class TestClass
{
    [Fact(Skip = ""Broken test"")]
    public void BrokenTest() { }
}";
        await File.WriteAllTextAsync(tempFile, testCode);

        try
        {
            var rules = new IDetectionRule[] { new DisabledTestRule() };
            var options = new AnalysisOptions
            {
                TestFilePatterns = new[] { "**/TestFile.cs" } // Match our specific test file
            };
            var analyzer = new FileAnalyzer(rules, options);

            // Act
            var results = new List<DetectionResult>();
            await foreach (var r in analyzer.AnalyzeFileAsync(tempFile))
            {
                results.Add(r);
            }

            // Assert
            var result = Assert.Single(results);
            Assert.Equal("SW001", result.RuleId);
            Assert.Contains("BrokenTest", result.Message);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Test_AnalyzesMultipleFiles()
    {
        // Arrange
        var tempFile1 = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.cs");
        var tempFile2 = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.cs");

        var testCode1 = @"
using Xunit;
public class TestClass1
{
    [Fact(Skip = ""Test 1"")]
    public void Test1() { }
}";
        var testCode2 = @"
using Xunit;
public class TestClass2
{
    [Fact(Skip = ""Test 2"")]
    public void Test2() { }
}";
        await File.WriteAllTextAsync(tempFile1, testCode1);
        await File.WriteAllTextAsync(tempFile2, testCode2);

        try
        {
            var rules = new IDetectionRule[] { new DisabledTestRule() };
            var options = new AnalysisOptions
            {
                TestFilePatterns = new[] { "**/*.cs" }
            };
            var analyzer = new FileAnalyzer(rules, options);

            // Act
            var results = new List<DetectionResult>();
            await foreach (var r in analyzer.AnalyzeFilesAsync(new[] { tempFile1, tempFile2 }))
            {
                results.Add(r);
            }

            // Assert
            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.Equal("SW001", r.RuleId));
            Assert.Contains(results, r => r.Message.Contains("Test1"));
            Assert.Contains(results, r => r.Message.Contains("Test2"));
        }
        finally
        {
            File.Delete(tempFile1);
            File.Delete(tempFile2);
        }
    }

    [Fact]
    public async Task Test_RespectsMinimumSeverity()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.cs");
        var testCode = @"
#pragma warning disable CS8600
public class TestClass { }";
        await File.WriteAllTextAsync(tempFile, testCode);

        try
        {
            var rules = new IDetectionRule[] { new WarningSuppressRule() };
            var options = new AnalysisOptions
            {
                MinimumSeverity = DetectionSeverity.Error // WarningSuppressRule has Warning severity
            };
            var analyzer = new FileAnalyzer(rules, options);

            // Act
            var results = new List<DetectionResult>();
            await foreach (var r in analyzer.AnalyzeFileAsync(tempFile))
            {
                results.Add(r);
            }

            // Assert
            Assert.Empty(results); // Should be filtered out due to severity
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Test_RespectsMinimumSeverity_AllowsHigherSeverity()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.cs");
        var testCode = @"
using Xunit;
public class TestClass
{
    [Fact(Skip = ""Test"")]
    public void Test() { }
}";
        await File.WriteAllTextAsync(tempFile, testCode);

        try
        {
            var rules = new IDetectionRule[] { new DisabledTestRule() };
            var options = new AnalysisOptions
            {
                MinimumSeverity = DetectionSeverity.Warning, // DisabledTestRule has Error severity
                TestFilePatterns = new[] { "**/*.cs" }
            };
            var analyzer = new FileAnalyzer(rules, options);

            // Act
            var results = new List<DetectionResult>();
            await foreach (var r in analyzer.AnalyzeFileAsync(tempFile))
            {
                results.Add(r);
            }

            // Assert
            Assert.Single(results); // Should be included because Error >= Warning
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Test_RespectsExcludePatterns()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        var objDir = Path.Combine(tempDir, "obj");
        Directory.CreateDirectory(objDir);

        var regularFile = Path.Combine(tempDir, "Test.cs");
        var excludedFile = Path.Combine(objDir, "Test.cs");

        var testCode = @"
using Xunit;
public class TestClass
{
    [Fact(Skip = ""Test"")]
    public void Test() { }
}";
        await File.WriteAllTextAsync(regularFile, testCode);
        await File.WriteAllTextAsync(excludedFile, testCode);

        try
        {
            var rules = new IDetectionRule[] { new DisabledTestRule() };
            var options = new AnalysisOptions
            {
                TestFilePatterns = new[] { "**/*.cs" },
                ExcludePatterns = new[] { "**/obj/**" } // Exclude obj directory
            };
            var analyzer = new FileAnalyzer(rules, options);

            // Act
            var results = new List<DetectionResult>();
            await foreach (var r in analyzer.AnalyzeDirectoryAsync(tempDir, new[] { "**/*.cs" }))
            {
                results.Add(r);
            }

            // Assert
            var result = Assert.Single(results);
            Assert.Contains(regularFile, result.FilePath);
            Assert.DoesNotContain("obj", result.FilePath);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Test_RespectsEnabledRuleIds()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.cs");
        var testCode = @"
using Xunit;
public class TestClass
{
    [Fact(Skip = ""Test"")]
    public void Test() { }
}";
        await File.WriteAllTextAsync(tempFile, testCode);

        try
        {
            var rules = new IDetectionRule[]
            {
                new DisabledTestRule(),
                new WarningSuppressRule()
            };
            var options = new AnalysisOptions
            {
                EnabledRuleIds = new HashSet<string> { "SW002" }, // Only enable WarningSuppressRule
                TestFilePatterns = new[] { "**/*.cs" }
            };
            var analyzer = new FileAnalyzer(rules, options);

            // Act
            var results = new List<DetectionResult>();
            await foreach (var r in analyzer.AnalyzeFileAsync(tempFile))
            {
                results.Add(r);
            }

            // Assert
            Assert.Empty(results); // SW001 should not run
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Test_RespectsDisabledRuleIds()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.cs");
        var testCode = @"
using Xunit;
public class TestClass
{
    [Fact(Skip = ""Test"")]
    public void Test() { }
}";
        await File.WriteAllTextAsync(tempFile, testCode);

        try
        {
            var rules = new IDetectionRule[] { new DisabledTestRule() };
            var options = new AnalysisOptions
            {
                DisabledRuleIds = new HashSet<string> { "SW001" }, // Disable DisabledTestRule
                TestFilePatterns = new[] { "**/*.cs" }
            };
            var analyzer = new FileAnalyzer(rules, options);

            // Act
            var results = new List<DetectionResult>();
            await foreach (var r in analyzer.AnalyzeFileAsync(tempFile))
            {
                results.Add(r);
            }

            // Assert
            Assert.Empty(results);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Test_AnalyzesDirectoryWithPatterns()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        var csFile = Path.Combine(tempDir, "Test.cs");
        var txtFile = Path.Combine(tempDir, "Test.txt");

        var testCode = @"
using Xunit;
public class TestClass
{
    [Fact(Skip = ""Test"")]
    public void Test() { }
}";
        await File.WriteAllTextAsync(csFile, testCode);
        await File.WriteAllTextAsync(txtFile, "Not a C# file");

        try
        {
            var rules = new IDetectionRule[] { new DisabledTestRule() };
            var options = new AnalysisOptions
            {
                TestFilePatterns = new[] { "**/*.cs" }
            };
            var analyzer = new FileAnalyzer(rules, options);

            // Act
            var results = new List<DetectionResult>();
            await foreach (var r in analyzer.AnalyzeDirectoryAsync(tempDir, new[] { "**/*.cs" }))
            {
                results.Add(r);
            }

            // Assert
            var result = Assert.Single(results);
            Assert.Contains(".cs", result.FilePath);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Test_HandlesNonExistentFile()
    {
        // Arrange
        var nonExistentFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".cs");
        var rules = new IDetectionRule[] { new DisabledTestRule() };
        var analyzer = new FileAnalyzer(rules);

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(async () =>
        {
            await foreach (var r in analyzer.AnalyzeFileAsync(nonExistentFile))
            {
                // Should not reach here
            }
        });
    }

    [Fact]
    public async Task Test_HandlesNonExistentDirectory()
    {
        // Arrange
        var nonExistentDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var rules = new IDetectionRule[] { new DisabledTestRule() };
        var analyzer = new FileAnalyzer(rules);

        // Act & Assert
        await Assert.ThrowsAsync<DirectoryNotFoundException>(async () =>
        {
            await foreach (var r in analyzer.AnalyzeDirectoryAsync(nonExistentDir))
            {
                // Should not reach here
            }
        });
    }

    [Fact]
    public async Task Test_RunsMultipleRules()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.cs");
        var testCode = @"
using Xunit;
public class TestClass
{
    [Fact(Skip = ""Test"")]
    public void Test()
    {
        try { }
        catch (Exception) { }
    }
}";
        await File.WriteAllTextAsync(tempFile, testCode);

        try
        {
            var rules = new IDetectionRule[]
            {
                new DisabledTestRule(),
                new EmptyCatchBlockRule()
            };
            var options = new AnalysisOptions
            {
                TestFilePatterns = new[] { "**/*.cs" }
            };
            var analyzer = new FileAnalyzer(rules, options);

            // Act
            var results = new List<DetectionResult>();
            await foreach (var r in analyzer.AnalyzeFileAsync(tempFile))
            {
                results.Add(r);
            }

            // Assert
            Assert.Equal(2, results.Count);
            Assert.Contains(results, r => r.RuleId == "SW001");
            Assert.Contains(results, r => r.RuleId == "SW003");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Test_IdentifiesTestFiles()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var testDir = Path.Combine(tempDir, "tests");
        Directory.CreateDirectory(testDir);

        var testFile = Path.Combine(testDir, "Test.cs");
        var testCode = @"
using System.Threading.Tasks;
using Xunit;
public class TestClass
{
    [Fact]
    public async Task Test()
    {
        await Task.Delay(100);
    }
}";
        await File.WriteAllTextAsync(testFile, testCode);

        try
        {
            var rules = new IDetectionRule[] { new TimeoutJigglingRule() };
            var options = new AnalysisOptions
            {
                TestFilePatterns = new[] { "**/tests/**" }
            };
            var analyzer = new FileAnalyzer(rules, options);

            // Act
            var results = new List<DetectionResult>();
            await foreach (var r in analyzer.AnalyzeDirectoryAsync(tempDir))
            {
                results.Add(r);
            }

            // Assert
            var result = Assert.Single(results);
            Assert.Equal("SW004", result.RuleId);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Test_ThrowsOnNullRules()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new FileAnalyzer(null!));
    }

    [Fact]
    public async Task Test_ThrowsOnNullFilePath()
    {
        // Arrange
        var rules = new IDetectionRule[] { new DisabledTestRule() };
        var analyzer = new FileAnalyzer(rules);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await foreach (var r in analyzer.AnalyzeFileAsync(null!))
            {
                // Should not reach here
            }
        });
    }

    [Fact]
    public async Task Test_ThrowsOnEmptyFilePath()
    {
        // Arrange
        var rules = new IDetectionRule[] { new DisabledTestRule() };
        var analyzer = new FileAnalyzer(rules);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await foreach (var r in analyzer.AnalyzeFileAsync(""))
            {
                // Should not reach here
            }
        });
    }

    [Fact]
    public async Task Test_HandlesEmptyDirectory()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var rules = new IDetectionRule[] { new DisabledTestRule() };
            var analyzer = new FileAnalyzer(rules);

            // Act
            var results = new List<DetectionResult>();
            await foreach (var r in analyzer.AnalyzeDirectoryAsync(tempDir))
            {
                results.Add(r);
            }

            // Assert
            Assert.Empty(results);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Test_UsesDefaultOptionsWhenNoneProvided()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.cs");
        var testCode = @"
#pragma warning disable CS8600
public class TestClass { }";
        await File.WriteAllTextAsync(tempFile, testCode);

        try
        {
            var rules = new IDetectionRule[] { new WarningSuppressRule() };
            var analyzer = new FileAnalyzer(rules); // No options provided

            // Act
            var results = new List<DetectionResult>();
            await foreach (var r in analyzer.AnalyzeFileAsync(tempFile))
            {
                results.Add(r);
            }

            // Assert - Default MinimumSeverity is Warning
            var result = Assert.Single(results);
            Assert.Equal("SW002", result.RuleId);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
