using Slopwatch.Detection;

namespace Slopwatch.Cmd.Output;

/// <summary>
/// Interface for formatting detection results for output.
/// </summary>
public interface IOutputFormatter
{
    /// <summary>
    /// Formats detection results for output.
    /// </summary>
    /// <param name="results">The detection results to format.</param>
    /// <param name="writer">The text writer to write output to.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    Task FormatAsync(IAsyncEnumerable<DetectionResult> results, TextWriter writer, CancellationToken cancellationToken = default);
}
