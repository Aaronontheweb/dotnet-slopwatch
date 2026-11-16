using Slopwatch.Analysis;
using Xunit;

namespace Slopwatch.Tests.Analysis;

/// <summary>
/// Tests for how <see cref="FileAnalyzer"/> treats nested git worktrees.
/// </summary>
/// <remarks>
/// A linked worktree is identified by a <c>.git</c> FILE whose gitdir pointer resolves to
/// <c>&lt;common-dir&gt;/worktrees/&lt;name&gt;</c>. The main checkout has a <c>.git</c>
/// directory. An earlier default exclude pattern of <c>**/worktrees/**</c> skipped any directory
/// that happened to be named <c>worktrees</c>, including ordinary source, and reported a clean
/// scan for code it never read.
/// <para>
/// A <c>.git</c> file alone is not proof of a worktree: submodules and
/// <c>git init --separate-git-dir</c> checkouts also use gitfiles, and their source is distinct
/// code that must still be analyzed.
/// </para>
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
        // A real linked worktree carries a .git FILE pointing at <common-dir>/worktrees/<name>.
        // Analyzing it would double-count the same code, so it is skipped structurally rather
        // than by directory name.
        var root = CreateTempDirectory();
        try
        {
            var nested = Path.Combine(root, "wt-branch");
            Directory.CreateDirectory(nested);
            await File.WriteAllTextAsync(
                Path.Combine(nested, ".git"),
                $"gitdir: {Path.Combine(root, ".git", "worktrees", "wt-branch")}");
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
    public async Task RelativeGitDirPointerIsResolved()
    {
        // git worktree --relative-paths writes a pointer relative to the .git file's directory.
        var root = CreateTempDirectory();
        try
        {
            var nested = Path.Combine(root, "wt-rel");
            Directory.CreateDirectory(nested);
            await File.WriteAllTextAsync(
                Path.Combine(nested, ".git"),
                "gitdir: ../.git/worktrees/wt-rel");
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
    public async Task SubmoduleGitFileIsAnalyzed()
    {
        // Submodules also use a .git FILE, but point at <super>/.git/modules/<name>. Submodule
        // source is distinct code, not a duplicate checkout, so it must be analyzed.
        var root = CreateTempDirectory();
        try
        {
            var submodule = Path.Combine(root, "vendor", "lib");
            Directory.CreateDirectory(submodule);
            await File.WriteAllTextAsync(
                Path.Combine(submodule, ".git"),
                $"gitdir: {Path.Combine(root, ".git", "modules", "vendor", "lib")}");
            await File.WriteAllTextAsync(Path.Combine(submodule, "Test.cs"), SlopCode);

            var results = await AnalyzeAsync(root);

            Assert.Contains(results, r => r.RuleId == "SW003");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SeparateGitDirCheckoutIsAnalyzed()
    {
        // git init --separate-git-dir writes a .git file pointing outside the tree. That is a
        // main checkout, not a linked worktree, so its source must be analyzed.
        var root = CreateTempDirectory();
        try
        {
            var checkout = Path.Combine(root, "checkout");
            Directory.CreateDirectory(checkout);
            await File.WriteAllTextAsync(
                Path.Combine(checkout, ".git"),
                $"gitdir: {Path.Combine(root, "separate-git-dir")}");
            await File.WriteAllTextAsync(Path.Combine(checkout, "Test.cs"), SlopCode);

            var results = await AnalyzeAsync(root);

            Assert.Contains(results, r => r.RuleId == "SW003");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MalformedGitFileIsAnalyzed()
    {
        // An unreadable or malformed gitfile must not silently hide source. Analyzing too much
        // is the safe failure mode.
        var root = CreateTempDirectory();
        try
        {
            var nested = Path.Combine(root, "broken");
            Directory.CreateDirectory(nested);
            await File.WriteAllTextAsync(Path.Combine(nested, ".git"), "not a gitfile at all");
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
    public async Task GitDirectoryBelowRootIsAnalyzed()
    {
        // A .git DIRECTORY below the analysis root is a nested main checkout, not a linked
        // worktree. This places the marker strictly inside the walk so the file-versus-directory
        // discrimination is actually exercised.
        var root = CreateTempDirectory();
        try
        {
            var nested = Path.Combine(root, "sub");
            Directory.CreateDirectory(Path.Combine(nested, ".git"));
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
    public async Task LinkedWorktreeRootIsAnalyzed()
    {
        // When analysis starts inside a worktree, that worktree is the root, not a nested
        // duplicate. Its own files must be analyzed.
        var root = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, ".git"),
                $"gitdir: {Path.Combine(root, "..", ".git", "worktrees", "root-wt")}");
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
    public async Task ParentDirectoryGlobOutsideRootIsAnalyzed()
    {
        // Include patterns may reach outside the analysis root ("../sibling/*.cs"). A sibling
        // whose name is longer than the root's is not a descendant, so it must not be treated
        // as a nested worktree even when it carries a worktree marker.
        var parent = CreateTempDirectory();
        try
        {
            var root = Path.Combine(parent, "main");
            var sibling = Path.Combine(parent, "sibling");
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(sibling);

            await File.WriteAllTextAsync(
                Path.Combine(sibling, ".git"),
                $"gitdir: {Path.Combine(parent, ".git", "worktrees", "sibling")}");
            await File.WriteAllTextAsync(Path.Combine(sibling, "Test.cs"), SlopCode);

            var results = await AnalyzeAsync(root, "../sibling/*.cs");

            Assert.Contains(results, r => r.RuleId == "SW003");
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
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

    private static Task<List<Slopwatch.Detection.DetectionResult>> AnalyzeAsync(string root)
        => AnalyzeAsync(root, "**/*.cs");

    private static async Task<List<Slopwatch.Detection.DetectionResult>> AnalyzeAsync(
        string root,
        params string[] patterns)
    {
        var rules = new Slopwatch.Detection.IDetectionRule[] { new Slopwatch.Detection.Rules.EmptyCatchBlockRule() };
        var analyzer = new FileAnalyzer(rules, new AnalysisOptions());
        var results = new List<Slopwatch.Detection.DetectionResult>();

        await foreach (var result in analyzer.AnalyzeDirectoryAsync(root, patterns))
            results.Add(result);

        return results;
    }
}
