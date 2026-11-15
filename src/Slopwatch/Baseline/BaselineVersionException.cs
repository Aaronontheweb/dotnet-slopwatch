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
    public BaselineVersionException(string path, int foundVersion, int requiredVersion)
        : base(BuildMessage(path, foundVersion, requiredVersion))
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

    private static string BuildMessage(string path, int foundVersion, int requiredVersion) =>
        $"""
        Baseline file is out of date: {path}

        Found schema version {foundVersion}, but this version of slopwatch requires version {requiredVersion}.

        Version {requiredVersion} changed how baseline entries are identified for SW001-SW006, so
        entries written by version {foundVersion} can no longer be matched. Analysis was stopped
        before running so you do not get a wall of findings that were already baselined.

        Regenerate the baseline:

            slopwatch init --force

        This re-scans your code and records current findings under the new identity scheme. Review
        the regenerated file before committing it.
        """;
}
