// -----------------------------------------------------------------------
// <copyright file="BaselineFile.cs" company="Aaron Stannard">
//     Copyright (C) 2025 - 2025 Aaron Stannard
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Slopwatch.Detection;

namespace Slopwatch.Baseline;

/// <summary>
/// Represents a baseline file containing acknowledged pre-existing detections.
/// </summary>
public sealed class BaselineFile
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Schema version for forward compatibility.
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; init; } = 1;

    /// <summary>
    /// When this baseline was created.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When this baseline was last updated.
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Optional description of the baseline.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// The baselined detection entries.
    /// </summary>
    [JsonPropertyName("entries")]
    public List<BaselineEntry> Entries { get; init; } = new();

    /// <summary>
    /// Lookup set for fast hash checking.
    /// </summary>
    [JsonIgnore]
    private HashSet<string>? _hashLookup;

    /// <summary>
    /// Gets the hash lookup set, building it on first access.
    /// </summary>
    [JsonIgnore]
    private HashSet<string> HashLookup => _hashLookup ??= Entries.Select(e => e.Hash).ToHashSet();

    /// <summary>
    /// Checks if a detection is already in the baseline.
    /// </summary>
    /// <param name="ruleId">The rule ID</param>
    /// <param name="relativePath">Relative file path</param>
    /// <param name="codeSnippet">The code snippet</param>
    /// <returns>True if this detection is baselined</returns>
    public bool IsBaselined(string ruleId, string relativePath, string codeSnippet)
    {
        var hash = BaselineEntry.ComputeHash(ruleId, relativePath, codeSnippet);
        return HashLookup.Contains(hash);
    }

    /// <summary>
    /// Checks if a detection result is already in the baseline.
    /// </summary>
    /// <param name="result">The detection result</param>
    /// <param name="rootDirectory">Root directory for computing relative paths</param>
    /// <returns>True if this detection is baselined</returns>
    public bool IsBaselined(DetectionResult result, string rootDirectory)
    {
        var relativePath = GetRelativePath(result.FilePath, rootDirectory);
        return IsBaselined(result.RuleId, relativePath, result.CodeSnippet ?? string.Empty);
    }

    /// <summary>
    /// Adds a detection result to the baseline.
    /// </summary>
    /// <param name="result">The detection result to baseline</param>
    /// <param name="rootDirectory">Root directory for computing relative paths</param>
    /// <returns>True if added, false if already exists</returns>
    public bool AddEntry(DetectionResult result, string rootDirectory)
    {
        var relativePath = GetRelativePath(result.FilePath, rootDirectory);
        var codeSnippet = result.CodeSnippet ?? string.Empty;
        var hash = BaselineEntry.ComputeHash(result.RuleId, relativePath, codeSnippet);

        if (HashLookup.Contains(hash))
            return false;

        var entry = new BaselineEntry
        {
            Hash = hash,
            RuleId = result.RuleId,
            FilePath = relativePath,
            LineNumber = result.LineNumber,
            CodeSnippet = codeSnippet,
            Message = result.Message,
            BaselinedAt = DateTimeOffset.UtcNow
        };

        Entries.Add(entry);
        HashLookup.Add(hash);
        UpdatedAt = DateTimeOffset.UtcNow;

        return true;
    }

    /// <summary>
    /// Creates a baseline file from a collection of detection results.
    /// </summary>
    /// <param name="results">The detection results to baseline</param>
    /// <param name="rootDirectory">Root directory for computing relative paths</param>
    /// <param name="description">Optional description</param>
    /// <returns>A new baseline file</returns>
    public static BaselineFile Create(
        IEnumerable<DetectionResult> results,
        string rootDirectory,
        string? description = null)
    {
        var baseline = new BaselineFile
        {
            Description = description
        };

        foreach (var result in results)
        {
            baseline.AddEntry(result, rootDirectory);
        }

        return baseline;
    }

    /// <summary>
    /// Loads a baseline file from disk.
    /// </summary>
    /// <param name="path">Path to the baseline file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The loaded baseline file, or null if not found</returns>
    public static async Task<BaselineFile?> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            return null;

        await using var stream = File.OpenRead(path);
        var baseline = await JsonSerializer.DeserializeAsync<BaselineFile>(stream, JsonOptions, cancellationToken);
        return baseline;
    }

    /// <summary>
    /// Saves this baseline file to disk.
    /// </summary>
    /// <param name="path">Path to save to</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, this, JsonOptions, cancellationToken);
    }

    /// <summary>
    /// Filters detection results to only those NOT in the baseline.
    /// </summary>
    /// <param name="results">The detection results to filter</param>
    /// <param name="rootDirectory">Root directory for computing relative paths</param>
    /// <returns>Only new detections not in the baseline</returns>
    public async IAsyncEnumerable<DetectionResult> FilterNewDetectionsAsync(
        IAsyncEnumerable<DetectionResult> results,
        string rootDirectory)
    {
        await foreach (var result in results)
        {
            if (!IsBaselined(result, rootDirectory))
            {
                yield return result;
            }
        }
    }

    /// <summary>
    /// Gets the count of entries by rule ID.
    /// </summary>
    /// <returns>Dictionary of rule ID to count</returns>
    public Dictionary<string, int> GetEntriesByRule()
    {
        return Entries
            .GroupBy(e => e.RuleId)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    private static string GetRelativePath(string fullPath, string rootDirectory)
    {
        var root = Path.GetFullPath(rootDirectory);
        var file = Path.GetFullPath(fullPath);

        if (file.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            var relative = file[root.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return relative.Replace('\\', '/');
        }

        return fullPath.Replace('\\', '/');
    }
}
