using Slopwatch.Analysis;
using Slopwatch.Detection;
using Slopwatch.Detection.Rules;
using Xunit;

namespace Slopwatch.Tests.Detection;

/// <summary>
/// Tests that detection rules work on .razor files by extracting @code blocks.
/// </summary>
public class RazorFileDetectionTests
{
    [Fact]
    public async Task SW003_DetectsEmptyCatchInRazorCodeBlock()
    {
        // Arrange - a .razor file with an empty catch block in @code
        var razorContent = """
            @page "/test"

            <h1>Test</h1>

            @code {
                private string? message;

                protected override void OnInitialized()
                {
                    try
                    {
                        message = "hello";
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            """;

        var rule = new EmptyCatchBlockRule();
        var analyzer = new FileAnalyzer(new IDetectionRule[] { rule });

        // Write to a temp .razor file
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.razor");
        try
        {
            await File.WriteAllTextAsync(tempFile, razorContent);

            // Act
            var results = new List<DetectionResult>();
            await foreach (var result in analyzer.AnalyzeFileAsync(tempFile))
            {
                results.Add(result);
            }

            // Assert
            var detection = Assert.Single(results);
            Assert.Equal("SW003", detection.RuleId);
            Assert.Contains("Empty catch block", detection.Message);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task SW003_DetectsEmptyCatchInCshtmlCodeBlock()
    {
        // Arrange - a .cshtml file with an empty catch block in a Razor code block
        var cshtmlContent = """
            @{
                try
                {
                    var value = 1;
                }
                catch (Exception)
                {
                }
            }
            """;

        var rule = new EmptyCatchBlockRule();
        var analyzer = new FileAnalyzer(new IDetectionRule[] { rule });

        // Write to a temp .cshtml file
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.cshtml");
        try
        {
            await File.WriteAllTextAsync(tempFile, cshtmlContent);

            // Act
            var results = new List<DetectionResult>();
            await foreach (var result in analyzer.AnalyzeFileAsync(tempFile))
            {
                results.Add(result);
            }

            // Assert
            var detection = Assert.Single(results);
            Assert.Equal("SW003", detection.RuleId);
            Assert.Contains("Empty catch block", detection.Message);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task SW003_MapsLineNumberBackToOriginalRazorFile()
    {
        // Arrange - each line is explicitly numbered so we can assert the mapped line.
        // The catch keyword must land on a known line in the .razor source.
        var razorContent =
            "@page \"/test\"\n" +       // line 1
            "\n" +                       // line 2
            "<h1>Test</h1>\n" +          // line 3
            "\n" +                       // line 4
            "@code {\n" +                // line 5
            "    private string? msg;\n" + // line 6
            "\n" +                       // line 7
            "    protected override void OnInitialized()\n" + // line 8
            "    {\n" +                  // line 9
            "        try\n" +            // line 10
            "        {\n" +              // line 11
            "            msg = \"hi\";\n" + // line 12
            "        }\n" +              // line 13
            "        catch (Exception)\n" + // line 14  <-- expected
            "        {\n" +              // line 15
            "        }\n" +              // line 16
            "    }\n" +                  // line 17
            "}\n";                       // line 18

        var rule = new EmptyCatchBlockRule();
        var analyzer = new FileAnalyzer(new IDetectionRule[] { rule });

        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.razor");
        try
        {
            await File.WriteAllTextAsync(tempFile, razorContent);

            // Act
            var results = new List<DetectionResult>();
            await foreach (var result in analyzer.AnalyzeFileAsync(tempFile))
            {
                results.Add(result);
            }

            // Assert - line number must map back to line 14 in the original .razor file
            var detection = Assert.Single(results);
            Assert.Equal("SW003", detection.RuleId);
            Assert.Equal(14, detection.LineNumber);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task SW003_IgnoresProperHandlingInRazorCodeBlock()
    {
        // Arrange - a .razor file with proper exception handling in @code
        var razorContent = """
            @page "/test"

            <h1>Test</h1>

            @code {
                private string? message;

                protected override void OnInitialized()
                {
                    try
                    {
                        message = "hello";
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                }
            }
            """;

        var rule = new EmptyCatchBlockRule();
        var analyzer = new FileAnalyzer(new IDetectionRule[] { rule });

        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.razor");
        try
        {
            await File.WriteAllTextAsync(tempFile, razorContent);

            // Act
            var results = new List<DetectionResult>();
            await foreach (var result in analyzer.AnalyzeFileAsync(tempFile))
            {
                results.Add(result);
            }

            // Assert - proper handling should not be flagged
            Assert.Empty(results);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task SW003_IgnoresRazorMarkupOutsideCodeBlock()
    {
        // Arrange - a .razor file with no @code block
        var razorContent = """
            @page "/test"

            <h1>Test</h1>
            <p>No code block here</p>
            """;

        var rule = new EmptyCatchBlockRule();
        var analyzer = new FileAnalyzer(new IDetectionRule[] { rule });

        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.razor");
        try
        {
            await File.WriteAllTextAsync(tempFile, razorContent);

            // Act
            var results = new List<DetectionResult>();
            await foreach (var result in analyzer.AnalyzeFileAsync(tempFile))
            {
                results.Add(result);
            }

            // Assert - no code block means nothing to detect
            Assert.Empty(results);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
