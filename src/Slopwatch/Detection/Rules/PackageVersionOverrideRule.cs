using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Slopwatch.Detection.Rules;

/// <summary>
/// Detects CPM (Central Package Management) version override abuse.
/// Rule ID: SW006
/// </summary>
/// <remarks>
/// This rule identifies problematic package version patterns:
/// <list type="bullet">
/// <item><description>VersionOverride attribute on PackageReference - always slop</description></item>
/// <item><description>Version attribute on PackageReference in .csproj when CPM is enabled</description></item>
/// </list>
///
/// Central Package Management is detected when:
/// <list type="bullet">
/// <item><description>Directory.Packages.props exists with PackageVersion elements</description></item>
/// <item><description>ManagePackageVersionsCentrally is set to true in props files</description></item>
/// </list>
///
/// This rule can be suppressed with XML comments: &lt;!-- slopwatch-ignore: SW006 justification --&gt;
/// </remarks>
public sealed class PackageVersionOverrideRule : IDetectionRule
{
    /// <inheritdoc />
    public string RuleId => "SW006";

    /// <inheritdoc />
    public string Name => "Package Version Override Detection";

    /// <inheritdoc />
    public string Description =>
        "Detects CPM (Central Package Management) version override abuse including " +
        "VersionOverride attributes (always slop) and Version attributes on PackageReference " +
        "when Central Package Management is enabled in the repository.";

    /// <inheritdoc />
    public DetectionSeverity DefaultSeverity => DetectionSeverity.Error;

    /// <inheritdoc />
    public IReadOnlyList<string> ApplicableFilePatterns => new[] { "*.csproj", "*.props", "*.targets" };

    // Matches: <!-- slopwatch-ignore: SW006 justification text here -->
    private static readonly Regex XmlCommentSuppressionPattern = new(
        @"<!--\s*slopwatch-ignore\s*:\s*(SW\d{3})\s+(.+?)\s*-->",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const int MinJustificationLength = 20;

    // Cache for CPM detection results per directory
    private static readonly ConcurrentDictionary<string, bool> CpmCache = new();

    /// <summary>
    /// Internal testing hook to override CPM detection.
    /// When not null, this value is used instead of file system detection.
    /// </summary>
    internal bool? CpmEnabledOverride { get; set; }

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

        // Skip Directory.Packages.props entirely - that's where versions belong
        var isDirectoryPackagesProps = context.FileName.Equals("Directory.Packages.props", StringComparison.OrdinalIgnoreCase);

        // Check for VersionOverride (always slop, even in Directory.Packages.props though it shouldn't be there)
        await foreach (var result in AnalyzeVersionOverride(context, document, suppressions, cancellationToken))
        {
            yield return result;
        }

        // Skip Version attribute analysis for Directory.Packages.props
        if (isDirectoryPackagesProps)
        {
            yield break;
        }

        // Check if CPM is enabled for Version attribute analysis
        var cpmEnabled = CpmEnabledOverride ?? IsCpmEnabled(context.FilePath);

        if (cpmEnabled)
        {
            await foreach (var result in AnalyzeVersionAttribute(context, document, suppressions, cancellationToken))
            {
                yield return result;
            }
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
    /// Checks whether the specified rule is suppressed by an XML slopwatch-ignore comment
    /// on the line immediately preceding the reported XML element.
    /// </summary>
    private static bool IsSuppressedByXmlComment(List<(string RuleId, string Justification, int Line)> suppressions, string ruleId, int lineNumber)
    {
        // Check if there's a suppression comment on the line immediately before
        return suppressions.Any(s =>
            s.RuleId.Equals(ruleId, StringComparison.OrdinalIgnoreCase) &&
            s.Line == lineNumber - 1);
    }

    /// <summary>
    /// Checks if Central Package Management is enabled for a given file path.
    /// </summary>
    private static bool IsCpmEnabled(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(directory))
            return false;

        return CpmCache.GetOrAdd(directory, DetectCpmInDirectoryTree);
    }

    /// <summary>
    /// Walks up the directory tree to detect CPM configuration.
    /// </summary>
    private static bool DetectCpmInDirectoryTree(string directory)
    {
        var currentDir = directory;

        while (!string.IsNullOrEmpty(currentDir))
        {
            // Check for Directory.Packages.props
            var packagesPropsPath = Path.Combine(currentDir, "Directory.Packages.props");
            if (TryReadFileContent(packagesPropsPath, out var packagesContent))
            {
                // Check if it contains PackageVersion elements (basic heuristic)
                if (packagesContent.Contains("<PackageVersion", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                // Also check for ManagePackageVersionsCentrally
                if (packagesContent.Contains("<ManagePackageVersionsCentrally>true", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // Also check Directory.Build.props for ManagePackageVersionsCentrally
            var buildPropsPath = Path.Combine(currentDir, "Directory.Build.props");
            if (TryReadFileContent(buildPropsPath, out var buildContent))
            {
                if (buildContent.Contains("<ManagePackageVersionsCentrally>true", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            currentDir = Path.GetDirectoryName(currentDir);
        }

        return false;
    }

    /// <summary>
    /// Attempts to read file content safely, returning false if file doesn't exist or can't be read.
    /// </summary>
    private static bool TryReadFileContent(string filePath, out string content)
    {
        content = string.Empty;

        if (!File.Exists(filePath))
            return false;

        try
        {
            content = File.ReadAllText(filePath);
            return true;
        }
        catch (IOException)
        {
            // File locked or inaccessible - continue searching
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // No permission to read - continue searching
            return false;
        }
    }

    /// <summary>
    /// Analyzes VersionOverride attributes on PackageReference elements.
    /// VersionOverride is always flagged as it explicitly bypasses CPM.
    /// </summary>
    private async IAsyncEnumerable<DetectionResult> AnalyzeVersionOverride(
        DetectionContext context,
        XDocument document,
        List<(string RuleId, string Justification, int Line)> suppressions,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var packageReferences = document.Descendants()
            .Where(e => e.Name.LocalName == "PackageReference")
            .ToList();

        foreach (var element in packageReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var versionOverrideAttr = element.Attribute("VersionOverride");
            if (versionOverrideAttr is null)
                continue;

            var lineInfo = (IXmlLineInfo)element;
            var lineNumber = lineInfo.HasLineInfo() ? lineInfo.LineNumber : 0;

            // Skip if not in diff scope
            if (lineNumber > 0 && !context.IsLineInScope(lineNumber))
                continue;

            // Check if suppressed
            if (IsSuppressedByXmlComment(suppressions, RuleId, lineNumber))
                continue;

            var packageName = element.Attribute("Include")?.Value ??
                              element.Attribute("Update")?.Value ??
                              "Unknown";
            var version = versionOverrideAttr.Value;

            yield return new DetectionResult(
                RuleId,
                Name,
                DetectionSeverity.Error,
                context.FilePath,
                lineNumber,
                lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1,
                $"VersionOverride explicitly bypasses Central Package Management for '{packageName}' (version: {version})",
                element.ToString(),
                $"Remove VersionOverride='{version}' and update the version in Directory.Packages.props instead. If override is required, add: <!-- slopwatch-ignore: SW006 [your justification here] -->"
            );
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Analyzes Version attributes on PackageReference elements when CPM is enabled.
    /// </summary>
    private async IAsyncEnumerable<DetectionResult> AnalyzeVersionAttribute(
        DetectionContext context,
        XDocument document,
        List<(string RuleId, string Justification, int Line)> suppressions,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var packageReferences = document.Descendants()
            .Where(e => e.Name.LocalName == "PackageReference")
            .ToList();

        foreach (var element in packageReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Check for Version attribute
            var versionAttr = element.Attribute("Version");
            // Also check for nested Version element
            var versionElement = element.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "Version");

            if (versionAttr is null && versionElement is null)
                continue;

            // Skip if it also has VersionOverride (already reported above)
            if (element.Attribute("VersionOverride") is not null)
                continue;

            var lineInfo = (IXmlLineInfo)element;
            var lineNumber = lineInfo.HasLineInfo() ? lineInfo.LineNumber : 0;

            // Skip if not in diff scope
            if (lineNumber > 0 && !context.IsLineInScope(lineNumber))
                continue;

            // Check if suppressed
            if (IsSuppressedByXmlComment(suppressions, RuleId, lineNumber))
                continue;

            var packageName = element.Attribute("Include")?.Value ??
                              element.Attribute("Update")?.Value ??
                              "Unknown";
            var version = versionAttr?.Value ?? versionElement?.Value ?? "Unknown";

            // Skip if version is empty
            if (string.IsNullOrWhiteSpace(version))
                continue;

            yield return new DetectionResult(
                RuleId,
                Name,
                DetectionSeverity.Error,
                context.FilePath,
                lineNumber,
                lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1,
                $"Version attribute on PackageReference bypasses Central Package Management for '{packageName}' (version: {version})",
                element.ToString(),
                $"Remove Version='{version}' from PackageReference and add '<PackageVersion Include=\"{packageName}\" Version=\"{version}\" />' to Directory.Packages.props. If inline version is required, add: <!-- slopwatch-ignore: SW006 [your justification here] -->"
            );
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Clears the CPM detection cache. Useful for testing.
    /// </summary>
    internal static void ClearCache()
    {
        CpmCache.Clear();
    }
}
