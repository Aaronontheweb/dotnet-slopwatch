using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Slopwatch.Detection;

namespace Slopwatch.Analysis;

/// <summary>
/// Main analysis engine for detecting code patterns across files.
/// </summary>
/// <remarks>
/// The FileAnalyzer orchestrates the analysis process by:
/// 1. Reading file content
/// 2. Determining file type and context (e.g., test files)
/// 3. Creating DetectionContext objects
/// 4. Running applicable rules
/// 5. Collecting and filtering results based on options
/// </remarks>
public sealed class FileAnalyzer
{
    private readonly RuleRegistry _ruleRegistry;
    private readonly AnalysisOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileAnalyzer"/> class.
    /// </summary>
    /// <param name="rules">The collection of detection rules to use for analysis.</param>
    /// <param name="options">The analysis options to configure behavior. If null, default options are used.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="rules"/> is null.</exception>
    public FileAnalyzer(IEnumerable<IDetectionRule> rules, AnalysisOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(rules);

        _ruleRegistry = new RuleRegistry();
        _ruleRegistry.RegisterRules(rules);
        _options = options ?? new AnalysisOptions();
    }

    /// <summary>
    /// Analyzes a single file for code pattern violations.
    /// </summary>
    /// <param name="filePath">The path to the file to analyze.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>An async enumerable of detection results.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="filePath"/> is null or whitespace.</exception>
    /// <exception cref="FileNotFoundException">Thrown if the specified file does not exist.</exception>
    public async IAsyncEnumerable<DetectionResult> AnalyzeFileAsync(
        string filePath,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}", filePath);
        }

        // Check if file should be excluded
        if (ShouldExcludeFile(filePath))
        {
            yield break;
        }

        // Read file content
        var content = await File.ReadAllTextAsync(filePath, cancellationToken);

        // Create detection context
        var context = CreateDetectionContext(filePath, content);

        // Files with embedded C# (e.g. .razor, .cshtml) produce a syntax tree from
        // generated code. Track this so we can remap line numbers after rules run.
        var hasGeneratedSource = context.SyntaxTree is not null
            && !filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

        // For files that contain embedded C#, look up rules using the effective
        // analysis type so all C# rules apply automatically.
        var ruleMatchPath = hasGeneratedSource
            ? Path.ChangeExtension(filePath, ".cs")
            : filePath;

        // Get applicable rules
        var rules = _ruleRegistry.GetRulesForFile(ruleMatchPath);

        // Filter rules based on options
        var enabledRules = rules.Where(r => _options.IsRuleEnabled(r.RuleId)).ToList();

        if (enabledRules.Count == 0)
        {
            yield break;
        }

        // Run all applicable rules and yield results
        await foreach (var result in RunRulesAsync(enabledRules, context, cancellationToken))
        {
            // Filter by minimum severity
            if (result.Severity >= _options.MinimumSeverity)
            {
                // For generated sources, remap line numbers from the generated C#
                // back to the original file using #line directive mappings.
                yield return hasGeneratedSource
                    ? RemapLineNumber(result, context.SyntaxTree!)
                    : result;
            }
        }
    }

    /// <summary>
    /// Analyzes multiple files for code pattern violations.
    /// </summary>
    /// <param name="filePaths">The paths to the files to analyze.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>An async enumerable of detection results.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="filePaths"/> is null.</exception>
    public async IAsyncEnumerable<DetectionResult> AnalyzeFilesAsync(
        IEnumerable<string> filePaths,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        foreach (var filePath in filePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Skip files that don't exist or encounter errors
            await foreach (var result in AnalyzeFileAsync(filePath, cancellationToken))
            {
                yield return result;
            }
        }
    }

    /// <summary>
    /// Analyzes all files in a directory matching the specified patterns.
    /// </summary>
    /// <param name="directoryPath">The directory path to search.</param>
    /// <param name="patterns">
    /// The glob patterns to match files against (e.g., "*.cs", "**/*.csproj").
    /// If null or empty, defaults to ["**/*"].
    /// </param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>An async enumerable of detection results.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="directoryPath"/> is null or whitespace.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown if the specified directory does not exist.</exception>
    public async IAsyncEnumerable<DetectionResult> AnalyzeDirectoryAsync(
        string directoryPath,
        string[]? patterns = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");
        }

        // Get all matching files
        var files = GetMatchingFiles(directoryPath, patterns ?? new[] { "**/*" });

        // Analyze each file
        await foreach (var result in AnalyzeFilesAsync(files, cancellationToken))
        {
            yield return result;
        }
    }

    /// <summary>
    /// Gets all files in a directory matching the specified patterns.
    /// </summary>
    /// <param name="directoryPath">The directory to search.</param>
    /// <param name="patterns">The glob patterns to match.</param>
    /// <returns>A collection of matching file paths.</returns>
    public IEnumerable<string> GetMatchingFiles(string directoryPath, string[] patterns)
    {
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);

        // Add include patterns
        foreach (var pattern in patterns)
        {
            matcher.AddInclude(pattern);
        }

        // Add exclude patterns from options
        foreach (var excludePattern in _options.ExcludePatterns)
        {
            matcher.AddExclude(excludePattern);
        }

        var result = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(directoryPath)));

        return result.Files.Select(f => Path.Combine(directoryPath, f.Path));
    }

    /// <summary>
    /// Creates a detection context for a file.
    /// </summary>
    /// <param name="filePath">The full path to the file.</param>
    /// <param name="content">The file content.</param>
    /// <returns>A detection context.</returns>
    private DetectionContext CreateDetectionContext(string filePath, string content)
    {
        var fileName = Path.GetFileName(filePath);
        var isTestFile = IsTestFile(filePath);

        // Parse C# files into syntax trees
        SyntaxTree? syntaxTree = null;
        if (filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                syntaxTree = CSharpSyntaxTree.ParseText(content, path: filePath);
            }
            catch
            {
                // If parsing fails, continue without syntax tree
                // The content will still be available for text-based analysis
            }
        }
        else if (filePath.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
        {
            var generatedCSharp = RazorCodeExtractor.ExtractGeneratedCSharp(content, filePath);
            if (generatedCSharp is not null)
            {
                try
                {
                    syntaxTree = CSharpSyntaxTree.ParseText(generatedCSharp, path: filePath);
                }
                catch
                {
                    // If parsing fails, continue without syntax tree
                }
            }
        }

        return new DetectionContext(
            FilePath: filePath,
            FileName: fileName,
            Content: content,
            SyntaxTree: syntaxTree,
            IsTestFile: isTestFile);
    }

    /// <summary>
    /// Determines if a file is a test file based on configured patterns.
    /// </summary>
    /// <param name="filePath">The file path to check.</param>
    /// <returns>True if the file is a test file; otherwise, false.</returns>
    private bool IsTestFile(string filePath)
    {
        // Normalize the path to use forward slashes
        var normalizedPath = filePath.Replace('\\', '/');

        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        foreach (var pattern in _options.TestFilePatterns)
        {
            matcher.AddInclude(pattern);
        }

        // For absolute paths, match against the full path
        // For relative paths, match as-is
        // The Matcher.Match expects a relative path, so for absolute paths
        // we need to find the root and create a relative path from it
        string pathToMatch;
        if (Path.IsPathRooted(normalizedPath))
        {
            // For Unix-like paths starting with /, remove the leading slash
            // For Windows paths like C:\, we need to remove the drive letter
            if (normalizedPath.StartsWith('/'))
            {
                pathToMatch = normalizedPath.TrimStart('/');
            }
            else
            {
                // Windows path - get the path after the drive (e.g., C:/ -> rest of path)
                var pathRoot = Path.GetPathRoot(normalizedPath);
                if (!string.IsNullOrEmpty(pathRoot))
                {
                    pathToMatch = normalizedPath.Substring(pathRoot.Length).TrimStart('/').TrimStart('\\');
                }
                else
                {
                    pathToMatch = normalizedPath;
                }
            }
        }
        else
        {
            pathToMatch = normalizedPath;
        }

        var result = matcher.Match(pathToMatch);
        return result.HasMatches;
    }

    /// <summary>
    /// Determines if a file should be excluded from analysis.
    /// </summary>
    /// <param name="filePath">The file path to check.</param>
    /// <returns>True if the file should be excluded; otherwise, false.</returns>
    private bool ShouldExcludeFile(string filePath)
    {
        // Normalize the path to use forward slashes
        var normalizedPath = filePath.Replace('\\', '/');

        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        foreach (var pattern in _options.ExcludePatterns)
        {
            matcher.AddInclude(pattern);
        }

        // For absolute paths, match against the full path
        // For relative paths, match as-is
        string pathToMatch;
        if (Path.IsPathRooted(normalizedPath))
        {
            // For Unix-like paths starting with /, remove the leading slash
            // For Windows paths like C:\, we need to remove the drive letter
            if (normalizedPath.StartsWith('/'))
            {
                pathToMatch = normalizedPath.TrimStart('/');
            }
            else
            {
                // Windows path - get the path after the drive (e.g., C:/ -> rest of path)
                var pathRoot = Path.GetPathRoot(normalizedPath);
                if (!string.IsNullOrEmpty(pathRoot))
                {
                    pathToMatch = normalizedPath.Substring(pathRoot.Length).TrimStart('/').TrimStart('\\');
                }
                else
                {
                    pathToMatch = normalizedPath;
                }
            }
        }
        else
        {
            pathToMatch = normalizedPath;
        }

        var result = matcher.Match(pathToMatch);
        return result.HasMatches;
    }

    /// <summary>
    /// Runs all applicable rules against a detection context.
    /// </summary>
    /// <param name="rules">The rules to execute.</param>
    /// <param name="context">The detection context.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>An async enumerable of detection results.</returns>
    private static async IAsyncEnumerable<DetectionResult> RunRulesAsync(
        IEnumerable<IDetectionRule> rules,
        DetectionContext context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var rule in rules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await foreach (var result in rule.AnalyzeAsync(context, cancellationToken))
            {
                yield return result;
            }
        }
    }

    /// <summary>
    /// Remaps a detection result's line number from generated code positions back to
    /// the original source file using #line directive mappings in the syntax tree.
    /// </summary>
    private static DetectionResult RemapLineNumber(DetectionResult result, SyntaxTree syntaxTree)
    {
        // The result's LineNumber is 1-based from GetLineSpan() in the generated code.
        // We need to find the corresponding position in the syntax tree and use
        // GetMappedLineSpan() to resolve the #line directives back to the original file.
        var generatedLine = result.LineNumber - 1; // 0-based for Roslyn
        var text = syntaxTree.GetText();

        if (generatedLine < 0 || generatedLine >= text.Lines.Count)
            return result;

        var position = text.Lines[generatedLine].Start;
        var mappedSpan = syntaxTree.GetMappedLineSpan(new Microsoft.CodeAnalysis.Text.TextSpan(position, 0));

        if (!mappedSpan.HasMappedPath && !mappedSpan.IsValid)
            return result;

        return result with
        {
            LineNumber = mappedSpan.StartLinePosition.Line + 1,
            Column = mappedSpan.StartLinePosition.Character + 1
        };
    }
}
