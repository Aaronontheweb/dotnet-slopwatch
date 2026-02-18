namespace Slopwatch.Cmd.Commands;

/// <summary>
/// Default file patterns used by slopwatch commands.
/// </summary>
internal static class DefaultPatterns
{
    /// <summary>
    /// Default glob patterns for scanning .NET projects.
    /// Includes C# and Razor source files plus MSBuild project/props/targets files.
    /// </summary>
    public static readonly string[] FilePatterns =
    {
        "**/*.cs",
        "**/*.razor",
        "**/*.cshtml",
        "**/*.csproj",
        "**/*.props",
        "**/*.targets"
    };

    /// <summary>
    /// Human-readable description of the default patterns for help text.
    /// </summary>
    public const string HelpText = "Glob patterns to match (default: **/*.cs, **/*.razor, **/*.cshtml, **/*.csproj, **/*.props, **/*.targets)";
}
