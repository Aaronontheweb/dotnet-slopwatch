using System.Diagnostics;
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

    [Option('p', "patterns", HelpText = DefaultPatterns.HelpText, Separator = ',')]
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

    [Option('v', "verbose", HelpText = "Show verbose output including baseline loading details")]
    public bool Verbose { get; set; }

    [Option("hook", HelpText = "Hook mode for Claude Code integration: only analyzes git dirty files for speed, outputs errors to stderr, suppresses other output, fails on warnings by default, exits with code 2 on failure")]
    public bool HookMode { get; set; }

    [Option("stats", HelpText = "Show analysis statistics (files analyzed, time elapsed)")]
    public bool ShowStats { get; set; }

    [Option("parallel", HelpText = "Number of parallel workers for file analysis (default: processor count, 0 = sequential)")]
    public int Parallelism { get; set; } = -1; // -1 means use default (processor count)

    /// <summary>
    /// Executes the analyze command.
    /// </summary>
    /// <returns>Exit code: 0 = success, 1 = issues found (normal mode), 2 = issues found (hook mode) or error</returns>
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = ShowStats ? Stopwatch.StartNew() : null;
        var filesAnalyzed = 0;

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

                if (!UpdateBaseline && Verbose)
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

            // In hook mode, only analyze dirty files from git status (much faster)
            if (HookMode && !(Files?.Any() == true))
            {
                var dirtyFiles = await GetDirtyFilesAsync(rootDirectory, cancellationToken);

                if (dirtyFiles.Count == 0)
                {
                    // No dirty files, nothing to analyze
                    return 0;
                }

                // Filter to only supported file types
                var supportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".cs", ".razor", ".cshtml", ".csproj", ".props", ".targets" };
                var filesToAnalyze = dirtyFiles
                    .Where(f => supportedExtensions.Contains(Path.GetExtension(f)))
                    .Where(File.Exists) // Skip deleted files
                    .ToList();

                if (filesToAnalyze.Count == 0)
                {
                    return 0;
                }

                filesAnalyzed = filesToAnalyze.Count;
                results = analyzer.AnalyzeFilesAsync(filesToAnalyze, cancellationToken);
            }
            else if (Files is not null && Files.Any())
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

                filesAnalyzed = filePaths.Count;
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
                var patterns = Patterns?.Any() == true ? Patterns.ToArray() : DefaultPatterns.FilePatterns;

                // Get the list of files to analyze
                var fileList = analyzer.GetMatchingFiles(rootDirectory, patterns).ToList();
                filesAnalyzed = fileList.Count;

                // Use parallel analysis for better performance on large codebases
                // Only parallelize if more than 50 files (unless explicitly disabled with --parallel 0)
                const int ParallelThreshold = 50;
                if (Parallelism != 0 && fileList.Count > ParallelThreshold)
                {
                    var parallelism = Parallelism > 0 ? Parallelism : Environment.ProcessorCount;
                    results = ParallelAnalyzer.AnalyzeFilesParallelAsync(analyzer, fileList, parallelism, cancellationToken);
                }
                else
                {
                    results = analyzer.AnalyzeFilesAsync(fileList, cancellationToken);
                }
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

            // Hook mode: collect results, output to stderr, exit with code 2 on failure
            // Hook mode defaults to warning severity for stricter checking
            if (HookMode)
            {
                // Use warning severity by default in hook mode, or user-specified --fail-on if stricter
                var hookSeverity = failOnSeverity < DetectionSeverity.Warning ? failOnSeverity : DetectionSeverity.Warning;
                return await ExecuteHookModeAsync(results, hookSeverity, cancellationToken);
            }

            // Normal mode: format and output to stdout
            var formatter = CreateOutputFormatter();

            // Track results for exit code determination
            var issueTracker = new IssueTracker(failOnSeverity);
            var trackedResults = TrackResultsAsync(results, issueTracker, cancellationToken);

            // Format and output results
            await formatter.FormatAsync(trackedResults, Console.Out, cancellationToken);

            // Output stats if requested
            if (ShowStats && stopwatch is not null)
            {
                stopwatch.Stop();
                await Console.Error.WriteLineAsync();
                await Console.Error.WriteLineAsync($"Stats: {filesAnalyzed} files analyzed in {stopwatch.Elapsed.TotalSeconds:F2}s");
            }

            // Determine exit code (1 = issues found in normal mode)
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

    /// <summary>
    /// Executes analysis in hook mode - outputs errors to stderr, suppresses other output,
    /// and exits with code 2 on failure (for Claude Code hook blocking).
    /// </summary>
    private static async Task<int> ExecuteHookModeAsync(
        IAsyncEnumerable<DetectionResult> results,
        DetectionSeverity failOnSeverity,
        CancellationToken cancellationToken)
    {
        var issues = new List<DetectionResult>();

        await foreach (var result in results.WithCancellation(cancellationToken))
        {
            if (result.Severity >= failOnSeverity)
            {
                issues.Add(result);
            }
        }

        if (issues.Count == 0)
        {
            return 0;
        }

        // Output to stderr in a format AI assistants and humans can understand
        await Console.Error.WriteLineAsync("SLOPWATCH BLOCKED: Code changes violate slopwatch rules.");
        await Console.Error.WriteLineAsync();
        await Console.Error.WriteLineAsync("=== VIOLATIONS ===");

        foreach (var issue in issues)
        {
            await Console.Error.WriteLineAsync($"- {issue.FilePath}:{issue.LineNumber} [{issue.RuleId}]: {issue.Message}");
            if (!string.IsNullOrEmpty(issue.SuggestedFix))
            {
                await Console.Error.WriteLineAsync($"  Remediation: {issue.SuggestedFix}");
            }
        }

        await Console.Error.WriteLineAsync();
        await Console.Error.WriteLineAsync("=== REQUIRED ACTION ===");
        await Console.Error.WriteLineAsync("You MUST fix these violations before proceeding. Do NOT:");
        await Console.Error.WriteLineAsync("  - Ignore this warning and continue with other tasks");
        await Console.Error.WriteLineAsync("  - Add more suppressions to hide the problem");
        await Console.Error.WriteLineAsync("  - Disable tests, warnings, or error checking");
        await Console.Error.WriteLineAsync();
        await Console.Error.WriteLineAsync("Instead, address the root cause of each violation.");

        return 2;
    }

    private static IEnumerable<IDetectionRule> CreateDetectionRules()
    {
        // Return all built-in rules
        yield return new DisabledTestRule();
        yield return new WarningSuppressRule();
        yield return new EmptyCatchBlockRule();
        yield return new TimeoutJigglingRule();
        yield return new ProjectFileRule();
        yield return new PackageVersionOverrideRule();
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
    /// Gets the list of dirty (modified, added, untracked) files from git status.
    /// </summary>
    /// <param name="rootDirectory">The root directory to run git status in.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of absolute file paths that are dirty in git.</returns>
    private static async Task<List<string>> GetDirtyFilesAsync(string rootDirectory, CancellationToken cancellationToken)
    {
        var dirtyFiles = new List<string>();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "status --porcelain",
                WorkingDirectory = rootDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return dirtyFiles;
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                return dirtyFiles;
            }

            // Parse git status --porcelain output
            // Format: XY filename (where XY is two-character status)
            // Examples:
            //  M src/file.cs       (modified in working tree)
            // M  src/file.cs       (modified in index)
            // ?? src/newfile.cs    (untracked)
            // A  src/added.cs      (added to index)
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length < 3)
                    continue;

                // Skip the two-character status and space
                var relativePath = line.Substring(3).Trim();

                // Handle renamed files (format: "old -> new")
                if (relativePath.Contains(" -> "))
                {
                    relativePath = relativePath.Split(" -> ")[1];
                }

                // Convert to absolute path
                var absolutePath = Path.GetFullPath(Path.Combine(rootDirectory, relativePath));
                dirtyFiles.Add(absolutePath);
            }
        }
        catch (Exception)
        {
            // If git fails for any reason (not installed, not a repo, etc.),
            // return empty list so slopwatch falls back to full analysis
            return dirtyFiles;
        }

        return dirtyFiles;
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
