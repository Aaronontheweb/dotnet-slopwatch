using CommandLine;
using Slopwatch.Analysis;
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

    /// <summary>
    /// Executes the analyze command.
    /// </summary>
    /// <returns>Exit code: 0 = success, 1 = issues found, 2 = error</returns>
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        try
        {
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

            // Create all detection rules
            var rules = CreateDetectionRules();

            // Create file analyzer
            var analyzer = new FileAnalyzer(rules, options);

            // Create output formatter
            var formatter = CreateOutputFormatter();

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
                var directory = Directory ?? System.IO.Directory.GetCurrentDirectory();

                if (!System.IO.Directory.Exists(directory))
                {
                    await Console.Error.WriteLineAsync($"Directory not found: {directory}");
                    return 2;
                }

                var patterns = Patterns?.ToArray() ?? new[] { "**/*.cs", "**/*.csproj" };
                results = analyzer.AnalyzeDirectoryAsync(directory, patterns, cancellationToken);
            }

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
