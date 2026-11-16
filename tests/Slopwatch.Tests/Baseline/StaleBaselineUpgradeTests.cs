using Slopwatch.Cmd.Commands;
using Xunit;

namespace Slopwatch.Tests.Baseline;

/// <summary>
/// End-to-end tests for the stale-baseline upgrade path. These drive the real analyze command so
/// they cover the CLI behavior a user actually experiences, not just the library exception.
/// </summary>
/// <remarks>
/// The failure mode being guarded: a version 1 baseline hashed SW001-SW006 entries from raw syntax
/// text. The current identity scheme cannot match those hashes, so without a version check every
/// entry silently stops matching and the user sees their entire baselined backlog reported as new
/// findings. The command must refuse to run and tell them how to regenerate.
/// </remarks>
[Collection(ConsoleRedirectCollection.Name)]
public class StaleBaselineUpgradeTests : IDisposable
{
    private readonly string _root;

    public StaleBaselineUpgradeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"slopwatch-upgrade-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_root, ".slopwatch"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup; a locked temp directory must not fail the test run.
        }
        GC.SuppressFinalize(this);
    }

    private void WriteBaseline(int version)
    {
        var json = $$"""
            {
              "version": {{version}},
              "createdAt": "2026-01-01T00:00:00Z",
              "updatedAt": "2026-01-01T00:00:00Z",
              "entries": [
                {
                  "hash": "b9eaa094c24d14bd",
                  "ruleId": "SW004",
                  "filePath": "Foo.cs",
                  "lineNumber": 3,
                  "codeSnippet": "Task.Delay(100)",
                  "message": "old",
                  "baselinedAt": "2026-01-01T00:00:00Z"
                }
              ]
            }
            """;
        File.WriteAllText(Path.Combine(_root, ".slopwatch", "baseline.json"), json);
    }

    private void WriteSource()
    {
        File.WriteAllText(
            Path.Combine(_root, "Foo.cs"),
            "public class Foo { public void M() { } }");
    }

    [Fact]
    public async Task Analyze_RefusesToRunWithStaleBaseline()
    {
        WriteSource();
        WriteBaseline(version: 1);

        var command = new AnalyzeCommand { Directory = _root };

        var stderr = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(stderr);
        int exitCode;
        try
        {
            exitCode = await command.ExecuteAsync();
        }
        finally
        {
            Console.SetError(originalError);
        }

        // Exit 2 is the CLI's "cannot proceed" code, distinct from 1 ("issues found").
        Assert.Equal(2, exitCode);

        var message = stderr.ToString();
        Assert.Contains("out of date", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("version 1", message);
        Assert.Contains("slopwatch init --force", message);
    }

    [Fact]
    public async Task Analyze_RunsNormallyWithCurrentBaseline()
    {
        WriteSource();
        WriteBaseline(version: 2);

        var command = new AnalyzeCommand { Directory = _root };

        var exitCode = await command.ExecuteAsync();

        // No findings in the fixture, so a current baseline must not block the run.
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task Analyze_StaleBaselineDoesNotReportBaselinedFindings()
    {
        // The whole point of failing early: the user must not see the baselined backlog
        // reappear as new findings. Assert the command produced no analysis output at all.
        WriteSource();
        WriteBaseline(version: 1);

        var command = new AnalyzeCommand { Directory = _root };

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        Console.SetOut(stdout);
        Console.SetError(stderr);
        int exitCode;
        try
        {
            exitCode = await command.ExecuteAsync();
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        Assert.Equal(2, exitCode);
        Assert.DoesNotContain("SW004", stdout.ToString());
        Assert.DoesNotContain("SW004", stderr.ToString());
    }
}
