namespace Slopwatch.Baseline;

/// <summary>
/// Thrown when a baseline file was written by an older schema version whose entry hashes cannot
/// be matched by the current identity scheme.
/// </summary>
/// <remarks>
/// This is deliberately a hard failure rather than a warning. A stale baseline does not degrade
/// gracefully: every entry silently stops matching, so the user sees their entire baselined
/// backlog reported as new findings. Failing here turns a confusing wall of output into one
/// actionable instruction.
/// </remarks>
public sealed class BaselineVersionException : Exception
{
    /// <summary>
    /// Creates the exception for a baseline written by an older schema version.
    /// </summary>
    /// <param name="path">Path to the baseline file that could not be used.</param>
    /// <param name="foundVersion">The schema version recorded in the file.</param>
    /// <param name="requiredVersion">The schema version this build requires.</param>
    /// <param name="rootDirectory">
    /// The analysis root the baseline was resolved against, when known. The remedy has to preserve
    /// it: running init from a different directory scans the wrong tree and records paths relative
    /// to the wrong root, so the regenerated baseline would not match the next analysis.
    /// </param>
    /// <param name="configPath">
    /// The configuration file the analysis used, when one was supplied. The remedy has to carry it:
    /// a config that enables generated-code analysis changes which findings exist, so regenerating
    /// without it records a different baseline than the next analysis expects.
    /// </param>
    public BaselineVersionException(
        string path,
        int foundVersion,
        int requiredVersion,
        string? rootDirectory = null,
        string? configPath = null)
        : base(BuildMessage(path, foundVersion, requiredVersion, rootDirectory, configPath))
    {
        Path = path;
        FoundVersion = foundVersion;
        RequiredVersion = requiredVersion;
    }

    /// <summary>
    /// Path to the baseline file that could not be used.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// The schema version recorded in the file.
    /// </summary>
    public int FoundVersion { get; }

    /// <summary>
    /// The schema version this build requires.
    /// </summary>
    public int RequiredVersion { get; }

    private static string BuildMessage(
        string path,
        int foundVersion,
        int requiredVersion,
        string? rootDirectory,
        string? configPath)
    {
        // The remedy has to name the file that actually failed AND the root it was resolved
        // against. init writes .slopwatch/baseline.json under its -d directory by default, so a
        // project using a custom --baseline path would follow the default instruction, regenerate
        // the wrong file, and hit this same error again. Running init from a different directory
        // is worse: it succeeds but records paths relative to the wrong root, so the regenerated
        // baseline silently fails to match the next analysis.
        //
        // "Default" means the complete default path for the selected root, not just the final two
        // components. A custom path like other/.slopwatch/baseline.json ends in the same two
        // names but is a different file.
        var defaultPath = rootDirectory is null
            ? null
            : System.IO.Path.GetFullPath(
                System.IO.Path.Combine(rootDirectory, ".slopwatch", "baseline.json"));

        // Case sensitivity follows the platform's file system. On Linux, .SLOPWATCH/baseline.json
        // is a different file from .slopwatch/baseline.json, so treating it as the default would
        // print a remedy that writes the wrong file and leaves the selected baseline stale.
        var pathComparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var isDefaultPath = defaultPath is not null
            && string.Equals(
                System.IO.Path.GetFullPath(path),
                defaultPath,
                pathComparison);

        var directoryArgument = rootDirectory is null
            ? string.Empty
            : $" --directory \"{rootDirectory}\"";

        // init resolves --baseline relative to its --directory, exactly as analyze does. Printing
        // the absolute path would therefore be re-resolved against the root and doubled
        // (project/project/state/custom.json). Print the path relative to the root instead, which
        // is what the user originally passed to analyze.
        var baselineArgument = rootDirectory is null
            ? path
            : System.IO.Path.GetRelativePath(rootDirectory, path);

        // The config path is emitted relative to the root for the same reason as the baseline:
        // init resolves it against --directory, so an absolute path would be re-resolved.
        var configArgument = configPath is null
            ? string.Empty
            : rootDirectory is null
                ? $" --config \"{configPath}\""
                : $" --config \"{System.IO.Path.GetRelativePath(rootDirectory, configPath)}\"";

        var remedy = isDefaultPath
            ? $"    slopwatch init --force{directoryArgument}{configArgument}"
            : $"    slopwatch init --force{directoryArgument}{configArgument} --baseline \"{baselineArgument}\"";

        return $"""
        Baseline file is out of date: {path}

        Found schema version {foundVersion}, but this version of slopwatch requires version {requiredVersion}.

        Version {requiredVersion} changed how baseline entries are identified for SW001-SW006, so
        entries written by version {foundVersion} can no longer be matched. Analysis was stopped
        before running so you do not get a wall of findings that were already baselined.

        Regenerate the baseline:

        {remedy}

        This re-scans your code and records current findings under the new identity scheme. Review
        the regenerated file before committing it.
        """;
    }
}
