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
/// <param name="Identity">
/// Optional baseline identity for this detection. When null, <paramref name="CodeSnippet"/> is
/// used as the identity.
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
    /// Optional baseline identity for this detection. When null, <see cref="CodeSnippet"/> is
    /// used as the identity.
    /// </summary>
    /// <remarks>
    /// This is an init-only property rather than a constructor parameter on purpose. Adding a
    /// positional parameter to a record replaces the generated constructor and <c>Deconstruct</c>,
    /// which breaks binary compatibility for any consumer compiled against the previous shape.
    /// </remarks>
    public string? Identity { get; init; }
    /// <summary>
    /// The string used as input to the baseline hash.
    /// </summary>
    /// <remarks>
    /// <see cref="CodeSnippet"/> serves double duty as display text and hash input, which forces
    /// a bad trade: readable snippets truncate (and therefore collide), while precise snippets
    /// are unreadable. <see cref="Identity"/> separates the two. Rules that build a structural
    /// identity set it here and keep <see cref="CodeSnippet"/> for humans.
    /// </remarks>
    public string BaselineIdentity => Identity ?? CodeSnippet ?? string.Empty;

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
