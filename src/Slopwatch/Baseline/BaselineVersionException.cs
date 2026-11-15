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

    private static string BuildMessage(string path, int foundVersion, int requiredVersion)
    {
        // The remedy has to name the file that actually failed. init writes
        // .slopwatch/baseline.json by default, so a project using a custom --baseline path would
        // follow the default instruction, regenerate the wrong file, and hit this same error
        // again. Name the path explicitly whenever it is not the default.
        var isDefaultPath = string.Equals(
            System.IO.Path.GetFileName(path),
            "baseline.json",
            StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path) ?? string.Empty),
                ".slopwatch",
                StringComparison.OrdinalIgnoreCase);

        var remedy = isDefaultPath
            ? "    slopwatch init --force"
            : $"    slopwatch init --force --baseline \"{path}\"";

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
