using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Slopwatch.Detection;

/// <summary>
/// Provides context information for detection rules to analyze a file.
/// </summary>
/// <param name="FilePath">The full path to the file being analyzed.</param>
/// <param name="FileName">The name of the file (without directory path).</param>
/// <param name="Content">The full content of the file as a string.</param>
/// <param name="SyntaxTree">
/// The Roslyn syntax tree for .cs files. Will be null for non-C# files.
/// </param>
/// <param name="IsTestFile">
/// Indicates whether this file is identified as a test file
/// (e.g., located in a test project or contains test attributes).
/// </param>
/// <param name="AddedLines">
/// Optional set of 1-based line numbers that were added in the current diff.
/// When provided, rules can focus analysis on newly added code only.
/// </param>
/// <param name="ModifiedLines">
/// Optional set of 1-based line numbers that were modified in the current diff.
/// When provided, rules can focus analysis on changed code only.
/// </param>
public sealed record DetectionContext(
    string FilePath,
    string FileName,
    string Content,
    SyntaxTree? SyntaxTree,
    bool IsTestFile,
    IReadOnlySet<int>? AddedLines = null,
    IReadOnlySet<int>? ModifiedLines = null
)
{
    /// <summary>
    /// Determines if the given 1-based line number is within the scope of analysis
    /// based on the diff context (added or modified lines).
    /// </summary>
    /// <param name="lineNumber">The 1-based line number to check.</param>
    /// <returns>
    /// True if the line should be analyzed (either in diff scope or no diff context provided).
    /// </returns>
    public bool IsLineInScope(int lineNumber)
    {
        // If no diff context is provided, all lines are in scope
        if (AddedLines is null && ModifiedLines is null)
            return true;

        return (AddedLines?.Contains(lineNumber) ?? false)
            || (ModifiedLines?.Contains(lineNumber) ?? false);
    }

    /// <summary>
    /// Indicates whether this context has diff-aware analysis enabled.
    /// </summary>
    public bool HasDiffContext => AddedLines is not null || ModifiedLines is not null;
}
