using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Slopwatch.Detection;

namespace Slopwatch.Analysis;

/// <summary>
/// Provides parallel file analysis using Akka.Streams.
/// </summary>
/// <remarks>
/// Creates a temporary ActorSystem with logging suppressed for efficient parallel processing
/// of files during analysis. This is particularly useful for CI/CD pipelines where
/// many files need to be analyzed quickly.
/// </remarks>
public static class ParallelAnalyzer
{
    // HOCON configuration to completely suppress Akka.NET logging
    private const string AkkaConfig = @"
        akka {
            loglevel = OFF
            stdout-loglevel = OFF
            log-dead-letters = off
            log-dead-letters-during-shutdown = off
            loggers = []
            actor {
                default-dispatcher {
                    throughput = 100
                }
            }
        }";

    /// <summary>
    /// Analyzes multiple files in parallel using Akka.Streams.
    /// </summary>
    /// <param name="analyzer">The file analyzer to use for individual file analysis.</param>
    /// <param name="filePaths">The paths to the files to analyze.</param>
    /// <param name="parallelism">The degree of parallelism (default: number of processors).</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>An async enumerable of detection results.</returns>
    public static async IAsyncEnumerable<DetectionResult> AnalyzeFilesParallelAsync(
        FileAnalyzer analyzer,
        IEnumerable<string> filePaths,
        int parallelism = 0,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(analyzer);
        ArgumentNullException.ThrowIfNull(filePaths);

        // Default to processor count if not specified
        if (parallelism <= 0)
        {
            parallelism = Environment.ProcessorCount;
        }

        // Create a source from the file paths
        var fileList = filePaths.ToList();

        if (fileList.Count == 0)
        {
            yield break;
        }

        // Create a temporary ActorSystem for this analysis run
        var actorSystem = ActorSystem.Create("slopwatch", AkkaConfig);
        try
        {
            var materializer = actorSystem.Materializer();

            // Use a channel to bridge Akka.Streams to IAsyncEnumerable
            var channel = System.Threading.Channels.Channel.CreateUnbounded<DetectionResult>();

            // Run the stream in the background
            var streamTask = Source.From(fileList)
                .SelectAsyncUnordered(parallelism, async filePath =>
                {
                    var results = new List<DetectionResult>();
                    try
                    {
                        await foreach (var result in analyzer.AnalyzeFileAsync(filePath, cancellationToken))
                        {
                            results.Add(result);
                        }
                    }
                    catch (FileNotFoundException)
                    {
                        // File was deleted between enumeration and analysis - return empty results
                        return results;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception)
                    {
                        // File failed to parse/analyze (e.g., invalid syntax) - return empty results
                        // We continue processing other files rather than failing the entire analysis
                        return results;
                    }
                    return results;
                })
                .SelectMany(results => results)
                .RunForeach(result => channel.Writer.TryWrite(result), materializer)
                .ContinueWith(_ => channel.Writer.Complete(), cancellationToken);

            // Yield results as they become available
            await foreach (var result in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return result;
            }

            // Ensure the stream task completes
            await streamTask;
        }
        finally
        {
            // Dispose the ActorSystem when done
            await actorSystem.Terminate();
        }
    }
}
