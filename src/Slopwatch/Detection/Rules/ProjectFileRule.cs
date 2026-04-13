using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Slopwatch.Suppression;

namespace Slopwatch.Detection.Rules;

/// <summary>
/// Detects project file (.csproj, .props, .targets) slop patterns.
/// Rule ID: SW005
/// </summary>
/// <remarks>
/// This rule identifies problematic configurations in MSBuild project files:
/// <list type="bullet">
/// <item><description>NoWarn additions - suppressing warnings at project level</description></item>
/// <item><description>TreatWarningsAsErrors disabled - turning off strict mode</description></item>
/// <item><description>Nullable disabled - turning off nullability checking</description></item>
/// <item><description>WarningsAsErrors removal - removing specific warnings from error treatment</description></item>
/// </list>
///
/// These patterns often indicate:
/// <list type="bullet">
/// <item><description>LLM-generated code that doesn't compile cleanly</description></item>
/// <item><description>Quick fixes that hide rather than solve problems</description></item>
/// <item><description>Technical debt accumulation</description></item>
/// <item><description>Regression in code quality standards</description></item>
/// </list>
///
/// This rule can be suppressed with XML comments: &lt;!-- slopwatch-ignore: SW005 justification --&gt;
/// </remarks>
public sealed class ProjectFileRule : IDetectionRule
{
    /// <inheritdoc />
    public string RuleId => "SW005";

    /// <inheritdoc />
    public string Name => "Project File Slop Detection";

    /// <inheritdoc />
    public string Description =>
        "Detects problematic configuration changes in .csproj, .props, and .targets files that may " +
        "indicate LLM-generated code or shortcuts around compilation issues. Flags NoWarn additions, " +
        "TreatWarningsAsErrors disabled, Nullable disabled, and WarningsAsErrors removal.";

    /// <inheritdoc />
    public DetectionSeverity DefaultSeverity => DetectionSeverity.Warning;

    /// <inheritdoc />
    public IReadOnlyList<string> ApplicableFilePatterns => new[] { "*.csproj", "*.props", "*.targets" };

    // Matches: <!-- slopwatch-ignore: SW005 justification text here -->
    private static readonly Regex XmlCommentSuppressionPattern = new(
        @"<!--\s*slopwatch-ignore\s*:\s*(SW\d{3})\s+(.+?)\s*-->",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const int MinJustificationLength = 20;

    /// <inheritdoc />
    public async IAsyncEnumerable<DetectionResult> AnalyzeAsync(
        DetectionContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Parse XML suppressions from content
        var suppressions = ParseXmlSuppressions(context.Content);

        XDocument document;
        try
        {
            document = XDocument.Parse(context.Content, LoadOptions.SetLineInfo);
        }
        catch
        {
            // Invalid XML - skip analysis
            yield break;
        }

        var projectRoot = GetProjectRoot(context.FilePath);
        var suppressionChecker = await SuppressionChecker.CreateAsync(context, projectRoot, cancellationToken);

        // Check for NoWarn additions
        await foreach (var result in AnalyzeNoWarn(context, document, suppressions, suppressionChecker, cancellationToken))
        {
            yield return result;
        }

        // Check for TreatWarningsAsErrors disabled
        await foreach (var result in AnalyzeTreatWarningsAsErrors(context, document, suppressions, suppressionChecker, cancellationToken))
        {
            yield return result;
        }

        // Check for Nullable disabled
        await foreach (var result in AnalyzeNullable(context, document, suppressions, suppressionChecker, cancellationToken))
        {
            yield return result;
        }

        // Check for WarningsAsErrors removal (empty or missing)
        await foreach (var result in AnalyzeWarningsAsErrors(context, document, suppressions, suppressionChecker, cancellationToken))
        {
            yield return result;
        }
    }

    /// <summary>
    /// Parses XML comment suppressions from the content.
    /// </summary>
    private static List<(string RuleId, string Justification, int Line)> ParseXmlSuppressions(string content)
    {
        var suppressions = new List<(string RuleId, string Justification, int Line)>();
        var lines = content.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var match = XmlCommentSuppressionPattern.Match(line);

            if (match.Success)
            {
                var ruleId = match.Groups[1].Value;
                var justification = match.Groups[2].Value.Trim();

                if (justification.Length >= MinJustificationLength)
                {
                    suppressions.Add((ruleId, justification, i + 1));
                }
            }
        }

        return suppressions;
    }

    /// <summary>
    /// Checks if a specific line is suppressed.
    /// </summary>
    private static bool IsXmlSuppressed(List<(string RuleId, string Justification, int Line)> suppressions, string ruleId, int lineNumber)
    {
        // Check if there's a suppression comment on the line immediately before
        return suppressions.Any(s =>
            s.RuleId.Equals(ruleId, StringComparison.OrdinalIgnoreCase) &&
            s.Line == lineNumber - 1);
    }

    /// <summary>
    /// Analyzes NoWarn elements for warning suppression.
    /// </summary>
    private async IAsyncEnumerable<DetectionResult> AnalyzeNoWarn(
        DetectionContext context,
        XDocument document,
        List<(string RuleId, string Justification, int Line)> suppressions,
        SuppressionChecker suppressionChecker,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var noWarnElements = document.Descendants()
            .Where(e => e.Name.LocalName == "NoWarn")
            .ToList();

        foreach (var element in noWarnElements)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var lineInfo = (IXmlLineInfo)element;
            var lineNumber = lineInfo.HasLineInfo() ? lineInfo.LineNumber : 0;

            // Skip if line is not in scope
            if (lineNumber > 0 && !context.IsLineInScope(lineNumber))
                continue;

            // Check if suppressed
            if (suppressionChecker.IsSuppressed(context, null, RuleId, lineNumber) || IsXmlSuppressed(suppressions, RuleId, lineNumber))
                continue;

            var value = element.Value.Trim();

            // Check if it's adding to existing NoWarn (contains $(NoWarn))
            if (value.Contains("$(NoWarn)", StringComparison.OrdinalIgnoreCase))
            {
                // Extract the warnings being added
                var warningsMatch = Regex.Match(value, @"\$\(NoWarn\)\s*;\s*(.+)");
                if (warningsMatch.Success)
                {
                    var warnings = warningsMatch.Groups[1].Value.Trim();

                    yield return new DetectionResult(
                        RuleId,
                        Name,
                        DetectionSeverity.Warning,
                        context.FilePath,
                        lineNumber,
                        lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1,
                        $"Adding warnings to NoWarn: {warnings}",
                        element.ToString(),
                        $"Remove the NoWarn entry and fix the underlying code issues causing {warnings}. If suppression is truly necessary, add: <!-- slopwatch-ignore: SW005 [your justification here] -->"
                    );
                }
            }
            else if (!string.IsNullOrWhiteSpace(value))
            {
                // Direct NoWarn value
                yield return new DetectionResult(
                    RuleId,
                    Name,
                    DetectionSeverity.Warning,
                    context.FilePath,
                    lineNumber,
                    lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1,
                    $"Setting NoWarn to suppress warnings: {value}",
                    element.ToString(),
                    $"Remove the NoWarn entry and fix the underlying code issues causing {value}. If suppression is truly necessary, add: <!-- slopwatch-ignore: SW005 [your justification here] -->"
                );
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Analyzes TreatWarningsAsErrors elements.
    /// </summary>
    private async IAsyncEnumerable<DetectionResult> AnalyzeTreatWarningsAsErrors(
        DetectionContext context,
        XDocument document,
        List<(string RuleId, string Justification, int Line)> suppressions,
        SuppressionChecker suppressionChecker,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var elements = document.Descendants()
            .Where(e => e.Name.LocalName == "TreatWarningsAsErrors")
            .ToList();

        foreach (var element in elements)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var lineInfo = (IXmlLineInfo)element;
            var lineNumber = lineInfo.HasLineInfo() ? lineInfo.LineNumber : 0;

            // Skip if line is not in scope
            if (lineNumber > 0 && !context.IsLineInScope(lineNumber))
                continue;

            // Check if suppressed
            if (suppressionChecker.IsSuppressed(context, null, RuleId, lineNumber) || IsXmlSuppressed(suppressions, RuleId, lineNumber))
                continue;

            var value = element.Value.Trim();

            // Flag if explicitly set to false
            if (value.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                yield return new DetectionResult(
                    RuleId,
                    Name,
                    DetectionSeverity.Warning,
                    context.FilePath,
                    lineNumber,
                    lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1,
                    "TreatWarningsAsErrors is disabled - warnings will not fail the build",
                    element.ToString(),
                    "Set TreatWarningsAsErrors to true and fix all warnings in the code. If you must disable it, add: <!-- slopwatch-ignore: SW005 [your justification here] -->"
                );
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Analyzes Nullable elements.
    /// </summary>
    private async IAsyncEnumerable<DetectionResult> AnalyzeNullable(
        DetectionContext context,
        XDocument document,
        List<(string RuleId, string Justification, int Line)> suppressions,
        SuppressionChecker suppressionChecker,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var elements = document.Descendants()
            .Where(e => e.Name.LocalName == "Nullable")
            .ToList();

        foreach (var element in elements)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var lineInfo = (IXmlLineInfo)element;
            var lineNumber = lineInfo.HasLineInfo() ? lineInfo.LineNumber : 0;

            // Skip if line is not in scope
            if (lineNumber > 0 && !context.IsLineInScope(lineNumber))
                continue;

            // Check if suppressed
            if (suppressionChecker.IsSuppressed(context, null, RuleId, lineNumber) || IsXmlSuppressed(suppressions, RuleId, lineNumber))
                continue;

            var value = element.Value.Trim();

            // Flag if disabled
            if (value.Equals("disable", StringComparison.OrdinalIgnoreCase))
            {
                yield return new DetectionResult(
                    RuleId,
                    Name,
                    DetectionSeverity.Info, // Info severity - some projects legitimately disable this
                    context.FilePath,
                    lineNumber,
                    lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1,
                    "Nullable reference types are disabled",
                    element.ToString(),
                    "Set Nullable to 'enable' and fix nullability warnings. If disabling is intentional, add: <!-- slopwatch-ignore: SW005 [your justification here] -->"
                );
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Analyzes WarningsAsErrors elements for removal patterns.
    /// </summary>
    private async IAsyncEnumerable<DetectionResult> AnalyzeWarningsAsErrors(
        DetectionContext context,
        XDocument document,
        List<(string RuleId, string Justification, int Line)> suppressions,
        SuppressionChecker suppressionChecker,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var elements = document.Descendants()
            .Where(e => e.Name.LocalName == "WarningsAsErrors")
            .ToList();

        foreach (var element in elements)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var lineInfo = (IXmlLineInfo)element;
            var lineNumber = lineInfo.HasLineInfo() ? lineInfo.LineNumber : 0;

            // Skip if line is not in scope
            if (lineNumber > 0 && !context.IsLineInScope(lineNumber))
                continue;

            // Check if suppressed
            if (suppressionChecker.IsSuppressed(context, null, RuleId, lineNumber) || IsXmlSuppressed(suppressions, RuleId, lineNumber))
                continue;

            var value = element.Value.Trim();

            // Flag if empty (removing all warnings from error treatment)
            if (string.IsNullOrWhiteSpace(value))
            {
                yield return new DetectionResult(
                    RuleId,
                    Name,
                    DetectionSeverity.Warning,
                    context.FilePath,
                    lineNumber,
                    lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1,
                    "WarningsAsErrors is empty - no warnings will be treated as errors",
                    element.ToString(),
                    "Remove the empty WarningsAsErrors element or specify which warnings should be errors. If intentional, add: <!-- slopwatch-ignore: SW005 [your justification here] -->"
                );
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Gets the project root directory from a file path.
    /// </summary>
    private static string GetProjectRoot(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath) ?? Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(directory))
        {
            try
            {
                if (Directory.Exists(Path.Combine(directory, ".git")) ||
                    Directory.Exists(Path.Combine(directory, ".slopwatch")) ||
                    Directory.GetFiles(directory, "*.sln").Any() ||
                    Directory.GetFiles(directory, "*.slnx").Any())
                {
                    return directory;
                }
            }
            catch
            {
            }

            var parent = Path.GetDirectoryName(directory);
            if (parent == directory)
                break;
            directory = parent;
        }
        return Path.GetDirectoryName(filePath) ?? Directory.GetCurrentDirectory();
    }
}
