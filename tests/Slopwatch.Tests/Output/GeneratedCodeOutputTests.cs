using Slopwatch.Cmd.Output;
using Slopwatch.Detection;
using Xunit;

namespace Slopwatch.Tests.Output;

/// <summary>
/// Tests that output formatters surface the generated-code marker.
/// </summary>
public class GeneratedCodeOutputTests
{
    [Fact]
    public async Task Test_JsonOutputIncludesIsGeneratedCode()
    {
        var result = CreateResult(isGeneratedCode: true);
        var json = await FormatJsonAsync(result);

        Assert.Contains("\"isGeneratedCode\": true", json);
    }

    [Fact]
    public async Task Test_JsonOutputMarksOrdinaryFindingsAsNotGenerated()
    {
        var result = CreateResult(isGeneratedCode: false);
        var json = await FormatJsonAsync(result);

        Assert.Contains("\"isGeneratedCode\": false", json);
    }

    [Fact]
    public async Task Test_ConsoleOutputMarksGeneratedFindings()
    {
        var result = CreateResult(isGeneratedCode: true);
        var output = await FormatConsoleAsync(result);

        Assert.Contains("[generated]", output);
    }

    [Fact]
    public async Task Test_ConsoleOutputDoesNotMarkOrdinaryFindings()
    {
        var result = CreateResult(isGeneratedCode: false);
        var output = await FormatConsoleAsync(result);

        Assert.DoesNotContain("[generated]", output);
    }

    private static DetectionResult CreateResult(bool isGeneratedCode) => new(
        RuleId: "SW001",
        RuleName: "Disabled Test",
        Severity: DetectionSeverity.Error,
        FilePath: "src/Model.g.cs",
        LineNumber: 3,
        Column: 19,
        Message: "Test method 'T' is disabled",
        CodeSnippet: "Fact(Skip = \"x\")")
    {
        IsGeneratedCode = isGeneratedCode
    };

    private static async Task<string> FormatJsonAsync(DetectionResult result)
    {
        var formatter = new JsonOutputFormatter();
        using var writer = new StringWriter();

        await formatter.FormatAsync(ToAsyncEnumerable(result), writer);

        return writer.ToString();
    }

    private static async Task<string> FormatConsoleAsync(DetectionResult result)
    {
        var formatter = new ConsoleOutputFormatter(useColors: false);
        using var writer = new StringWriter();

        await formatter.FormatAsync(ToAsyncEnumerable(result), writer);

        return writer.ToString();
    }

    private static async IAsyncEnumerable<DetectionResult> ToAsyncEnumerable(DetectionResult result)
    {
        yield return result;
        await Task.CompletedTask;
    }
}
