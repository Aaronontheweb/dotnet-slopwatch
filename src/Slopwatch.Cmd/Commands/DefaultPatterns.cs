namespace Slopwatch.Cmd.Commands;

/// <summary>
/// Default file patterns used by slopwatch commands.
/// </summary>
internal static class DefaultPatterns
{
    /// <summary>
    /// Default glob patterns for scanning .NET projects.
    /// Includes C# source files and MSBuild project/props/targets files.
    /// </summary>
    public static readonly string[] FilePatterns =
    {
        "**/*.cs",
        "**/*.csproj",
        "**/*.props",
        "**/*.targets"
    };

    /// <summary>
    /// Human-readable description of the default patterns for help text.
    /// </summary>
    public const string HelpText = "Glob patterns to match (default: **/*.cs, **/*.csproj, **/*.props, **/*.targets)";
}
