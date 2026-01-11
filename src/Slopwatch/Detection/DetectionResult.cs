namespace Slopwatch.Detection;

/// <summary>
/// Represents a single detection result from a rule analysis.
/// </summary>
/// <param name="RuleId">
/// The unique identifier for the rule that produced this result (e.g., "SW001").
/// </param>
/// <param name="RuleName">
/// The human-readable name of the rule that produced this result.
/// </param>
/// <param name="Severity">
/// The severity level of this detection.
/// </param>
/// <param name="FilePath">
/// The full path to the file where the issue was detected.
/// </param>
/// <param name="LineNumber">
/// The 1-based line number where the issue was detected.
/// </param>
/// <param name="Column">
/// The 1-based column number where the issue starts.
/// </param>
/// <param name="Message">
/// A descriptive message explaining the detected issue.
/// </param>
/// <param name="CodeSnippet">
/// Optional code snippet showing the problematic code for context.
/// </param>
/// <param name="SuggestedFix">
/// Optional suggestion for how to fix the detected issue.
/// </param>
public sealed record DetectionResult(
    string RuleId,
    string RuleName,
    DetectionSeverity Severity,
    string FilePath,
    int LineNumber,
    int Column,
    string Message,
    string? CodeSnippet = null,
    string? SuggestedFix = null
)
{
    /// <summary>
    /// Returns a formatted string representation suitable for console output.
    /// </summary>
    /// <returns>A formatted detection message.</returns>
    public string ToFormattedString()
    {
        var severityText = Severity switch
        {
            DetectionSeverity.Info => "info",
            DetectionSeverity.Warning => "warning",
            DetectionSeverity.Error => "error",
            _ => "unknown"
        };

        return $"{FilePath}({LineNumber},{Column}): {severityText} {RuleId}: {Message}";
    }
}
