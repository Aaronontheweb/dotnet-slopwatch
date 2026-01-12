using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Slopwatch.Suppression;

namespace Slopwatch.Detection.Rules;

/// <summary>
/// Detects compiler warning suppression that may hide real issues.
/// Rule ID: SW002
/// </summary>
/// <remarks>
/// This rule identifies code that suppresses compiler warnings through:
/// <list type="bullet">
/// <item><description>#pragma warning disable without matching restore in same scope</description></item>
/// <item><description>[SuppressMessage(...)] attributes</description></item>
/// </list>
///
/// Warning suppression often indicates:
/// <list type="bullet">
/// <item><description>LLM-generated code that doesn't compile cleanly</description></item>
/// <item><description>Quick fixes that hide rather than solve problems</description></item>
/// <item><description>Technical debt accumulation</description></item>
/// <item><description>Security or quality issues being ignored</description></item>
/// </list>
///
/// This rule can be suppressed with <see cref="SlopwatchSuppressAttribute"/> when there's
/// a legitimate reason for warning suppression (e.g., generated code, third-party library quirks).
/// </remarks>
public sealed class WarningSuppressRule : IDetectionRule
{
    /// <inheritdoc />
    public string RuleId => "SW002";

    /// <inheritdoc />
    public string Name => "Warning Suppression Detection";

    /// <inheritdoc />
    public string Description =>
        "Detects compiler warning suppression through #pragma directives or SuppressMessage attributes. " +
        "Warning suppression should have a valid justification or the underlying issue should be fixed.";

    /// <inheritdoc />
    public DetectionSeverity DefaultSeverity => DetectionSeverity.Warning;

    /// <inheritdoc />
    public IReadOnlyList<string> ApplicableFilePatterns => new[] { "*.cs" };

    /// <inheritdoc />
    public async IAsyncEnumerable<DetectionResult> AnalyzeAsync(
        DetectionContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (context.SyntaxTree is null)
            yield break;

        var root = await context.SyntaxTree.GetRootAsync(cancellationToken);

        // Create suppression checker
        var projectRoot = GetProjectRoot(context.FilePath);
        var suppressionChecker = await SuppressionChecker.CreateAsync(context, projectRoot, cancellationToken);

        // Check for #pragma warning disable directives
        await foreach (var result in AnalyzePragmaDirectives(context, root, suppressionChecker, cancellationToken))
        {
            yield return result;
        }

        // Check for SuppressMessage attributes
        await foreach (var result in AnalyzeSuppressMessageAttributes(context, root, suppressionChecker, cancellationToken))
        {
            yield return result;
        }
    }

    /// <summary>
    /// Analyzes #pragma warning disable directives
    /// </summary>
    private async IAsyncEnumerable<DetectionResult> AnalyzePragmaDirectives(
        DetectionContext context,
        SyntaxNode root,
        SuppressionChecker suppressionChecker,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var allTrivia = root.DescendantTrivia().ToList();
        var disableDirectives = new Dictionary<int, (PragmaWarningDirectiveTriviaSyntax Directive, int LineNumber)>();

        foreach (var trivia in allTrivia)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (trivia.IsKind(SyntaxKind.PragmaWarningDirectiveTrivia))
            {
                var directive = trivia.GetStructure() as PragmaWarningDirectiveTriviaSyntax;
                if (directive is null)
                    continue;

                var location = directive.GetLocation().GetLineSpan();
                var lineNumber = location.StartLinePosition.Line + 1;

                // Skip if line is not in scope
                if (!context.IsLineInScope(lineNumber))
                    continue;

                if (directive.DisableOrRestoreKeyword.IsKind(SyntaxKind.DisableKeyword))
                {
                    // Track disable directives
                    disableDirectives[directive.SpanStart] = (directive, lineNumber);
                }
                else if (directive.DisableOrRestoreKeyword.IsKind(SyntaxKind.RestoreKeyword))
                {
                    // Match restore with disable
                    // Find the most recent unmatched disable before this restore
                    var matchedDisables = disableDirectives
                        .Where(kvp => kvp.Key < directive.SpanStart)
                        .OrderByDescending(kvp => kvp.Key)
                        .ToList();

                    if (matchedDisables.Any())
                    {
                        // Remove the matched disable
                        disableDirectives.Remove(matchedDisables.First().Key);
                    }
                }
            }
        }

        // Report unmatched disable directives
        foreach (var (directive, lineNumber) in disableDirectives.Values)
        {
            // Check if the parent node has a valid suppression
            var parentNode = FindParentDeclaration(directive.Parent);
            if (suppressionChecker.IsSuppressed(context, parentNode, RuleId, lineNumber))
                continue;

            var warnings = GetWarningCodes(directive);
            var warningText = warnings.Any()
                ? $"warnings {string.Join(", ", warnings)}"
                : "all warnings";

            var location = directive.GetLocation().GetLineSpan();

            yield return new DetectionResult(
                RuleId,
                Name,
                DefaultSeverity,
                context.FilePath,
                lineNumber,
                location.StartLinePosition.Character + 1,
                $"#pragma warning disable for {warningText} without matching restore in same scope",
                directive.ToString(),
                $"Fix the code causing {warningText} and remove the #pragma, or add matching #pragma warning restore. If suppression is needed, use [SlopwatchSuppress(\"SW002\", \"reason with 20+ chars\")]"
            );
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Analyzes SuppressMessage attributes
    /// </summary>
    private async IAsyncEnumerable<DetectionResult> AnalyzeSuppressMessageAttributes(
        DetectionContext context,
        SyntaxNode root,
        SuppressionChecker suppressionChecker,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var attributeLists = root.DescendantNodes()
            .SelectMany(node => node switch
            {
                BaseTypeDeclarationSyntax typeDecl => typeDecl.AttributeLists,
                MethodDeclarationSyntax methodDecl => methodDecl.AttributeLists,
                PropertyDeclarationSyntax propertyDecl => propertyDecl.AttributeLists,
                FieldDeclarationSyntax fieldDecl => fieldDecl.AttributeLists,
                _ => Enumerable.Empty<AttributeListSyntax>()
            });

        foreach (var attributeList in attributeLists)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var attribute in attributeList.Attributes)
            {
                var attributeName = attribute.Name.ToString();
                if (!attributeName.Contains("SuppressMessage"))
                    continue;

                var location = attribute.GetLocation().GetLineSpan();
                var lineNumber = location.StartLinePosition.Line + 1;

                // Skip if line is not in scope
                if (!context.IsLineInScope(lineNumber))
                    continue;

                // Find parent declaration
                var parentNode = FindParentDeclaration(attribute.Parent);
                if (suppressionChecker.IsSuppressed(context, parentNode, RuleId, lineNumber))
                    continue;

                var suppressionInfo = GetSuppressionInfo(attribute);

                yield return new DetectionResult(
                    RuleId,
                    Name,
                    DefaultSeverity,
                    context.FilePath,
                    lineNumber,
                    location.StartLinePosition.Character + 1,
                    $"SuppressMessage attribute suppressing {suppressionInfo}",
                    attribute.ToString(),
                    $"Fix the code causing {suppressionInfo} and remove the SuppressMessage attribute. If suppression is needed, use [SlopwatchSuppress(\"SW002\", \"reason with 20+ chars\")]"
                );
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Extracts warning codes from a pragma directive
    /// </summary>
    private static IEnumerable<string> GetWarningCodes(PragmaWarningDirectiveTriviaSyntax directive)
    {
        return directive.ErrorCodes
            .Select(code => code.ToString())
            .Where(code => !string.IsNullOrWhiteSpace(code));
    }

    /// <summary>
    /// Extracts suppression information from a SuppressMessage attribute
    /// </summary>
    private static string GetSuppressionInfo(AttributeSyntax attribute)
    {
        if (attribute.ArgumentList is null || !attribute.ArgumentList.Arguments.Any())
            return "unknown warning";

        var args = attribute.ArgumentList.Arguments;
        if (args.Count >= 2)
        {
            var category = args[0].Expression.ToString().Trim('"');
            var checkId = args[1].Expression.ToString().Trim('"');
            return $"{category}:{checkId}";
        }

        return args[0].Expression.ToString().Trim('"');
    }

    /// <summary>
    /// Finds the parent declaration node (class, method, property, etc.)
    /// </summary>
    private static SyntaxNode? FindParentDeclaration(SyntaxNode? node)
    {
        while (node is not null)
        {
            if (node is BaseTypeDeclarationSyntax or
                MethodDeclarationSyntax or
                PropertyDeclarationSyntax or
                FieldDeclarationSyntax)
            {
                return node;
            }
            node = node.Parent;
        }
        return null;
    }

    /// <summary>
    /// Gets the project root directory from a file path.
    /// </summary>
    private static string GetProjectRoot(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(directory))
        {
            return Directory.GetCurrentDirectory();
        }

        while (directory != null)
        {
            try
            {
                // Look for common project root indicators
                if (Directory.Exists(Path.Combine(directory, ".git")) ||
                    Directory.Exists(Path.Combine(directory, ".slopwatch")) ||
                    Directory.GetFiles(directory, "*.sln").Any())
                {
                    return directory;
                }
            }
            catch
            {
                // If we can't access the directory, continue up
            }

            var parent = Path.GetDirectoryName(directory);
            if (parent == directory) // Reached root
                break;
            directory = parent;
        }

        // Fallback to the file's directory or current directory
        return Path.GetDirectoryName(filePath) ?? Directory.GetCurrentDirectory();
    }
}
