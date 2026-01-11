using System.Text.Json.Serialization;

namespace Slopwatch.Configuration;

/// <summary>
/// Root configuration for Slopwatch suppressions and rules.
/// </summary>
/// <remarks>
/// This configuration file allows project-level suppression management through
/// a .slopwatch/config.json file in the repository root.
/// </remarks>
public sealed class SlopwatchConfig
{
    /// <summary>
    /// Gets or sets the list of path-based rule suppressions.
    /// </summary>
    [JsonPropertyName("suppressions")]
    public List<PathSuppression> Suppressions { get; set; } = new();

    /// <summary>
    /// Gets or sets the list of global rule suppressions (apply to entire project).
    /// </summary>
    [JsonPropertyName("globalSuppressions")]
    public List<GlobalSuppression> GlobalSuppressions { get; set; } = new();
}

/// <summary>
/// Represents a path-based suppression that applies to files matching a glob pattern.
/// </summary>
public sealed class PathSuppression
{
    /// <summary>
    /// Gets or sets the rule ID to suppress (e.g., "SW002").
    /// </summary>
    [JsonPropertyName("ruleId")]
    public string RuleId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the glob pattern for files this suppression applies to.
    /// </summary>
    /// <remarks>
    /// Examples:
    /// <list type="bullet">
    /// <item><description>"**/Generated/**" - All files in Generated directories</description></item>
    /// <item><description>"src/Legacy/**/*.cs" - All C# files in Legacy directory</description></item>
    /// <item><description>"**/*.Designer.cs" - All designer-generated files</description></item>
    /// </list>
    /// </remarks>
    [JsonPropertyName("pattern")]
    public string Pattern { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the justification for this suppression.
    /// </summary>
    /// <remarks>
    /// Like <see cref="Suppression.SlopwatchSuppressAttribute"/>, this must be at least 20 characters
    /// and provide meaningful context. Generic phrases like "false positive" are discouraged.
    /// </remarks>
    [JsonPropertyName("justification")]
    public string Justification { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional issue URL tracking this suppression.
    /// </summary>
    [JsonPropertyName("issueUrl")]
    public string? IssueUrl { get; set; }

    /// <summary>
    /// Gets or sets the optional expiration date for this suppression.
    /// </summary>
    /// <remarks>
    /// If set, the suppression will be flagged as expired after this date.
    /// Format: "yyyy-MM-dd" (e.g., "2026-06-01")
    /// </remarks>
    [JsonPropertyName("expiresAt")]
    public string? ExpiresAt { get; set; }

    /// <summary>
    /// Gets or sets the optional reviewer who approved this suppression.
    /// </summary>
    [JsonPropertyName("reviewer")]
    public string? Reviewer { get; set; }

    /// <summary>
    /// Checks if this suppression has expired.
    /// </summary>
    /// <returns>True if the suppression has an expiration date and it has passed.</returns>
    public bool IsExpired()
    {
        if (string.IsNullOrWhiteSpace(ExpiresAt))
            return false;

        if (DateOnly.TryParse(ExpiresAt, out var expirationDate))
        {
            return DateOnly.FromDateTime(DateTime.UtcNow) > expirationDate;
        }

        return false;
    }
}

/// <summary>
/// Represents a global suppression that applies to all files in the project.
/// </summary>
public sealed class GlobalSuppression
{
    /// <summary>
    /// Gets or sets the rule ID to suppress (e.g., "SW004").
    /// </summary>
    [JsonPropertyName("ruleId")]
    public string RuleId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the justification for this global suppression.
    /// </summary>
    /// <remarks>
    /// Must be at least 20 characters and provide meaningful context.
    /// Global suppressions should be rare and well-justified.
    /// </remarks>
    [JsonPropertyName("justification")]
    public string Justification { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional issue URL tracking this suppression.
    /// </summary>
    [JsonPropertyName("issueUrl")]
    public string? IssueUrl { get; set; }

    /// <summary>
    /// Gets or sets the optional expiration date for this suppression.
    /// </summary>
    /// <remarks>
    /// If set, the suppression will be flagged as expired after this date.
    /// Format: "yyyy-MM-dd" (e.g., "2026-06-01")
    /// </remarks>
    [JsonPropertyName("expiresAt")]
    public string? ExpiresAt { get; set; }

    /// <summary>
    /// Gets or sets the optional reviewer who approved this suppression.
    /// </summary>
    [JsonPropertyName("reviewer")]
    public string? Reviewer { get; set; }

    /// <summary>
    /// Checks if this suppression has expired.
    /// </summary>
    /// <returns>True if the suppression has an expiration date and it has passed.</returns>
    public bool IsExpired()
    {
        if (string.IsNullOrWhiteSpace(ExpiresAt))
            return false;

        if (DateOnly.TryParse(ExpiresAt, out var expirationDate))
        {
            return DateOnly.FromDateTime(DateTime.UtcNow) > expirationDate;
        }

        return false;
    }
}
