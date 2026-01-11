using Slopwatch.Detection;

namespace Slopwatch.Cmd.Output;

/// <summary>
/// Formats detection results for console output with color support.
/// </summary>
public sealed class ConsoleOutputFormatter : IOutputFormatter
{
    private readonly bool _useColors;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConsoleOutputFormatter"/> class.
    /// </summary>
    /// <param name="useColors">Whether to use ANSI colors in output.</param>
    public ConsoleOutputFormatter(bool useColors = true)
    {
        _useColors = useColors && !Console.IsOutputRedirected;
    }

    /// <inheritdoc />
    public async Task FormatAsync(IAsyncEnumerable<DetectionResult> results, TextWriter writer, CancellationToken cancellationToken = default)
    {
        var count = 0;
        var severityCounts = new Dictionary<DetectionSeverity, int>
        {
            { DetectionSeverity.Info, 0 },
            { DetectionSeverity.Warning, 0 },
            { DetectionSeverity.Error, 0 }
        };

        await foreach (var result in results.WithCancellation(cancellationToken))
        {
            count++;
            severityCounts[result.Severity]++;

            await FormatResultAsync(result, writer, cancellationToken);
        }

        // Print summary
        await writer.WriteLineAsync();
        await writer.WriteLineAsync($"Scan complete: {count} issue(s) found");

        if (severityCounts[DetectionSeverity.Error] > 0)
        {
            await WriteColoredAsync(writer, $"  Errors: {severityCounts[DetectionSeverity.Error]}", ConsoleColor.Red);
        }
        if (severityCounts[DetectionSeverity.Warning] > 0)
        {
            await WriteColoredAsync(writer, $"  Warnings: {severityCounts[DetectionSeverity.Warning]}", ConsoleColor.Yellow);
        }
        if (severityCounts[DetectionSeverity.Info] > 0)
        {
            await WriteColoredAsync(writer, $"  Info: {severityCounts[DetectionSeverity.Info]}", ConsoleColor.Cyan);
        }
    }

    private async Task FormatResultAsync(DetectionResult result, TextWriter writer, CancellationToken cancellationToken)
    {
        // Format: file(line,col): severity ruleId: message
        var location = $"{result.FilePath}({result.LineNumber},{result.Column})";
        var severityText = result.Severity switch
        {
            DetectionSeverity.Info => "info",
            DetectionSeverity.Warning => "warning",
            DetectionSeverity.Error => "error",
            _ => "unknown"
        };

        // Write location
        await writer.WriteAsync(location);
        await writer.WriteAsync(": ");

        // Write severity with color
        var severityColor = result.Severity switch
        {
            DetectionSeverity.Error => ConsoleColor.Red,
            DetectionSeverity.Warning => ConsoleColor.Yellow,
            DetectionSeverity.Info => ConsoleColor.Cyan,
            _ => ConsoleColor.White
        };

        await WriteColoredAsync(writer, severityText, severityColor, newLine: false);
        await writer.WriteAsync(" ");

        // Write rule ID in gray
        await WriteColoredAsync(writer, result.RuleId, ConsoleColor.DarkGray, newLine: false);
        await writer.WriteAsync(": ");

        // Write message
        await writer.WriteLineAsync(result.Message);

        // Write code snippet if available
        if (!string.IsNullOrWhiteSpace(result.CodeSnippet))
        {
            await WriteColoredAsync(writer, $"    Code: {result.CodeSnippet}", ConsoleColor.DarkGray);
        }

        // Write suggested fix if available
        if (!string.IsNullOrWhiteSpace(result.SuggestedFix))
        {
            await WriteColoredAsync(writer, $"    Fix: {result.SuggestedFix}", ConsoleColor.Green);
        }

        await writer.WriteLineAsync();
    }

    private async Task WriteColoredAsync(TextWriter writer, string text, ConsoleColor color, bool newLine = true)
    {
        if (_useColors && writer == Console.Out)
        {
            var previousColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            if (newLine)
            {
                await writer.WriteLineAsync(text);
            }
            else
            {
                await writer.WriteAsync(text);
            }
            Console.ForegroundColor = previousColor;
        }
        else
        {
            if (newLine)
            {
                await writer.WriteLineAsync(text);
            }
            else
            {
                await writer.WriteAsync(text);
            }
        }
    }
}
