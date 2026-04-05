using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Slopwatch.Suppression;

namespace Slopwatch.Detection.Rules;

/// <summary>
/// Detects disabled tests that may indicate incomplete work or test failures being hidden.
/// Rule ID: SW001
/// </summary>
/// <remarks>
/// This rule identifies test methods that have been disabled through:
/// <list type="bullet">
/// <item><description>[Fact(Skip = "...")] or [Theory(Skip = "...")] (xUnit)</description></item>
/// <item><description>[Ignore] or [IgnoreAttribute] (NUnit)</description></item>
/// <item><description>[TestMethod, Ignore] (MSTest)</description></item>
/// <item><description>#if false around test methods</description></item>
/// </list>
///
/// Disabled tests often indicate:
/// <list type="bullet">
/// <item><description>Test failures being hidden rather than fixed</description></item>
/// <item><description>Incomplete test implementation</description></item>
/// <item><description>Flaky tests that need investigation</description></item>
/// <item><description>LLM-generated tests that don't actually work</description></item>
/// </list>
///
/// This rule can be suppressed with <see cref="SlopwatchSuppressAttribute"/> when there's
/// a legitimate reason for disabling a test (e.g., platform-specific tests, external dependencies).
/// </remarks>
public sealed class DisabledTestRule : IDetectionRule
{
    /// <inheritdoc />
    public string RuleId => "SW001";

    /// <inheritdoc />
    public string Name => "Disabled Test Detection";

    /// <inheritdoc />
    public string Description =>
        "Detects disabled tests that may indicate incomplete work or test failures being hidden. " +
        "Disabled tests should have a valid suppression with justification or be fixed/removed.";

    /// <inheritdoc />
    public DetectionSeverity DefaultSeverity => DetectionSeverity.Error;

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
        var semanticModel = CSharpCompilation.Create("temp")
            .AddSyntaxTrees(context.SyntaxTree)
            .GetSemanticModel(context.SyntaxTree);

        // Create suppression checker
        var suppressionChecker = await SuppressionChecker.CreateAsync(context, cancellationToken);

        // Find all method declarations
        var methods = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>();

        foreach (var method in methods)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Check if method has test attributes
            if (!HasTestAttributes(method))
                continue;

            // Check if method is inside #if false
            if (IsInsideIfFalseDirective(method))
            {
                var location = method.GetLocation().GetLineSpan();
                var lineNumber = location.StartLinePosition.Line + 1;

                // Skip if line is not in scope or has valid suppression
                if (!context.IsLineInScope(lineNumber))
                    continue;

                if (suppressionChecker.IsSuppressed(context, method, RuleId, lineNumber))
                    continue;

                yield return new DetectionResult(
                    RuleId,
                    Name,
                    DefaultSeverity,
                    context.FilePath,
                    lineNumber,
                    location.StartLinePosition.Character + 1,
                    $"Test method '{method.Identifier.Text}' is disabled with #if false directive",
                    GetMethodSnippet(method),
                    "Remove the #if false directive and fix the test so it passes. If the test must be disabled, use [SlopwatchSuppress(\"SW001\", \"reason with 20+ chars\")]"
                );
                continue;
            }

            // Check for disabled test attributes
            var disabledAttribute = GetDisabledTestAttribute(method);
            if (disabledAttribute is not null)
            {
                var location = disabledAttribute.GetLocation().GetLineSpan();
                var lineNumber = location.StartLinePosition.Line + 1;

                // Skip if line is not in scope or has valid suppression
                if (!context.IsLineInScope(lineNumber))
                    continue;

                if (suppressionChecker.IsSuppressed(context, method, RuleId, lineNumber))
                    continue;

                var skipReason = GetSkipReason(disabledAttribute);
                var message = string.IsNullOrWhiteSpace(skipReason)
                    ? $"Test method '{method.Identifier.Text}' is disabled"
                    : $"Test method '{method.Identifier.Text}' is disabled: {skipReason}";

                yield return new DetectionResult(
                    RuleId,
                    Name,
                    DefaultSeverity,
                    context.FilePath,
                    lineNumber,
                    location.StartLinePosition.Character + 1,
                    message,
                    disabledAttribute.ToString(),
                    "Remove the Skip/Ignore attribute and fix the test so it passes. If the test must be disabled, use [SlopwatchSuppress(\"SW001\", \"reason with 20+ chars\")]"
                );
            }
        }
    }

    /// <summary>
    /// Checks if a method has test attributes (Fact, Theory, Test, TestMethod, etc.)
    /// </summary>
    private static bool HasTestAttributes(MethodDeclarationSyntax method)
    {
        var attributeLists = method.AttributeLists;
        foreach (var attributeList in attributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var attributeName = attribute.Name.ToString();
                if (attributeName.Contains("Fact") ||
                    attributeName.Contains("Theory") ||
                    attributeName.Contains("Test") ||
                    attributeName.Contains("TestMethod") ||
                    attributeName.Contains("TestCase"))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Gets the disabled test attribute if present (Skip parameter or Ignore attribute)
    /// </summary>
    private static AttributeSyntax? GetDisabledTestAttribute(MethodDeclarationSyntax method)
    {
        var attributeLists = method.AttributeLists;
        foreach (var attributeList in attributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var attributeName = attribute.Name.ToString();

                // Check for Ignore attribute (NUnit/MSTest)
                if (attributeName.Contains("Ignore"))
                {
                    return attribute;
                }

                // Check for Skip parameter in xUnit attributes
                if ((attributeName.Contains("Fact") || attributeName.Contains("Theory")) &&
                    attribute.ArgumentList is not null)
                {
                    foreach (var arg in attribute.ArgumentList.Arguments)
                    {
                        if (arg.NameEquals?.Name.ToString() == "Skip")
                        {
                            return attribute;
                        }
                    }
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Extracts the skip/ignore reason from the attribute
    /// </summary>
    private static string? GetSkipReason(AttributeSyntax attribute)
    {
        if (attribute.ArgumentList is null)
            return null;

        foreach (var arg in attribute.ArgumentList.Arguments)
        {
            // xUnit: Skip = "reason"
            if (arg.NameEquals?.Name.ToString() == "Skip")
            {
                return arg.Expression.ToString().Trim('"');
            }
            // NUnit/MSTest: [Ignore("reason")]
            if (arg.NameEquals is null && arg.Expression is LiteralExpressionSyntax literal)
            {
                return literal.Token.ValueText;
            }
        }
        return null;
    }

    /// <summary>
    /// Checks if a method is inside an #if false directive
    /// </summary>
    private static bool IsInsideIfFalseDirective(MethodDeclarationSyntax method)
    {
        var trivia = method.GetLeadingTrivia();
        var allTrivia = method.SyntaxTree.GetRoot().DescendantTrivia();

        // Find all #if directives before this method
        var methodPosition = method.SpanStart;
        var activeIf = false;

        foreach (var triviaItem in allTrivia)
        {
            if (triviaItem.SpanStart > methodPosition)
                break;

            if (triviaItem.IsKind(SyntaxKind.IfDirectiveTrivia))
            {
                var directive = triviaItem.GetStructure() as IfDirectiveTriviaSyntax;
                if (directive is not null)
                {
                    var condition = directive.Condition.ToString().Trim();
                    if (condition == "false" || condition == "FALSE")
                    {
                        activeIf = true;
                    }
                }
            }
            else if (triviaItem.IsKind(SyntaxKind.EndIfDirectiveTrivia))
            {
                if (triviaItem.SpanStart < methodPosition)
                {
                    activeIf = false;
                }
            }
        }

        return activeIf;
    }

    /// <summary>
    /// Gets a snippet of the method for context
    /// </summary>
    private static string GetMethodSnippet(MethodDeclarationSyntax method)
    {
        var lines = method.ToString().Split('\n');
        if (lines.Length <= 3)
            return method.ToString();

        // Return first few lines with ellipsis
        return string.Join('\n', lines.Take(3)) + "\n    // ...";
    }
}
