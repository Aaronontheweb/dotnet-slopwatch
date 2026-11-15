// -----------------------------------------------------------------------
// <copyright file="BaselineFile.cs" company="Aaron Stannard">
//     Copyright (C) 2025 - 2025 Aaron Stannard
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    /// The schema version this build writes and requires.
    /// </summary>
    public const int CurrentVersion = 2;

    /// <summary>
    /// Schema version for forward compatibility.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Version 2 changed the baseline identity for SW001-SW006 from raw syntax text to a
    /// structural path (see <see cref="StructuralPath"/>). Entries written by version 1 for those
    /// rules no longer match and must be regenerated with <c>slopwatch init --force</c>.
    /// SW021 already used a structural identity in version 1, so its entries are unaffected.
    /// </para>
    /// <para>
    /// Loading a version 1 file throws <see cref="BaselineVersionException"/> before analysis
    /// runs, so the user gets one clear instruction instead of a wall of already-baselined
    /// findings. A real migration path — recording which identity scheme produced each entry and
    /// re-deriving old entries — is tracked by issue #107 (version metadata in baselines).
    /// </para>
    /// </remarks>
    [JsonPropertyName("version")]
    public int Version { get; init; } = CurrentVersion;

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
    /// Checks if a structural identity is already in the baseline.
    /// </summary>
    /// <param name="ruleId">The rule ID</param>
    /// <param name="relativePath">Relative file path</param>
    /// <param name="structuralIdentity">The structural identity produced by the rule</param>
    /// <returns>True if this detection is baselined</returns>
    /// <remarks>
    /// Uses <see cref="BaselineEntry.ComputeStructuralHash"/>, which preserves content. The
    /// snippet overload above trims every line, which erases whitespace inside multiline literals
    /// and would let one finding's baseline entry exempt a different finding.
    /// </remarks>
    public bool IsStructuralBaselined(string ruleId, string relativePath, string structuralIdentity)
    {
        var hash = BaselineEntry.ComputeStructuralHash(ruleId, relativePath, structuralIdentity);
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
        return IsStructuralBaselined(result.RuleId, relativePath, result.BaselineIdentity);
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
        var hash = BaselineEntry.ComputeStructuralHash(result.RuleId, relativePath, result.BaselineIdentity);

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
        // Invalidate rather than mutate: HashLookup may have been materialized before this entry
        // existed, and mutating a cached set leaves it inconsistent with Entries.
        _hashLookup = null;
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
    /// <exception cref="BaselineVersionException">
    /// The file was written by an older schema version whose entry hashes cannot be matched by
    /// the current identity scheme.
    /// </exception>
    public static async Task<BaselineFile?> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            return null;

        // Read the raw document first. Version defaults to CurrentVersion so a freshly constructed
        // baseline serializes correctly, which makes an omitted property indistinguishable from an
        // explicit current version. A baseline written before versioning existed has no property at
        // all, and treating it as current would let it load and silently match nothing — the exact
        // failure the version check exists to prevent. Presence has to be checked on the JSON.
        JsonNode? document;
        await using (var stream = File.OpenRead(path))
        {
            document = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken);
        }

        var baseline = document?.Deserialize<BaselineFile>(JsonOptions);
        if (baseline is null)
            return null;

        var declaredVersion = document is JsonObject obj && obj.TryGetPropertyValue("version", out var versionNode)
            ? versionNode?.GetValue<int>()
            : (int?)null;

        // Fail before any analysis runs. A version 1 baseline hashed SW001-SW006 entries from raw
        // syntax text; the current code hashes them from a structural path, so every entry would
        // silently fail to match and the user would see their entire baseline reappear as new
        // findings with no explanation. Refuse to load instead of producing that wall of noise.
        if (declaredVersion is null)
            throw new BaselineVersionException(path, 1, CurrentVersion);

        if (declaredVersion < CurrentVersion)
            throw new BaselineVersionException(path, declaredVersion.Value, CurrentVersion);

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

    /// <summary>
    /// Synchronizes the baseline with current detection results - removes stale entries
    /// that no longer exist in the provided scan scope and adds any new detections.
    /// </summary>
    /// <param name="results">The current detection results</param>
    /// <param name="rootDirectory">Root directory for computing relative paths</param>
    /// <param name="scannedFiles">Optional list of files included in the current scan;
    /// only entries for these files are considered for stale removal.</param>
    /// <returns>Tuple of (removed, added, kept) counts</returns>
    /// <remarks>
    /// Retained as a distinct overload so previously compiled consumers that bind the
    /// three-parameter signature keep working.
    /// </remarks>
    public (int removed, int added, int kept) SyncWithDetections(
        IEnumerable<DetectionResult> results,
        string rootDirectory,
        IEnumerable<string>? scannedFiles = null)
    {
        return SyncWithDetections(results, rootDirectory, scannedFiles, skippedFiles: null);
    }

    /// <summary>
    /// Synchronizes the baseline with current detection results - removes stale entries
    /// that no longer exist in the provided scan scope and adds any new detections.
    /// </summary>
    /// <param name="results">The current detection results</param>
    /// <param name="rootDirectory">Root directory for computing relative paths</param>
    /// <param name="scannedFiles">Optional list of files included in the current scan;
    /// only entries for these files are considered for stale removal.</param>
    /// <param name="skippedFiles">Optional list of files that were present in the scan but
    /// deliberately not analyzed (for example, generated code). Entries for these files are
    /// never removed, so changing the analysis configuration later does not resurface findings.</param>
    /// <returns>Tuple of (removed, added, kept) counts</returns>
    /// <remarks>
    /// Preservation is scoped to whole files. A file that was only partially analyzed — some
    /// findings filtered out as generated, others reported — is pruned normally. The baseline
    /// hash is rule + path + snippet, which cannot distinguish two identical snippets in the
    /// same file, and line numbers are neither stable under edits nor unique within a line.
    /// Preserving individual findings would therefore risk shielding a stale hand-written entry,
    /// which is a false negative. Pruning a live generated entry is only noise, so the
    /// conservative direction is to prune.
    /// </remarks>
    public (int removed, int added, int kept) SyncWithDetections(
        IEnumerable<DetectionResult> results,
        string rootDirectory,
        IEnumerable<string>? scannedFiles,
        IEnumerable<string>? skippedFiles)
    {
        // Build a set of all current hashes from the detection results
        var currentHashes = new HashSet<string>();
        foreach (var result in results)
        {
            var relativePath = GetRelativePath(result.FilePath, rootDirectory);
            var hash = BaselineEntry.ComputeStructuralHash(result.RuleId, relativePath, result.BaselineIdentity);
            currentHashes.Add(hash);
        }

        HashSet<string>? scopedFilePaths = null;
        if (scannedFiles is not null)
        {
            var normalizedScannedPaths = scannedFiles
                .Select(path => GetRelativePath(path, rootDirectory))
                .Select(path => path.Replace('\\', '/'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (normalizedScannedPaths.Count > 0)
            {
                scopedFilePaths = normalizedScannedPaths;
            }
        }

        HashSet<string>? skippedFilePaths = null;
        if (skippedFiles is not null)
        {
            var normalizedSkippedPaths = skippedFiles
                .Select(path => GetRelativePath(path, rootDirectory))
                .Select(path => path.Replace('\\', '/'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (normalizedSkippedPaths.Count > 0)
            {
                skippedFilePaths = normalizedSkippedPaths;
            }
        }

        var removed = 0;
        var kept = 0;

        // Remove entries whose hashes no longer appear in the current analysis
        Entries.RemoveAll(e =>
        {
            if (scopedFilePaths is not null && !scopedFilePaths.Contains(e.FilePath))
            {
                return false;
            }

            // Files that were skipped rather than analyzed keep their entries: the absence of
            // findings reflects the configuration, not a fix.
            if (skippedFilePaths is not null && skippedFilePaths.Contains(e.FilePath))
            {
                kept++;
                return false;
            }

            if (!currentHashes.Contains(e.Hash))
            {
                removed++;
                return true;
            }

            kept++;
            return false;
        });

        // Rebuild hash lookup after removals
        _hashLookup = Entries.Select(e => e.Hash).ToHashSet();

        // Add new entries - those in current results but not in the (remaining) baseline
        var added = 0;
        foreach (var result in results)
        {
            var relativePath = GetRelativePath(result.FilePath, rootDirectory);
            var hash = BaselineEntry.ComputeStructuralHash(result.RuleId, relativePath, result.BaselineIdentity);

            if (!HashLookup.Contains(hash))
            {
                var entry = new BaselineEntry
                {
                    Hash = hash,
                    RuleId = result.RuleId,
                    FilePath = relativePath,
                    LineNumber = result.LineNumber,
                    CodeSnippet = result.CodeSnippet ?? string.Empty,
                    Message = result.Message,
                    BaselinedAt = DateTimeOffset.UtcNow
                };

                Entries.Add(entry);
                HashLookup.Add(hash);
                added++;
            }
        }

        UpdatedAt = DateTimeOffset.UtcNow;

        return (removed, added, kept);
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
