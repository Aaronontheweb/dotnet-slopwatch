using Slopwatch.Detection;

namespace Slopwatch.Analysis;

/// <summary>
/// Configuration options for file analysis.
/// </summary>
public sealed class AnalysisOptions
{
    /// <summary>
    /// Gets or sets the minimum severity level for reporting detections.
    /// </summary>
    /// <remarks>
    /// Detections with a severity level lower than this will be filtered out.
    /// Default is <see cref="DetectionSeverity.Warning"/>.
    /// </remarks>
    public DetectionSeverity MinimumSeverity { get; set; } = DetectionSeverity.Warning;

    /// <summary>
    /// Gets or sets the set of rule IDs that are explicitly enabled.
    /// </summary>
    /// <remarks>
    /// If null or empty, all rules are enabled (except those in <see cref="DisabledRuleIds"/>).
    /// If specified, only rules in this set will be executed.
    /// </remarks>
    public IReadOnlySet<string>? EnabledRuleIds { get; set; }

    /// <summary>
    /// Gets or sets the set of rule IDs that should be disabled.
    /// </summary>
    /// <remarks>
    /// Rules in this set will not be executed, even if they appear in <see cref="EnabledRuleIds"/>.
    /// This takes precedence over <see cref="EnabledRuleIds"/>.
    /// </remarks>
    public IReadOnlySet<string>? DisabledRuleIds { get; set; }

    /// <summary>
    /// Gets or sets the file patterns to exclude from analysis.
    /// </summary>
    /// <remarks>
    /// Uses glob-style patterns (e.g., "**/obj/**", "**/bin/**", "*.Designer.cs").
    /// Files matching any of these patterns will be skipped during analysis.
    /// Default patterns exclude common build artifacts and generated files.
    /// </remarks>
    public IReadOnlyList<string> ExcludePatterns { get; set; } = new[]
    {
        "**/obj/**",
        "**/bin/**",
        "**/.vs/**",
        "**/.git/**",
        "**/*.Designer.cs",
        "**/*.generated.cs",
        "**/*.g.cs",
        "**/*.g.i.cs"
    };

    /// <summary>
    /// Gets or sets the patterns used to identify test files.
    /// </summary>
    /// <remarks>
    /// Files matching any of these patterns will be marked as test files,
    /// which may affect how certain rules analyze them.
    /// Default patterns cover common .NET test project structures.
    /// </remarks>
    public IReadOnlyList<string> TestFilePatterns { get; set; } = new[]
    {
        "**/test/**",
        "**/tests/**",
        "**/*.test.cs",
        "**/*.tests.cs",
        "**/*.spec.cs",
        "**/test*.cs",
        "**/*test.cs",
        "**/*tests.cs"
    };

    /// <summary>
    /// Determines if a rule should be executed based on the enabled/disabled configuration.
    /// </summary>
    /// <param name="ruleId">The rule ID to check.</param>
    /// <returns>True if the rule should be executed; otherwise, false.</returns>
    public bool IsRuleEnabled(string ruleId)
    {
        // Disabled rules take precedence
        if (DisabledRuleIds?.Contains(ruleId) ?? false)
            return false;

        // If no enabled list is specified, all non-disabled rules are enabled
        if (EnabledRuleIds is null || EnabledRuleIds.Count == 0)
            return true;

        // Otherwise, only rules in the enabled list are executed
        return EnabledRuleIds.Contains(ruleId);
    }
}
