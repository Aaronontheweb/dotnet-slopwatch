using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.FileSystemGlobbing;
using Slopwatch.Configuration;
using Slopwatch.Detection;

namespace Slopwatch.Suppression;

/// <summary>
/// Unified checker for all suppression mechanisms (attributes, inline comments, config file).
/// </summary>
public sealed class SuppressionChecker
{
    private readonly SlopwatchConfig? _config;
    private readonly List<InlineCommentSuppression> _inlineSuppressions;
    private readonly string _projectRoot;

    /// <summary>
    /// Initializes a new instance of <see cref="SuppressionChecker"/>.
    /// </summary>
    /// <param name="context">The detection context.</param>
    /// <param name="projectRoot">The root directory of the project (for config file loading).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<SuppressionChecker> CreateAsync(
        DetectionContext context,
        string projectRoot,
        CancellationToken cancellationToken = default)
    {
        var config = await LoadConfigAsync(projectRoot, cancellationToken);

        var inlineSuppressions = context.SyntaxTree != null
            ? await InlineCommentSuppressionParser.ParseAsync(context.SyntaxTree, cancellationToken)
            : new List<InlineCommentSuppression>();

        return new SuppressionChecker(config, inlineSuppressions, projectRoot);
    }

    private SuppressionChecker(
        SlopwatchConfig? config,
        List<InlineCommentSuppression> inlineSuppressions,
        string projectRoot)
    {
        _config = config;
        _inlineSuppressions = inlineSuppressions;
        _projectRoot = projectRoot;
    }

    /// <summary>
    /// Checks if a syntax node has a valid SlopwatchSuppress attribute.
    /// </summary>
    /// <param name="node">The syntax node to check.</param>
    /// <param name="ruleId">The rule ID to check for.</param>
    /// <returns>True if the node has a valid suppression attribute.</returns>
    public bool CheckAttribute(SyntaxNode node, string ruleId)
    {
        var attributeLists = node switch
        {
            BaseTypeDeclarationSyntax typeDecl => typeDecl.AttributeLists,
            MethodDeclarationSyntax methodDecl => methodDecl.AttributeLists,
            PropertyDeclarationSyntax propertyDecl => propertyDecl.AttributeLists,
            FieldDeclarationSyntax fieldDecl => fieldDecl.AttributeLists,
            _ => Enumerable.Empty<AttributeListSyntax>()
        };

        foreach (var attributeList in attributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var attributeName = attribute.Name.ToString();
                if (attributeName.Contains("SlopwatchSuppress"))
                {
                    // Check if this suppression is for our rule
                    if (attribute.ArgumentList is not null &&
                        attribute.ArgumentList.Arguments.Count > 0)
                    {
                        var firstArg = attribute.ArgumentList.Arguments[0].Expression.ToString();
                        if (firstArg.Trim('"') == ruleId)
                        {
                            // Check if justification exists and is valid (at least 20 chars)
                            if (attribute.ArgumentList.Arguments.Count > 1)
                            {
                                var justification = attribute.ArgumentList.Arguments[1].Expression.ToString();
                                if (justification.Trim('"').Length >= 20)
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Checks if a specific line has an inline comment suppression.
    /// </summary>
    /// <param name="ruleId">The rule ID to check for.</param>
    /// <param name="lineNumber">The 1-based line number to check.</param>
    /// <returns>True if the line is suppressed by an inline comment.</returns>
    public bool CheckInlineComment(string ruleId, int lineNumber)
    {
        return InlineCommentSuppressionParser.IsSuppressed(_inlineSuppressions, ruleId, lineNumber);
    }

    /// <summary>
    /// Checks if a file path is suppressed by configuration file rules.
    /// </summary>
    /// <param name="filePath">The absolute file path to check.</param>
    /// <param name="ruleId">The rule ID to check for.</param>
    /// <returns>True if the file is suppressed by config file rules.</returns>
    public bool CheckConfigFile(string filePath, string ruleId)
    {
        if (_config == null)
            return false;

        // Check global suppressions first
        foreach (var globalSuppression in _config.GlobalSuppressions)
        {
            if (globalSuppression.RuleId.Equals(ruleId, StringComparison.OrdinalIgnoreCase))
            {
                // Check if suppression has expired
                if (!globalSuppression.IsExpired())
                {
                    return true;
                }
            }
        }

        // Check path-based suppressions
        var relativePath = GetRelativePath(_projectRoot, filePath);

        foreach (var suppression in _config.Suppressions)
        {
            if (suppression.RuleId.Equals(ruleId, StringComparison.OrdinalIgnoreCase))
            {
                // Check if suppression has expired
                if (suppression.IsExpired())
                    continue;

                // Check if file path matches the pattern
                if (MatchesPattern(relativePath, suppression.Pattern))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Unified check for whether a detection should be suppressed.
    /// Checks all suppression mechanisms in order: attribute, inline comment, config file.
    /// </summary>
    /// <param name="context">The detection context.</param>
    /// <param name="node">The syntax node being checked (for attribute suppression).</param>
    /// <param name="ruleId">The rule ID to check for.</param>
    /// <param name="lineNumber">The 1-based line number to check.</param>
    /// <returns>True if the detection should be suppressed.</returns>
    public bool IsSuppressed(DetectionContext context, SyntaxNode? node, string ruleId, int lineNumber)
    {
        // Check attribute suppression
        if (node != null && CheckAttribute(node, ruleId))
            return true;

        // Check inline comment suppression
        if (CheckInlineComment(ruleId, lineNumber))
            return true;

        // Check config file suppression
        if (CheckConfigFile(context.FilePath, ruleId))
            return true;

        return false;
    }

    /// <summary>
    /// Loads the Slopwatch configuration file from the project root.
    /// </summary>
    /// <param name="projectRoot">The root directory of the project.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The loaded configuration, or null if no config file exists.</returns>
    private static async Task<SlopwatchConfig?> LoadConfigAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        var configPath = Path.Combine(projectRoot, ".slopwatch", "config.json");

        if (!File.Exists(configPath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(configPath, cancellationToken);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };

            return JsonSerializer.Deserialize<SlopwatchConfig>(json, options);
        }
        catch
        {
            // If config file is invalid, treat as if no config exists
            return null;
        }
    }

    /// <summary>
    /// Gets the relative path from a base directory to a target path.
    /// </summary>
    private static string GetRelativePath(string basePath, string targetPath)
    {
        // Ensure both paths are absolute for reliable comparison
        var absoluteBase = Path.GetFullPath(basePath);
        var absoluteTarget = Path.GetFullPath(targetPath);

        return Path.GetRelativePath(absoluteBase, absoluteTarget);
    }

    /// <summary>
    /// Checks if a file path matches a glob pattern.
    /// </summary>
    private static bool MatchesPattern(string filePath, string pattern)
    {
        var matcher = new Matcher();
        matcher.AddInclude(pattern);

        var normalizedPath = filePath.Replace('\\', '/');
        var result = matcher.Match(normalizedPath);

        return result.HasMatches;
    }
}
