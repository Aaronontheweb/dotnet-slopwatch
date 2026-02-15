using Microsoft.AspNetCore.Razor.Language;

namespace Slopwatch.Analysis;

/// <summary>
/// Extracts generated C# code from .razor files using the official Razor compiler.
/// The generated code includes #line directives that map back to the original .razor source.
/// </summary>
internal static class RazorCodeExtractor
{
    private static readonly RazorProjectEngine Engine = RazorProjectEngine.Create(
        RazorConfiguration.Default,
        RazorProjectFileSystem.Create("/"));

    /// <summary>
    /// Processes a .razor file and returns the generated C# code.
    /// Returns null if the file cannot be processed.
    /// </summary>
    /// <param name="content">The .razor file content.</param>
    /// <param name="filePath">The file path (used for source mapping).</param>
    /// <returns>The generated C# code, or null if processing failed.</returns>
    public static string? ExtractGeneratedCSharp(string content, string filePath)
    {
        try
        {
            var source = RazorSourceDocument.Create(content, filePath);
            var codeDocument = Engine.Process(
                source,
                FileKinds.Component,
                importSources: null,
                tagHelpers: null);

            var csharpDoc = codeDocument.GetCSharpDocument();
            return csharpDoc?.GeneratedCode;
        }
        catch
        {
            return null;
        }
    }
}
