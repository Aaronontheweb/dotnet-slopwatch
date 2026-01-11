namespace Slopwatch.Detection;

/// <summary>
/// Defines the contract for a detection rule that analyzes code for specific patterns.
/// </summary>
/// <remarks>
/// Detection rules are the core extensibility point for slopwatch. Each rule is responsible
/// for identifying a specific category of potentially LLM-generated or "sloppy" code patterns.
/// Rules should be designed to be stateless and thread-safe for concurrent analysis.
/// </remarks>
public interface IDetectionRule
{
    /// <summary>
    /// Gets the unique identifier for this rule (e.g., "SW001").
    /// </summary>
    /// <remarks>
    /// Rule IDs should follow the format "SW###" where ### is a zero-padded number.
    /// This ID is used for configuration, suppression, and reporting purposes.
    /// </remarks>
    string RuleId { get; }

    /// <summary>
    /// Gets the human-readable name of this rule.
    /// </summary>
    /// <remarks>
    /// The name should be concise but descriptive (e.g., "Excessive Comment Detection").
    /// </remarks>
    string Name { get; }

    /// <summary>
    /// Gets a detailed description of what this rule detects and why it matters.
    /// </summary>
    /// <remarks>
    /// The description should explain the pattern being detected and provide
    /// context for why it may indicate LLM-generated or problematic code.
    /// </remarks>
    string Description { get; }

    /// <summary>
    /// Gets the default severity level for findings from this rule.
    /// </summary>
    /// <remarks>
    /// This can be overridden through configuration, but provides a sensible default.
    /// </remarks>
    DetectionSeverity DefaultSeverity { get; }

    /// <summary>
    /// Gets the file patterns this rule applies to.
    /// </summary>
    /// <remarks>
    /// Uses glob-style patterns (e.g., "*.cs", "*.csproj", "**/*.razor").
    /// Only files matching at least one pattern will be passed to this rule.
    /// </remarks>
    IReadOnlyList<string> ApplicableFilePatterns { get; }

    /// <summary>
    /// Analyzes the provided context and yields any detected issues.
    /// </summary>
    /// <param name="context">
    /// The detection context containing file information and optional diff scope.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to monitor for cancellation requests.
    /// </param>
    /// <returns>
    /// An async enumerable of detection results. May be empty if no issues are found.
    /// </returns>
    /// <remarks>
    /// Implementations should be designed to yield results as they are found
    /// to support streaming output for large files or analyses.
    /// When <see cref="DetectionContext.HasDiffContext"/> is true, implementations
    /// should consider focusing on lines indicated by <see cref="DetectionContext.IsLineInScope"/>.
    /// </remarks>
    IAsyncEnumerable<DetectionResult> AnalyzeAsync(
        DetectionContext context,
        CancellationToken cancellationToken = default);
}
