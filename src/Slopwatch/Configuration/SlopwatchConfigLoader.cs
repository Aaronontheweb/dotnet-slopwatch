using System.Text.Json;
using System.Text.Json.Serialization;

namespace Slopwatch.Configuration;

/// <summary>
/// Loads <see cref="SlopwatchConfig"/> from a project root or an explicit path.
/// </summary>
/// <remarks>
/// Shared by suppression checking and analysis options so both read the same file with
/// the same parsing rules.
/// </remarks>
public static class SlopwatchConfigLoader
{
    /// <summary>
    /// The default config path relative to a project root.
    /// </summary>
    public const string DefaultRelativePath = ".slopwatch/config.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    /// Resolves the config file path for a project root, honoring an optional override.
    /// </summary>
    /// <param name="projectRoot">The project root directory.</param>
    /// <param name="overridePath">An optional explicit config path, absolute or relative to the root.</param>
    /// <returns>The resolved absolute path.</returns>
    public static string ResolvePath(string projectRoot, string? overridePath)
    {
        if (string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.Combine(projectRoot, DefaultRelativePath);
        }

        return Path.IsPathRooted(overridePath)
            ? overridePath
            : Path.GetFullPath(Path.Combine(projectRoot, overridePath));
    }

    /// <summary>
    /// Loads the configuration file for a project root.
    /// </summary>
    /// <param name="projectRoot">The project root directory.</param>
    /// <param name="overridePath">An optional explicit config path, absolute or relative to the root.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The loaded configuration, or null if the file does not exist or cannot be parsed.</returns>
    public static async Task<SlopwatchConfig?> LoadAsync(
        string projectRoot,
        string? overridePath = null,
        CancellationToken cancellationToken = default)
    {
        var configPath = ResolvePath(projectRoot, overridePath);

        if (!File.Exists(configPath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(configPath, cancellationToken);
            return JsonSerializer.Deserialize<SlopwatchConfig>(json, SerializerOptions);
        }
        catch
        {
            // If config file is invalid, treat as if no config exists
            return null;
        }
    }
}
