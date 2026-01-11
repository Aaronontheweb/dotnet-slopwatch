// -----------------------------------------------------------------------
// <copyright file="BaselineEntry.cs" company="Aaron Stannard">
//     Copyright (C) 2025 - 2025 Aaron Stannard
// </copyright>
// -----------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Slopwatch.Baseline;

/// <summary>
/// Represents a single detection that has been baselined (acknowledged as pre-existing).
/// </summary>
public sealed class BaselineEntry
{
    /// <summary>
    /// Unique hash identifying this detection.
    /// Computed from: RuleId + RelativeFilePath + CodeSnippet (normalized).
    /// </summary>
    /// <remarks>
    /// We intentionally exclude line number from the hash so that if code moves
    /// around in the file, it's still recognized as the same baselined detection.
    /// </remarks>
    [JsonPropertyName("hash")]
    public required string Hash { get; init; }

    /// <summary>
    /// The rule ID that produced this detection (e.g., "SW001").
    /// </summary>
    [JsonPropertyName("ruleId")]
    public required string RuleId { get; init; }

    /// <summary>
    /// Relative file path from the repository root.
    /// </summary>
    [JsonPropertyName("filePath")]
    public required string FilePath { get; init; }

    /// <summary>
    /// The original line number when baselined (for reference only, not used in matching).
    /// </summary>
    [JsonPropertyName("lineNumber")]
    public int LineNumber { get; init; }

    /// <summary>
    /// Normalized code snippet that triggered the detection.
    /// </summary>
    [JsonPropertyName("codeSnippet")]
    public required string CodeSnippet { get; init; }

    /// <summary>
    /// The original message from the detection.
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    /// <summary>
    /// When this entry was added to the baseline.
    /// </summary>
    [JsonPropertyName("baselinedAt")]
    public DateTimeOffset BaselinedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Computes a stable hash for a detection result.
    /// </summary>
    /// <param name="ruleId">The rule ID</param>
    /// <param name="relativePath">Relative file path</param>
    /// <param name="codeSnippet">The code that triggered the detection</param>
    /// <returns>A stable SHA256 hash (first 16 chars)</returns>
    public static string ComputeHash(string ruleId, string relativePath, string codeSnippet)
    {
        // Normalize the code snippet: trim whitespace, normalize line endings
        var normalizedSnippet = NormalizeCodeSnippet(codeSnippet);

        // Normalize path separators
        var normalizedPath = relativePath.Replace('\\', '/');

        var input = $"{ruleId}|{normalizedPath}|{normalizedSnippet}";
        var bytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = SHA256.HashData(bytes);

        // Return first 16 chars of hex string for readability
        return Convert.ToHexString(hashBytes)[..16].ToLowerInvariant();
    }

    /// <summary>
    /// Normalizes a code snippet for stable hashing.
    /// </summary>
    private static string NormalizeCodeSnippet(string snippet)
    {
        if (string.IsNullOrEmpty(snippet))
            return string.Empty;

        // Normalize line endings
        var normalized = snippet.Replace("\r\n", "\n").Replace("\r", "\n");

        // Trim each line and rejoin
        var lines = normalized.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrEmpty(l));

        return string.Join("\n", lines);
    }
}
