using Slopwatch.Analysis;
using Xunit;

namespace Slopwatch.Tests.Analysis;

/// <summary>
/// Tests for how <see cref="FileAnalyzer"/> treats nested git worktrees.
/// </summary>
/// <remarks>
/// A linked worktree is identified by a <c>.git</c> FILE whose gitdir carries both a
/// <c>commondir</c> file and a reciprocal <c>gitdir</c> pointer. The main checkout has a
/// <c>.git</c> directory. An earlier default exclude pattern of <c>**/worktrees/**</c> skipped
/// any directory that happened to be named <c>worktrees</c>, including ordinary source, and
/// reported a clean scan for code it never read.
/// <para>
/// A <c>.git</c> file alone is not proof of a worktree, and neither is a gitdir whose parent
/// happens to be named <c>worktrees</c>: submodules added with <c>--name worktrees/lib</c> and
/// <c>git init --separate-git-dir .../worktrees/checkout</c> checkouts both look like that, and
/// their source is distinct code that must still be analyzed.
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
        // A real linked worktree carries a .git FILE whose gitdir has commondir + gitdir.
        // Analyzing it would double-count the same code, so it is skipped structurally.
        var root = CreateTempDirectory();
        try
        {
            var nested = Path.Combine(root, "wt-branch");
            Directory.CreateDirectory(nested);
            WriteLinkedWorktreeMarker(root, nested, "wt-branch");
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
        // The admin directory here is deliberately NOT named "worktrees" so the test cannot pass
        // by matching a literal path component.
        var root = CreateTempDirectory();
        try
        {
            var nested = Path.Combine(root, "wt-rel");
            Directory.CreateDirectory(nested);

            var admin = Path.Combine(root, "admin", "wt-rel");
            Directory.CreateDirectory(admin);
            await File.WriteAllTextAsync(Path.Combine(admin, "commondir"), "../..");
            await File.WriteAllTextAsync(Path.Combine(admin, "gitdir"), Path.Combine(nested, ".git"));

            // Relative pointer from nested/.git back up to root/admin/wt-rel.
            await File.WriteAllTextAsync(Path.Combine(nested, ".git"), "gitdir: ../admin/wt-rel");
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
        // Submodules also use a .git FILE, but their admin directory has no commondir/gitdir
        // pair. Submodule source is distinct code, not a duplicate checkout, so it must be read.
        var root = CreateTempDirectory();
        try
        {
            var submodule = Path.Combine(root, "vendor", "lib");
            Directory.CreateDirectory(submodule);

            var admin = Path.Combine(root, ".git", "modules", "vendor", "lib");
            Directory.CreateDirectory(admin);
            await File.WriteAllTextAsync(Path.Combine(admin, "HEAD"), "ref: refs/heads/main");

            await File.WriteAllTextAsync(
                Path.Combine(submodule, ".git"),
                $"gitdir: {admin}");
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
    public async Task SubmoduleNamedWorktreesIsAnalyzed()
    {
        // The regression that a directory-name heuristic misses: a submodule added with
        // --name worktrees/lib puts its admin dir under .git/modules/worktrees/lib, so the
        // gitdir's parent is literally named "worktrees" even though it is not a worktree.
        var root = CreateTempDirectory();
        try
        {
            var submodule = Path.Combine(root, "vendor", "lib");
            Directory.CreateDirectory(submodule);

            var admin = Path.Combine(root, ".git", "modules", "worktrees", "lib");
            Directory.CreateDirectory(admin);
            await File.WriteAllTextAsync(Path.Combine(admin, "HEAD"), "ref: refs/heads/main");

            await File.WriteAllTextAsync(Path.Combine(submodule, ".git"), $"gitdir: {admin}");
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
        // main checkout, not a linked worktree, so its source must be analyzed. The admin dir is
        // named "worktrees" to prove the name is not the discriminator.
        var root = CreateTempDirectory();
        try
        {
            var checkout = Path.Combine(root, "checkout");
            Directory.CreateDirectory(checkout);

            var admin = Path.Combine(root, "metadata", "worktrees", "checkout");
            Directory.CreateDirectory(admin);
            await File.WriteAllTextAsync(Path.Combine(admin, "HEAD"), "ref: refs/heads/main");

            await File.WriteAllTextAsync(Path.Combine(checkout, ".git"), $"gitdir: {admin}");
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
    public async Task NonexistentGitDirPointerIsAnalyzed()
    {
        // A stale pointer must not hide source. Analyzing too much is the safe failure mode.
        var root = CreateTempDirectory();
        try
        {
            var nested = Path.Combine(root, "stale");
            Directory.CreateDirectory(nested);
            await File.WriteAllTextAsync(
                Path.Combine(nested, ".git"),
                $"gitdir: {Path.Combine(root, "nonexistent", "worktrees", "stale")}");
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
    public async Task MalformedGitFileIsAnalyzed()
    {
        // An unreadable or malformed gitfile must not silently hide source.
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
            var admin = Path.Combine(root, "..", "admin", "root-wt");
            Directory.CreateDirectory(admin);
            await File.WriteAllTextAsync(Path.Combine(admin, "commondir"), "../..");
            await File.WriteAllTextAsync(Path.Combine(admin, "gitdir"), Path.Combine(root, ".git"));

            await File.WriteAllTextAsync(Path.Combine(root, ".git"), $"gitdir: {admin}");
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
        // is not a descendant, so it must not be treated as a nested worktree even when it
        // carries a worktree marker.
        var parent = CreateTempDirectory();
        try
        {
            var root = Path.Combine(parent, "main");
            var sibling = Path.Combine(parent, "sibling");
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(sibling);

            WriteLinkedWorktreeMarker(parent, sibling, "sibling");
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
    public void FilesystemRootIsAncestorOfEveryDescendant()
    {
        // A filesystem root already ends in a separator, so the boundary character after it is
        // the first character of the child name. Comparing that against a separator would reject
        // every descendant of the root.
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var descendant = Path.Combine(root, "tmp");

        Assert.True(FileAnalyzer.IsStrictDescendant(descendant, root));
    }

    [Fact]
    public void PrefixCollisionIsNotDescendant()
    {
        // "/repo-other" must not be treated as living under "/repo".
        var root = Path.Combine(Path.GetTempPath(), "repo");
        var sibling = Path.Combine(Path.GetTempPath(), "repo-other");

        Assert.False(FileAnalyzer.IsStrictDescendant(sibling, root));
    }

    [Fact]
    public void RootIsNotItsOwnDescendant()
    {
        var root = Path.Combine(Path.GetTempPath(), "repo");

        Assert.False(FileAnalyzer.IsStrictDescendant(root, root));
    }

    [Fact]
    public void DefaultExcludePatternsDoNotMatchByDirectoryName()
    {
        // Guard the regression directly: no default pattern may exclude a directory purely
        // because of its name.
        var options = new AnalysisOptions();

        Assert.DoesNotContain(options.ExcludePatterns, p => p.Contains("worktrees"));
    }

    /// <summary>
    /// Writes a <c>.git</c> file plus the administrative directory Git creates for a linked
    /// worktree: a <c>commondir</c> file and a reciprocal <c>gitdir</c> pointer.
    /// </summary>
    private static void WriteLinkedWorktreeMarker(string commonDir, string worktreePath, string name)
    {
        var admin = Path.Combine(commonDir, ".git", "worktrees", name);
        Directory.CreateDirectory(admin);
        File.WriteAllText(Path.Combine(admin, "commondir"), "../..");
        File.WriteAllText(Path.Combine(admin, "gitdir"), Path.Combine(worktreePath, ".git"));
        File.WriteAllText(Path.Combine(worktreePath, ".git"), $"gitdir: {admin}");
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
