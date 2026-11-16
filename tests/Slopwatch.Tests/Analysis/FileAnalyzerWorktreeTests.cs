using Slopwatch.Analysis;
using Xunit;

namespace Slopwatch.Tests.Analysis;

/// <summary>
/// Tests for how <see cref="FileAnalyzer"/> treats nested git worktrees.
/// </summary>
/// <remarks>
/// A linked worktree is identified by a <c>.git</c> FILE (a gitdir pointer). The main checkout
/// has a <c>.git</c> directory. An earlier default exclude pattern of <c>**/worktrees/**</c>
/// skipped any directory that happened to be named <c>worktrees</c>, including ordinary source,
/// and reported a clean scan for code it never read.
/// </remarks>
public class FileAnalyzerWorktreeTests
{
    private const string SlopCode = """
        class C
        {
            void M()
            {
                try { }
                catch (System.IO.IOException) { }
            }
        }
        """;

    [Fact]
    public async Task DirectoryNamedWorktreesIsAnalyzed()
    {
        // A directory called "worktrees" is not necessarily a worktree. Its source must be read.
        var root = CreateTempDirectory();
        try
        {
            var nested = Path.Combine(root, "worktrees", "wt");
            Directory.CreateDirectory(nested);
            await File.WriteAllTextAsync(Path.Combine(nested, "Test.cs"), SlopCode);

            var results = await AnalyzeAsync(root);

            Assert.Contains(results, r => r.RuleId == "SW003");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task NestedGitWorktreeIsSkipped()
    {
        // A real linked worktree carries a .git FILE. Analyzing it would double-count the same
        // code, so it is skipped structurally rather than by directory name.
        var root = CreateTempDirectory();
        try
        {
            var nested = Path.Combine(root, "wt-branch");
            Directory.CreateDirectory(nested);
            await File.WriteAllTextAsync(
                Path.Combine(nested, ".git"),
                "gitdir: /somewhere/.git/worktrees/wt-branch");
            await File.WriteAllTextAsync(Path.Combine(nested, "Test.cs"), SlopCode);

            var results = await AnalyzeAsync(root);

            Assert.DoesNotContain(results, r => r.RuleId == "SW003");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MainCheckoutWithGitDirectoryIsAnalyzed()
    {
        // The main checkout has a .git DIRECTORY, not a file. It must never be treated as nested.
        var root = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            await File.WriteAllTextAsync(Path.Combine(root, "Test.cs"), SlopCode);

            var results = await AnalyzeAsync(root);

            Assert.Contains(results, r => r.RuleId == "SW003");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DefaultExcludePatternsDoNotMatchByDirectoryName()
    {
        // Guard the regression directly: no default pattern may exclude a directory purely
        // because of its name.
        var options = new AnalysisOptions();

        Assert.DoesNotContain(options.ExcludePatterns, p => p.Contains("worktrees"));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"slopwatch-wt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<List<Slopwatch.Detection.DetectionResult>> AnalyzeAsync(string root)
    {
        var rules = new Slopwatch.Detection.IDetectionRule[] { new Slopwatch.Detection.Rules.EmptyCatchBlockRule() };
        var analyzer = new FileAnalyzer(rules, new AnalysisOptions());
        var results = new List<Slopwatch.Detection.DetectionResult>();

        await foreach (var result in analyzer.AnalyzeDirectoryAsync(root, new[] { "**/*.cs" }))
            results.Add(result);

        return results;
    }
}
