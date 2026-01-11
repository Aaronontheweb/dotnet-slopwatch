using CommandLine;
using Slopwatch.Analysis;
using Slopwatch.Baseline;
using Slopwatch.Cmd.Output;
using Slopwatch.Detection;
using Slopwatch.Detection.Rules;

namespace Slopwatch.Cmd.Commands;

/// <summary>
/// Command for analyzing files for sloppy code patterns.
/// </summary>
[Verb("analyze", isDefault: true, HelpText = "Analyze files for LLM-generated code patterns")]
public sealed class AnalyzeCommand
{
    [Option('f', "files", HelpText = "Specific files to analyze (can be multiple)")]
    public IEnumerable<string>? Files { get; set; }

    [Option('d', "directory", HelpText = "Directory to analyze (default: current directory)")]
    public string? Directory { get; set; }

    [Option('p', "patterns", HelpText = "Glob patterns to match (default: **/*.cs, **/*.csproj)", Separator = ',')]
    public IEnumerable<string>? Patterns { get; set; }

    [Option('o', "output", HelpText = "Output format: console, json (default: console)")]
    public string Output { get; set; } = "console";

    [Option("min-severity", HelpText = "Minimum severity to report: info, warning, error (default: warning)")]
    public string MinSeverity { get; set; } = "warning";

    [Option("fail-on", HelpText = "Exit with code 1 if issues at this severity or higher: info, warning, error (default: error)")]
    public string FailOn { get; set; } = "error";

    [Option("exclude", HelpText = "Patterns to exclude", Separator = ',')]
    public IEnumerable<string>? Exclude { get; set; }

    [Option('c', "config", HelpText = "Path to configuration file")]
    public string? ConfigFile { get; set; }

    [Option("baseline", HelpText = "Path to baseline file (default: .slopwatch/baseline.json)")]
    public string BaselineFile { get; set; } = ".slopwatch/baseline.json";

    [Option("no-baseline", HelpText = "Skip baseline checking - report ALL detections (useful for initial setup)")]
    public bool NoBaseline { get; set; }

    [Option("create-baseline", HelpText = "Create a baseline file from current detections (skips normal output)")]
    public string? CreateBaseline { get; set; }

    [Option("update-baseline", HelpText = "Add new detections to an existing baseline file")]
    public bool UpdateBaseline { get; set; }

    /// <summary>
    /// Executes the analyze command.
    /// </summary>
    /// <returns>Exit code: 0 = success, 1 = issues found, 2 = error</returns>
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Validate mutually exclusive options
            if (!string.IsNullOrEmpty(CreateBaseline) && UpdateBaseline)
            {
                await Console.Error.WriteLineAsync("Cannot use --create-baseline and --update-baseline together");
                return 2;
            }

            if (NoBaseline && UpdateBaseline)
            {
                await Console.Error.WriteLineAsync("Cannot use --no-baseline and --update-baseline together");
                return 2;
            }

            if (NoBaseline && !string.IsNullOrEmpty(CreateBaseline))
            {
                await Console.Error.WriteLineAsync("Cannot use --no-baseline and --create-baseline together");
                return 2;
            }

            // Parse severity levels
            if (!TryParseSeverity(MinSeverity, out var minSeverity))
            {
                await Console.Error.WriteLineAsync($"Invalid min-severity: {MinSeverity}. Valid values: info, warning, error");
                return 2;
            }

            if (!TryParseSeverity(FailOn, out var failOnSeverity))
            {
                await Console.Error.WriteLineAsync($"Invalid fail-on: {FailOn}. Valid values: info, warning, error");
                return 2;
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

            // Determine root directory
            var rootDirectory = Directory ?? System.IO.Directory.GetCurrentDirectory();

            // Load baseline (required by default unless --no-baseline or --create-baseline)
            Baseline.BaselineFile? baseline = null;
            var useBaseline = !NoBaseline && string.IsNullOrEmpty(CreateBaseline);

            // Resolve baseline path relative to root directory
            var resolvedBaselinePath = Path.IsPathRooted(BaselineFile)
                ? BaselineFile
                : Path.Combine(rootDirectory, BaselineFile);

            if (useBaseline)
            {
                baseline = await Baseline.BaselineFile.LoadAsync(resolvedBaselinePath, cancellationToken);

                if (baseline is null && !UpdateBaseline)
                {
                    await Console.Error.WriteLineAsync($"Baseline file not found: {resolvedBaselinePath}");
                    await Console.Error.WriteLineAsync();
                    await Console.Error.WriteLineAsync("Slopwatch requires a baseline to detect NEW issues.");
                    await Console.Error.WriteLineAsync("Run 'slopwatch init' to create a baseline from existing code.");
                    await Console.Error.WriteLineAsync();
                    await Console.Error.WriteLineAsync("Or use --no-baseline to analyze ALL code (not recommended for CI/CD).");
                    return 2;
                }

                baseline ??= new Baseline.BaselineFile();

                if (!UpdateBaseline)
                {
                    var countsByRule = baseline.GetEntriesByRule();
                    var totalBaselined = baseline.Entries.Count;
                    if (totalBaselined > 0)
                    {
                        await Console.Error.WriteLineAsync($"Loaded baseline with {totalBaselined} entries ({string.Join(", ", countsByRule.Select(kv => $"{kv.Key}: {kv.Value}"))})");
                    }
                }
            }

            // Create all detection rules
            var rules = CreateDetectionRules();

            // Create file analyzer
            var analyzer = new FileAnalyzer(rules, options);

            // Determine what to analyze
            IAsyncEnumerable<DetectionResult> results;

            if (Files is not null && Files.Any())
            {
                // Analyze specific files
                var filePaths = Files.Select(f => Path.GetFullPath(f)).ToList();

                // Validate files exist
                foreach (var file in filePaths)
                {
                    if (!File.Exists(file))
                    {
                        await Console.Error.WriteLineAsync($"File not found: {file}");
                        return 2;
                    }
                }

                results = analyzer.AnalyzeFilesAsync(filePaths, cancellationToken);
            }
            else
            {
                // Analyze directory
                if (!System.IO.Directory.Exists(rootDirectory))
                {
                    await Console.Error.WriteLineAsync($"Directory not found: {rootDirectory}");
                    return 2;
                }

                // CommandLineParser initializes IEnumerable to empty (not null), so check Any()
                var patterns = Patterns?.Any() == true ? Patterns.ToArray() : new[] { "**/*.cs", "**/*.csproj" };
                results = analyzer.AnalyzeDirectoryAsync(rootDirectory, patterns, cancellationToken);
            }

            // Handle --create-baseline mode
            if (!string.IsNullOrEmpty(CreateBaseline))
            {
                return await CreateBaselineAsync(results, rootDirectory, CreateBaseline, cancellationToken);
            }

            // Handle --update-baseline mode
            if (UpdateBaseline && baseline is not null)
            {
                return await UpdateBaselineAsync(results, rootDirectory, baseline, resolvedBaselinePath, cancellationToken);
            }

            // Filter against baseline if specified
            if (baseline is not null)
            {
                results = baseline.FilterNewDetectionsAsync(results, rootDirectory);
            }

            // Create output formatter
            var formatter = CreateOutputFormatter();

            // Track results for exit code determination
            var issueTracker = new IssueTracker(failOnSeverity);
            var trackedResults = TrackResultsAsync(results, issueTracker, cancellationToken);

            // Format and output results
            await formatter.FormatAsync(trackedResults, Console.Out, cancellationToken);

            // Determine exit code
            return issueTracker.ShouldFail ? 1 : 0;
        }
        catch (OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("Analysis cancelled");
            return 2;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Error during analysis: {ex.Message}");
            if (ex.InnerException is not null)
            {
                await Console.Error.WriteLineAsync($"  {ex.InnerException.Message}");
            }
            return 2;
        }
    }

    private static async Task<int> CreateBaselineAsync(
        IAsyncEnumerable<DetectionResult> results,
        string rootDirectory,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var allResults = new List<DetectionResult>();
        await foreach (var result in results.WithCancellation(cancellationToken))
        {
            allResults.Add(result);
        }

        var baseline = Baseline.BaselineFile.Create(
            allResults,
            rootDirectory,
            $"Baseline created on {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");

        await baseline.SaveAsync(outputPath, cancellationToken);

        var countsByRule = baseline.GetEntriesByRule();
        await Console.Out.WriteLineAsync($"Created baseline at: {outputPath}");
        await Console.Out.WriteLineAsync($"Total entries: {baseline.Entries.Count}");

        foreach (var (ruleId, count) in countsByRule.OrderBy(kv => kv.Key))
        {
            await Console.Out.WriteLineAsync($"  {ruleId}: {count}");
        }

        return 0;
    }

    private static async Task<int> UpdateBaselineAsync(
        IAsyncEnumerable<DetectionResult> results,
        string rootDirectory,
        Baseline.BaselineFile baseline,
        string baselinePath,
        CancellationToken cancellationToken)
    {
        var addedCount = 0;
        var skippedCount = 0;

        await foreach (var result in results.WithCancellation(cancellationToken))
        {
            if (baseline.AddEntry(result, rootDirectory))
            {
                addedCount++;
            }
            else
            {
                skippedCount++;
            }
        }

        if (addedCount > 0)
        {
            await baseline.SaveAsync(baselinePath, cancellationToken);
            await Console.Out.WriteLineAsync($"Updated baseline at: {baselinePath}");
            await Console.Out.WriteLineAsync($"  Added: {addedCount} new entries");
            await Console.Out.WriteLineAsync($"  Skipped: {skippedCount} already baselined");
            await Console.Out.WriteLineAsync($"  Total: {baseline.Entries.Count} entries");
        }
        else
        {
            await Console.Out.WriteLineAsync("No new entries to add to baseline");
        }

        return 0;
    }

    private static IEnumerable<IDetectionRule> CreateDetectionRules()
    {
        // Return all built-in rules
        yield return new DisabledTestRule();
        yield return new WarningSuppressRule();
        yield return new EmptyCatchBlockRule();
        yield return new TimeoutJigglingRule();
        yield return new ProjectFileRule();
    }

    private IOutputFormatter CreateOutputFormatter()
    {
        return Output.ToLowerInvariant() switch
        {
            "json" => new JsonOutputFormatter(),
            "console" => new ConsoleOutputFormatter(),
            _ => throw new InvalidOperationException($"Unknown output format: {Output}")
        };
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

    private static async IAsyncEnumerable<DetectionResult> TrackResultsAsync(
        IAsyncEnumerable<DetectionResult> results,
        IssueTracker tracker,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var result in results.WithCancellation(cancellationToken))
        {
            tracker.Track(result);
            yield return result;
        }
    }

    /// <summary>
    /// Helper class to track issues and determine if we should fail.
    /// </summary>
    private sealed class IssueTracker
    {
        private readonly DetectionSeverity _failOnSeverity;

        public IssueTracker(DetectionSeverity failOnSeverity)
        {
            _failOnSeverity = failOnSeverity;
        }

        public bool ShouldFail { get; private set; }

        public void Track(DetectionResult result)
        {
            if (result.Severity >= _failOnSeverity)
            {
                ShouldFail = true;
            }
        }
    }
}
