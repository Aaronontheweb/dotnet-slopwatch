using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Slopwatch.Suppression;

/// <summary>
/// Represents an inline comment suppression parsed from source code.
/// </summary>
public sealed record InlineCommentSuppression
{
    /// <summary>
    /// Gets the rule ID being suppressed (e.g., "SW001").
    /// </summary>
    public string RuleId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the justification for the suppression.
    /// </summary>
    public string Justification { get; init; } = string.Empty;

    /// <summary>
    /// Gets the starting line number (1-based) for this suppression.
    /// </summary>
    public int StartLine { get; init; }

    /// <summary>
    /// Gets the ending line number (1-based) for this suppression.
    /// For single-line suppressions, this equals StartLine.
    /// For block suppressions (slopwatch-ignore-start/end), this is the end line.
    /// </summary>
    public int EndLine { get; init; }

    /// <summary>
    /// Gets whether this is a block suppression (slopwatch-ignore-start/end).
    /// </summary>
    public bool IsBlock { get; init; }

    /// <summary>
    /// Checks if a given line number is covered by this suppression.
    /// </summary>
    /// <param name="lineNumber">The 1-based line number to check.</param>
    /// <returns>True if the line is within the suppression range.</returns>
    public bool CoversLine(int lineNumber)
    {
        return lineNumber >= StartLine && lineNumber <= EndLine;
    }
}

/// <summary>
/// Parser for inline comment suppressions in source code.
/// </summary>
public static class InlineCommentSuppressionParser
{
    // Matches: // slopwatch-ignore: SW001 justification text here
    private static readonly Regex SingleLinePattern = new(
        @"^\s*//\s*slopwatch-ignore\s*:\s*(SW\d{3})\s+(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Matches: // slopwatch-ignore-start: SW001 justification text here
    private static readonly Regex BlockStartPattern = new(
        @"^\s*//\s*slopwatch-ignore-start\s*:\s*(SW\d{3})\s+(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Matches: // slopwatch-ignore-end
    private static readonly Regex BlockEndPattern = new(
        @"^\s*//\s*slopwatch-ignore-end\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const int MinJustificationLength = 20;

    /// <summary>
    /// Parses inline comment suppressions from a syntax tree.
    /// </summary>
    /// <param name="syntaxTree">The syntax tree to parse.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of parsed inline comment suppressions.</returns>
    public static async Task<List<InlineCommentSuppression>> ParseAsync(
        SyntaxTree syntaxTree,
        CancellationToken cancellationToken = default)
    {
        var root = await syntaxTree.GetRootAsync(cancellationToken);
        var suppressions = new List<InlineCommentSuppression>();
        var lines = syntaxTree.GetText().Lines;

        // Track active block suppressions
        var activeBlocks = new Stack<(string RuleId, string Justification, int StartLine)>();

        // Process all trivia in document order
        var allTrivia = root.DescendantTrivia(descendIntoTrivia: true).ToList();

        foreach (var trivia in allTrivia)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!trivia.IsKind(SyntaxKind.SingleLineCommentTrivia))
                continue;

            var commentText = trivia.ToString();
            var location = trivia.GetLocation().GetLineSpan();
            var lineNumber = location.StartLinePosition.Line + 1;

            // Check for single-line suppression
            var singleLineMatch = SingleLinePattern.Match(commentText);
            if (singleLineMatch.Success)
            {
                var ruleId = singleLineMatch.Groups[1].Value;
                var justification = singleLineMatch.Groups[2].Value.Trim();

                if (justification.Length >= MinJustificationLength)
                {
                    suppressions.Add(new InlineCommentSuppression
                    {
                        RuleId = ruleId,
                        Justification = justification,
                        StartLine = lineNumber,
                        EndLine = lineNumber + 1, // Covers the line immediately following the comment
                        IsBlock = false
                    });
                }
                continue;
            }

            // Check for block start
            var blockStartMatch = BlockStartPattern.Match(commentText);
            if (blockStartMatch.Success)
            {
                var ruleId = blockStartMatch.Groups[1].Value;
                var justification = blockStartMatch.Groups[2].Value.Trim();

                if (justification.Length >= MinJustificationLength)
                {
                    activeBlocks.Push((ruleId, justification, lineNumber));
                }
                continue;
            }

            // Check for block end
            var blockEndMatch = BlockEndPattern.Match(commentText);
            if (blockEndMatch.Success && activeBlocks.Count > 0)
            {
                var (ruleId, justification, startLine) = activeBlocks.Pop();

                suppressions.Add(new InlineCommentSuppression
                {
                    RuleId = ruleId,
                    Justification = justification,
                    StartLine = startLine,
                    EndLine = lineNumber,
                    IsBlock = true
                });
            }
        }

        return suppressions;
    }

    /// <summary>
    /// Parses inline comment suppressions from source code text.
    /// </summary>
    /// <param name="sourceText">The source code text.</param>
    /// <returns>A list of parsed inline comment suppressions.</returns>
    public static List<InlineCommentSuppression> Parse(string sourceText)
    {
        var suppressions = new List<InlineCommentSuppression>();
        var lines = sourceText.Split('\n');

        // Track active block suppressions
        var activeBlocks = new Stack<(string RuleId, string Justification, int StartLine)>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineNumber = i + 1;

            // Check for single-line suppression
            var singleLineMatch = SingleLinePattern.Match(line);
            if (singleLineMatch.Success)
            {
                var ruleId = singleLineMatch.Groups[1].Value;
                var justification = singleLineMatch.Groups[2].Value.Trim();

                if (justification.Length >= MinJustificationLength)
                {
                    suppressions.Add(new InlineCommentSuppression
                    {
                        RuleId = ruleId,
                        Justification = justification,
                        StartLine = lineNumber,
                        EndLine = lineNumber + 1, // Covers the line immediately following the comment
                        IsBlock = false
                    });
                }
                continue;
            }

            // Check for block start
            var blockStartMatch = BlockStartPattern.Match(line);
            if (blockStartMatch.Success)
            {
                var ruleId = blockStartMatch.Groups[1].Value;
                var justification = blockStartMatch.Groups[2].Value.Trim();

                if (justification.Length >= MinJustificationLength)
                {
                    activeBlocks.Push((ruleId, justification, lineNumber));
                }
                continue;
            }

            // Check for block end
            var blockEndMatch = BlockEndPattern.Match(line);
            if (blockEndMatch.Success && activeBlocks.Count > 0)
            {
                var (ruleId, justification, startLine) = activeBlocks.Pop();

                suppressions.Add(new InlineCommentSuppression
                {
                    RuleId = ruleId,
                    Justification = justification,
                    StartLine = startLine,
                    EndLine = lineNumber,
                    IsBlock = true
                });
            }
        }

        return suppressions;
    }

    /// <summary>
    /// Checks if a specific line and rule ID combination is suppressed by inline comments.
    /// </summary>
    /// <param name="suppressions">The list of parsed suppressions.</param>
    /// <param name="ruleId">The rule ID to check.</param>
    /// <param name="lineNumber">The 1-based line number to check.</param>
    /// <returns>True if the line is suppressed for the given rule.</returns>
    public static bool IsSuppressed(
        IEnumerable<InlineCommentSuppression> suppressions,
        string ruleId,
        int lineNumber)
    {
        return suppressions.Any(s =>
            s.RuleId.Equals(ruleId, StringComparison.OrdinalIgnoreCase) &&
            s.CoversLine(lineNumber));
    }
}
