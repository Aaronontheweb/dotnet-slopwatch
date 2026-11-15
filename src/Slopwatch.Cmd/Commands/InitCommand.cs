using CommandLine;
using Slopwatch.Analysis;
using Slopwatch.Baseline;
using Slopwatch.Detection;
using Slopwatch.Detection.Rules;

namespace Slopwatch.Cmd.Commands;

/// <summary>
/// Command for initializing slopwatch in a project.
/// Creates .slopwatch directory and baseline file.
/// </summary>
[Verb("init", HelpText = "Initialize slopwatch in a project - creates baseline from existing code")]
public sealed class InitCommand
{
    [Option('d', "directory", HelpText = "Directory to initialize (default: current directory)")]
    public string? Directory { get; set; }

    [Option('p', "patterns", HelpText = DefaultPatterns.HelpText, Separator = ',')]
    public IEnumerable<string>? Patterns { get; set; }

    [Option("exclude", HelpText = "Patterns to exclude", Separator = ',')]
    public IEnumerable<string>? Exclude { get; set; }

    [Option('f', "force", HelpText = "Overwrite existing baseline file if it exists")]
    public bool Force { get; set; }

    [Option("min-severity", HelpText = "Minimum severity to baseline: info, warning, error (default: info)")]
    public string MinSeverity { get; set; } = "info";

    [Option('c', "config", HelpText = "Path to a custom .slopwatch/config.json file")]
    public string? ConfigFile { get; set; }

    /// <summary>
    /// Executes the init command.
    /// </summary>
    /// <returns>Exit code: 0 = success, 1 = already initialized, 2 = error</returns>
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var rootDirectory = Directory ?? System.IO.Directory.GetCurrentDirectory();

            if (!System.IO.Directory.Exists(rootDirectory))
            {
                await Console.Error.WriteLineAsync($"Directory not found: {rootDirectory}");
                return 2;
            }

            var slopwatchDir = Path.Combine(rootDirectory, ".slopwatch");
            var baselinePath = Path.Combine(slopwatchDir, "baseline.json");
            var configExamplePath = Path.Combine(slopwatchDir, "config.json.example");

            // Check if already initialized
            if (File.Exists(baselinePath) && !Force)
            {
                await Console.Error.WriteLineAsync($"Slopwatch already initialized at: {baselinePath}");
                await Console.Error.WriteLineAsync("Use --force to overwrite existing baseline");
                return 1;
            }

            // Parse severity
            if (!TryParseSeverity(MinSeverity, out var minSeverity))
            {
                await Console.Error.WriteLineAsync($"Invalid min-severity: {MinSeverity}. Valid values: info, warning, error");
                return 2;
            }

            await Console.Out.WriteLineAsync($"Initializing slopwatch in: {rootDirectory}");
            await Console.Out.WriteLineAsync();

            // Create .slopwatch directory
            if (!System.IO.Directory.Exists(slopwatchDir))
            {
                System.IO.Directory.CreateDirectory(slopwatchDir);
                await Console.Out.WriteLineAsync($"Created: {slopwatchDir}");
            }

            // Create analysis options
            var options = new AnalysisOptions
            {
                MinimumSeverity = minSeverity
            };

            if (Exclude is not null && Exclude.Any())
            {
                var excludeList = options.ExcludePatterns.ToList();
                excludeList.AddRange(Exclude);
                options.ExcludePatterns = excludeList;
            }

            // Apply the generatedCode section from the config file, if present. init must honor
            // the same mode as analyze, otherwise the baseline it creates disagrees with the
            // findings the next analyze run reports.
            var config = await Configuration.SlopwatchConfigLoader.LoadAsync(
                rootDirectory,
                ConfigFile,
                cancellationToken);

            if (config?.GeneratedCode is not null)
            {
                options.GeneratedCodeMode = config.GeneratedCode.Mode;
                options.DetectGeneratedCodeAttributes = config.GeneratedCode.DetectAttributes;
            }

            // Forward the config path to suppression checking as well, so suppressions from the
            // selected config apply during init exactly as they do during analyze.
            if (!string.IsNullOrWhiteSpace(ConfigFile))
            {
                var resolvedConfigPath = Path.IsPathRooted(ConfigFile)
                    ? ConfigFile
                    : Path.GetFullPath(Path.Combine(rootDirectory, ConfigFile));

                if (!File.Exists(resolvedConfigPath))
                {
                    await Console.Error.WriteLineAsync($"Configuration file not found: {resolvedConfigPath}");
                    return 2;
                }

                Suppression.SuppressionChecker.SetConfigPathOverride(resolvedConfigPath);
            }
            else
            {
                Suppression.SuppressionChecker.SetConfigPathOverride(null);
            }

            // Create all detection rules
            var rules = CreateDetectionRules();
            var analyzer = new FileAnalyzer(rules, options);

            // Analyze directory
            // CommandLineParser initializes IEnumerable to empty (not null), so check Any()
            var patterns = Patterns?.Any() == true ? Patterns.ToArray() : DefaultPatterns.FilePatterns;

            await Console.Out.WriteLineAsync("Scanning for existing issues...");

            var allResults = new List<DetectionResult>();
            await foreach (var result in analyzer.AnalyzeDirectoryAsync(rootDirectory, patterns, cancellationToken))
            {
                allResults.Add(result);
            }

            // Create baseline
            var baseline = BaselineFile.Create(
                allResults,
                rootDirectory,
                $"Initial baseline created by 'slopwatch init' on {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");

            await baseline.SaveAsync(baselinePath, cancellationToken);

            // Create example config if it doesn't exist
            if (!File.Exists(configExamplePath))
            {
                await CreateExampleConfigAsync(configExamplePath);
            }

            // Summary
            await Console.Out.WriteLineAsync();
            await Console.Out.WriteLineAsync("Slopwatch initialized successfully!");
            await Console.Out.WriteLineAsync();
            await Console.Out.WriteLineAsync($"  Baseline: {baselinePath}");
            await Console.Out.WriteLineAsync($"  Entries:  {baseline.Entries.Count}");

            if (baseline.Entries.Count > 0)
            {
                await Console.Out.WriteLineAsync();
                await Console.Out.WriteLineAsync("  Baselined issues by rule:");
                foreach (var (ruleId, count) in baseline.GetEntriesByRule().OrderBy(kv => kv.Key))
                {
                    await Console.Out.WriteLineAsync($"    {ruleId}: {count}");
                }
            }

            await Console.Out.WriteLineAsync();
            await Console.Out.WriteLineAsync("Next steps:");
            await Console.Out.WriteLineAsync("  1. Commit .slopwatch/baseline.json to your repository");
            await Console.Out.WriteLineAsync("  2. Run 'slopwatch analyze' to check for NEW issues");
            await Console.Out.WriteLineAsync("  3. Add slopwatch to your CI/CD pipeline or Claude Code hooks");

            return 0;
        }
        catch (OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("Initialization cancelled");
            return 2;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Error during initialization: {ex.Message}");
            return 2;
        }
        finally
        {
            Suppression.SuppressionChecker.SetConfigPathOverride(null);
        }
    }

    private static IEnumerable<IDetectionRule> CreateDetectionRules()
    {
        yield return new DisabledTestRule();
        yield return new WarningSuppressRule();
        yield return new EmptyCatchBlockRule();
        yield return new TimeoutJigglingRule();
        yield return new ProjectFileRule();
        yield return new PackageVersionOverrideRule();
    }

    private static bool TryParseSeverity(string severityText, out DetectionSeverity severity)
    {
        switch (severityText.ToLowerInvariant())
        {
            case "info":
                severity = DetectionSeverity.Info;
                return true;
            case "warning":
                severity = DetectionSeverity.Warning;
                return true;
            case "error":
                severity = DetectionSeverity.Error;
                return true;
            default:
                severity = default;
                return false;
        }
    }

    private static async Task CreateExampleConfigAsync(string path)
    {
        var exampleConfig = """
            {
              "suppressions": [
                {
                  "ruleId": "SW002",
                  "pattern": "**/Generated/**",
                  "justification": "Generated code from protobuf/gRPC compiler - cannot be modified"
                }
              ],
              "globalSuppressions": []
            }
            """;

        await File.WriteAllTextAsync(path, exampleConfig);
    }
}
