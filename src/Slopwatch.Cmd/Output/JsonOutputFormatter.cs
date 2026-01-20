using System.Text.Encodings.Web;
using System.Text.Json;
using Slopwatch.Detection;

namespace Slopwatch.Cmd.Output;

/// <summary>
/// Formats detection results as JSON.
/// </summary>
public sealed class JsonOutputFormatter : IOutputFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <inheritdoc />
    public async Task FormatAsync(IAsyncEnumerable<DetectionResult> results, TextWriter writer, CancellationToken cancellationToken = default)
    {
        var resultsList = new List<DetectionResult>();

        await foreach (var result in results.WithCancellation(cancellationToken))
        {
            resultsList.Add(result);
        }

        var output = new
        {
            version = "1.0",
            timestamp = DateTime.UtcNow,
            totalIssues = resultsList.Count,
            summary = new
            {
                errors = resultsList.Count(r => r.Severity == DetectionSeverity.Error),
                warnings = resultsList.Count(r => r.Severity == DetectionSeverity.Warning),
                info = resultsList.Count(r => r.Severity == DetectionSeverity.Info)
            },
            results = resultsList.Select(r => new
            {
                ruleId = r.RuleId,
                ruleName = r.RuleName,
                severity = r.Severity.ToString().ToLowerInvariant(),
                filePath = r.FilePath,
                lineNumber = r.LineNumber,
                column = r.Column,
                message = r.Message,
                codeSnippet = r.CodeSnippet,
                suggestedFix = r.SuggestedFix
            })
        };

        var json = JsonSerializer.Serialize(output, JsonOptions);
        await writer.WriteLineAsync(json);
    }
}
