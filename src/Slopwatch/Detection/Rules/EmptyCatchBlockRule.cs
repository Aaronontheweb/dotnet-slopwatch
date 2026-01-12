using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Slopwatch.Suppression;

namespace Slopwatch.Detection.Rules;

/// <summary>
/// Detects empty catch blocks that swallow exceptions.
/// Rule ID: SW003
/// </summary>
/// <remarks>
/// This rule identifies catch blocks that:
/// <list type="bullet">
/// <item><description>Are completely empty</description></item>
/// <item><description>Only contain comments</description></item>
/// <item><description>Only log without rethrowing or handling</description></item>
/// </list>
///
/// Empty catch blocks often indicate:
/// <list type="bullet">
/// <item><description>LLM-generated error handling without proper implementation</description></item>
/// <item><description>Silent failure that hides real problems</description></item>
/// <item><description>Lazy exception handling that should be improved</description></item>
/// <item><description>Security issues being hidden</description></item>
/// </list>
///
/// Note: This rule does NOT flag catch blocks that catch broad exception types (like Exception)
/// as long as they contain actual handling code. Catching Exception is a legitimate pattern
/// for top-level handlers, plugin systems, and graceful degradation scenarios.
///
/// This rule can be suppressed with <see cref="SlopwatchSuppressAttribute"/> when there's
/// a legitimate reason for an empty catch block (e.g., optional configuration files, benign exceptions).
/// </remarks>
public sealed class EmptyCatchBlockRule : IDetectionRule
{
    /// <inheritdoc />
    public string RuleId => "SW003";

    /// <inheritdoc />
    public string Name => "Empty Catch Block Detection";

    /// <inheritdoc />
    public string Description =>
        "Detects empty catch blocks that swallow exceptions without proper handling. " +
        "Exceptions should be handled appropriately or allowed to propagate.";

    /// <inheritdoc />
    public DetectionSeverity DefaultSeverity => DetectionSeverity.Error;

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

        // Find all catch clauses
        var catchClauses = root.DescendantNodes()
            .OfType<CatchClauseSyntax>();

        foreach (var catchClause in catchClauses)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var location = catchClause.GetLocation().GetLineSpan();
            var lineNumber = location.StartLinePosition.Line + 1;

            // Skip if line is not in scope
            if (!context.IsLineInScope(lineNumber))
                continue;

            // Check if parent has valid suppression
            var parentMethod = FindParentMethod(catchClause);
            if (suppressionChecker.IsSuppressed(context, parentMethod, RuleId, lineNumber))
                continue;

            // Check if catch block is empty or only has comments
            if (IsEmptyOrCommentOnly(catchClause))
            {
                yield return new DetectionResult(
                    RuleId,
                    Name,
                    DefaultSeverity,
                    context.FilePath,
                    lineNumber,
                    location.StartLinePosition.Character + 1,
                    "Empty catch block swallows exceptions without handling",
                    GetCatchSnippet(catchClause),
                    "Add proper exception handling (log and rethrow, or handle the error condition). If the empty catch is intentional, use [SlopwatchSuppress(\"SW003\", \"reason with 20+ chars\")]"
                );
                continue;
            }

            // Check if catch block only logs without rethrowing
            if (IsLoggingOnlyWithoutRethrow(catchClause))
            {
                yield return new DetectionResult(
                    RuleId,
                    Name,
                    DetectionSeverity.Warning, // Lower severity for logging-only
                    context.FilePath,
                    lineNumber,
                    location.StartLinePosition.Character + 1,
                    "Catch block only logs exception without rethrowing or handling",
                    GetCatchSnippet(catchClause),
                    "Add 'throw;' after logging to rethrow, or add actual error handling. If logging-only is intentional, use [SlopwatchSuppress(\"SW003\", \"reason with 20+ chars\")]"
                );
            }
        }
    }

    /// <summary>
    /// Checks if a catch block is empty or only contains comments
    /// </summary>
    private static bool IsEmptyOrCommentOnly(CatchClauseSyntax catchClause)
    {
        if (catchClause.Block is null)
            return true;

        var statements = catchClause.Block.Statements;
        if (statements.Count == 0)
            return true;

        // Check if all statements are comments or empty
        foreach (var statement in statements)
        {
            // If there's any actual statement, it's not empty
            if (!string.IsNullOrWhiteSpace(statement.ToString().Trim()))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Checks if a catch block only logs without rethrowing
    /// </summary>
    private static bool IsLoggingOnlyWithoutRethrow(CatchClauseSyntax catchClause)
    {
        if (catchClause.Block is null || catchClause.Block.Statements.Count == 0)
            return false;

        var statements = catchClause.Block.Statements;
        var hasLogging = false;
        var hasRethrow = false;
        var hasOtherStatements = false;

        foreach (var statement in statements)
        {
            var statementText = statement.ToString().ToLowerInvariant();

            // Check for throw or throw ex
            if (statement is ThrowStatementSyntax)
            {
                hasRethrow = true;
            }
            // Check for logging patterns
            else if (statementText.Contains("log.") ||
                     statementText.Contains("logger.") ||
                     statementText.Contains("console.write") ||
                     statementText.Contains("debug.write") ||
                     statementText.Contains("trace.write"))
            {
                hasLogging = true;
            }
            // Any other statement means there's actual handling
            else if (!string.IsNullOrWhiteSpace(statement.ToString().Trim()))
            {
                hasOtherStatements = true;
            }
        }

        // Only flag if it's logging only without rethrow or other handling
        return hasLogging && !hasRethrow && !hasOtherStatements;
    }

    /// <summary>
    /// Finds the parent method declaration
    /// </summary>
    private static MethodDeclarationSyntax? FindParentMethod(SyntaxNode? node)
    {
        while (node is not null)
        {
            if (node is MethodDeclarationSyntax method)
                return method;
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

    /// <summary>
    /// Gets a snippet of the catch block for context
    /// </summary>
    private static string GetCatchSnippet(CatchClauseSyntax catchClause)
    {
        var text = catchClause.ToString();
        var lines = text.Split('\n');
        if (lines.Length <= 5)
            return text;

        // Return first few lines with ellipsis
        return string.Join('\n', lines.Take(5)) + "\n    // ...";
    }
}
