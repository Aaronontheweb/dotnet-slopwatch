namespace Slopwatch.Analysis;

/// <summary>
/// Resolves the effective project root for analysis features that need repository-level files.
/// Prefers repository markers and falls back to the nearest solution directory.
/// </summary>
internal static class ProjectRootLocator
{
    public static string FindRoot(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(directory))
        {
            return Directory.GetCurrentDirectory();
        }

        string? nearestSolutionDirectory = null;

        while (directory != null)
        {
            try
            {
                // Look for common project root indicators
                if (Directory.Exists(Path.Combine(directory, ".git")) ||
                    Directory.Exists(Path.Combine(directory, ".slopwatch")))
                {
                    return directory;
                }

                // Look for nearest solution directory
                if (nearestSolutionDirectory is null &&
                    (Directory.GetFiles(directory, "*.slnx").Length > 0 ||
                     Directory.GetFiles(directory, "*.sln").Length > 0))
                {
                    nearestSolutionDirectory = directory;
                }
            }
            catch
            {
                // If we can't access the directory, continue up.
            }

            var parent = Path.GetDirectoryName(directory);
            if (parent == directory)
            {
                break;
            }

            directory = parent;
        }

        // Fallback to the nearest solution directory, file's directory or current directory
        return nearestSolutionDirectory ?? Path.GetDirectoryName(filePath) ?? Directory.GetCurrentDirectory();
    }
}
