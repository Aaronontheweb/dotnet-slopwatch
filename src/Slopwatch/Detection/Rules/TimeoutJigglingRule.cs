using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Slopwatch.Suppression;

namespace Slopwatch.Detection.Rules;

/// <summary>
/// Detects suspicious delays in test files that may indicate timing-dependent tests.
/// Rule ID: SW004
/// </summary>
/// <remarks>
/// This rule identifies test code that uses delays or waits:
/// <list type="bullet">
/// <item><description>Task.Delay(...) in test files</description></item>
/// <item><description>Thread.Sleep(...) in test files</description></item>
/// <item><description>SpinWait usage in tests</description></item>
/// </list>
///
/// Delays in tests often indicate:
/// <list type="bullet">
/// <item><description>Flaky tests that depend on timing</description></item>
/// <item><description>LLM-generated tests that use delays instead of proper synchronization</description></item>
/// <item><description>Race conditions being hidden with artificial delays</description></item>
/// <item><description>Poor test design that needs refactoring</description></item>
/// </list>
///
/// This rule can be suppressed with <see cref="SlopwatchSuppressAttribute"/> when there's
/// a legitimate reason for delays in tests (e.g., testing rate limiters, timeout behavior, debouncing).
///
/// <strong>Note:</strong> This rule only applies to test files (IsTestFile = true).
/// </remarks>
public sealed class TimeoutJigglingRule : IDetectionRule
{
    /// <inheritdoc />
    public string RuleId => "SW004";

    /// <inheritdoc />
    public string Name => "Test Timeout Jiggling Detection";

    /// <inheritdoc />
    public string Description =>
        "Detects suspicious delays in test files (Task.Delay, Thread.Sleep, SpinWait) that may indicate " +
        "timing-dependent tests. Tests should use proper synchronization instead of arbitrary delays.";

    /// <inheritdoc />
    public DetectionSeverity DefaultSeverity => DetectionSeverity.Warning;

    /// <inheritdoc />
    public IReadOnlyList<string> ApplicableFilePatterns => new[] { "*.cs" };

    /// <inheritdoc />
    public async IAsyncEnumerable<DetectionResult> AnalyzeAsync(
        DetectionContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Only analyze test files
        if (!context.IsTestFile || context.SyntaxTree is null)
            yield break;

        var root = await context.SyntaxTree.GetRootAsync(cancellationToken);

        // Create suppression checker
        var projectRoot = GetProjectRoot(context.FilePath);
        var suppressionChecker = await SuppressionChecker.CreateAsync(context, projectRoot, cancellationToken);

        // Find all invocation expressions (method calls)
        var invocations = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>();

        foreach (var invocation in invocations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var location = invocation.GetLocation().GetLineSpan();
            var lineNumber = location.StartLinePosition.Line + 1;

            // Skip if line is not in scope
            if (!context.IsLineInScope(lineNumber))
                continue;

            // Check if parent method has valid suppression
            var parentMethod = FindParentMethod(invocation);
            if (suppressionChecker.IsSuppressed(context, parentMethod, RuleId, lineNumber))
                continue;

            // Check for Task.Delay
            if (IsTaskDelay(invocation))
            {
                var delayValue = GetDelayValue(invocation);
                yield return new DetectionResult(
                    RuleId,
                    Name,
                    DefaultSeverity,
                    context.FilePath,
                    lineNumber,
                    location.StartLinePosition.Character + 1,
                    $"Test uses Task.Delay({delayValue}) which may indicate a timing-dependent test",
                    invocation.ToString(),
                    "Replace Task.Delay with proper synchronization (TaskCompletionSource, SemaphoreSlim, or test framework's async helpers). If delay is intentional (testing timeouts), use [SlopwatchSuppress(\"SW004\", \"reason with 20+ chars\")]"
                );
            }
            // Check for Thread.Sleep
            else if (IsThreadSleep(invocation))
            {
                var delayValue = GetDelayValue(invocation);
                yield return new DetectionResult(
                    RuleId,
                    Name,
                    DefaultSeverity,
                    context.FilePath,
                    lineNumber,
                    location.StartLinePosition.Character + 1,
                    $"Test uses Thread.Sleep({delayValue}) which may indicate a timing-dependent test",
                    invocation.ToString(),
                    "Replace Thread.Sleep with async Task.Delay or proper synchronization primitives. If delay is intentional (testing timeouts), use [SlopwatchSuppress(\"SW004\", \"reason with 20+ chars\")]"
                );
            }
            // Check for SpinWait
            else if (IsSpinWait(invocation))
            {
                yield return new DetectionResult(
                    RuleId,
                    Name,
                    DefaultSeverity,
                    context.FilePath,
                    lineNumber,
                    location.StartLinePosition.Character + 1,
                    "Test uses SpinWait which may indicate a timing-dependent test",
                    invocation.ToString(),
                    "Replace SpinWait with proper synchronization primitives (ManualResetEventSlim, SemaphoreSlim). If SpinWait is intentional, use [SlopwatchSuppress(\"SW004\", \"reason with 20+ chars\")]"
                );
            }
        }

        // Also check for SpinWait usage as object creation
        var objectCreations = root.DescendantNodes()
            .OfType<ObjectCreationExpressionSyntax>();

        foreach (var creation in objectCreations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var typeName = creation.Type.ToString();
            if (typeName.Contains("SpinWait"))
            {
                var location = creation.GetLocation().GetLineSpan();
                var lineNumber = location.StartLinePosition.Line + 1;

                if (!context.IsLineInScope(lineNumber))
                    continue;

                var parentMethod = FindParentMethod(creation);
                if (suppressionChecker.IsSuppressed(context, parentMethod, RuleId, lineNumber))
                    continue;

                yield return new DetectionResult(
                    RuleId,
                    Name,
                    DefaultSeverity,
                    context.FilePath,
                    lineNumber,
                    location.StartLinePosition.Character + 1,
                    "Test creates SpinWait instance which may indicate a timing-dependent test",
                    creation.ToString(),
                    "Replace SpinWait with proper synchronization primitives (ManualResetEventSlim, SemaphoreSlim). If SpinWait is intentional, use [SlopwatchSuppress(\"SW004\", \"reason with 20+ chars\")]"
                );
            }
        }
    }

    /// <summary>
    /// Checks if invocation is Task.Delay
    /// </summary>
    private static bool IsTaskDelay(InvocationExpressionSyntax invocation)
    {
        var expression = invocation.Expression.ToString();
        return expression.Contains("Task.Delay") ||
               expression.EndsWith("Delay") && IsTaskContext(invocation);
    }

    /// <summary>
    /// Checks if invocation is Thread.Sleep
    /// </summary>
    private static bool IsThreadSleep(InvocationExpressionSyntax invocation)
    {
        var expression = invocation.Expression.ToString();
        return expression.Contains("Thread.Sleep") ||
               expression.EndsWith("Sleep") && IsThreadContext(invocation);
    }

    /// <summary>
    /// Checks if invocation is SpinWait related
    /// </summary>
    private static bool IsSpinWait(InvocationExpressionSyntax invocation)
    {
        var expression = invocation.Expression.ToString();
        return expression.Contains("SpinWait");
    }

    /// <summary>
    /// Checks if the invocation is in a Task context
    /// </summary>
    private static bool IsTaskContext(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var targetType = memberAccess.Expression.ToString();
            return targetType == "Task" || targetType == "System.Threading.Tasks.Task";
        }
        return false;
    }

    /// <summary>
    /// Checks if the invocation is in a Thread context
    /// </summary>
    private static bool IsThreadContext(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var targetType = memberAccess.Expression.ToString();
            return targetType == "Thread" || targetType == "System.Threading.Thread";
        }
        return false;
    }

    /// <summary>
    /// Extracts the delay value from the invocation arguments
    /// </summary>
    private static string GetDelayValue(InvocationExpressionSyntax invocation)
    {
        if (invocation.ArgumentList?.Arguments.Count > 0)
        {
            var firstArg = invocation.ArgumentList.Arguments[0];
            return firstArg.Expression.ToString();
        }
        return "?";
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
                    Directory.GetFiles(directory, "*.sln").Any() ||
                    Directory.GetFiles(directory, "*.slnx").Any())
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
