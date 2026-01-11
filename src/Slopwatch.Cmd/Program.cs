using CommandLine;
using Slopwatch.Cmd.Commands;

namespace Slopwatch.Cmd;

/// <summary>
/// Main entry point for the slopwatch CLI tool.
/// </summary>
public static class Program
{
    /// <summary>
    /// Main entry point.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <returns>Exit code: 0 = success, 1 = issues found, 2 = error.</returns>
    public static async Task<int> Main(string[] args)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (sender, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        try
        {
            return await Parser.Default.ParseArguments<AnalyzeCommand, InitCommand, ListRulesCommand>(args)
                .MapResult(
                    (AnalyzeCommand cmd) => cmd.ExecuteAsync(cts.Token),
                    (InitCommand cmd) => cmd.ExecuteAsync(cts.Token),
                    (ListRulesCommand cmd) => cmd.ExecuteAsync(cts.Token),
                    errors => Task.FromResult(2));
        }
        catch (OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("\nOperation cancelled");
            return 2;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Unhandled error: {ex.Message}");
            return 2;
        }
    }
}
