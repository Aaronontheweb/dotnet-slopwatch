using Microsoft.Extensions.FileSystemGlobbing;
using Slopwatch.Detection;

namespace Slopwatch.Analysis;

/// <summary>
/// Manages the registration and discovery of detection rules.
/// </summary>
/// <remarks>
/// The registry maintains a collection of rules and provides efficient lookup
/// of rules applicable to specific files based on glob pattern matching.
/// This class is thread-safe for concurrent rule queries after registration is complete.
/// </remarks>
public sealed class RuleRegistry
{
    private readonly List<IDetectionRule> _rules = new();
    private readonly object _lock = new();

    /// <summary>
    /// Registers a detection rule with the registry.
    /// </summary>
    /// <param name="rule">The rule to register.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="rule"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown if a rule with the same ID is already registered.</exception>
    public void RegisterRule(IDetectionRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        lock (_lock)
        {
            // Check for duplicate rule IDs
            if (_rules.Any(r => r.RuleId == rule.RuleId))
            {
                throw new InvalidOperationException(
                    $"A rule with ID '{rule.RuleId}' is already registered.");
            }

            _rules.Add(rule);
        }
    }

    /// <summary>
    /// Registers multiple detection rules with the registry.
    /// </summary>
    /// <param name="rules">The rules to register.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="rules"/> is null.</exception>
    public void RegisterRules(IEnumerable<IDetectionRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        foreach (var rule in rules)
        {
            RegisterRule(rule);
        }
    }

    /// <summary>
    /// Gets all rules whose file patterns match the specified file path.
    /// </summary>
    /// <param name="filePath">The file path to match against rule patterns.</param>
    /// <returns>A collection of rules applicable to the specified file.</returns>
    /// <remarks>
    /// The file path is normalized to use forward slashes for consistent glob matching.
    /// Rules with no patterns or empty pattern lists are never returned.
    /// </remarks>
    public IReadOnlyList<IDetectionRule> GetRulesForFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        // Normalize path separators for consistent glob matching
        var normalizedPath = NormalizePath(filePath);

        lock (_lock)
        {
            var matchingRules = new List<IDetectionRule>();

            foreach (var rule in _rules)
            {
                if (rule.ApplicableFilePatterns.Count == 0)
                    continue;

                // Check if any of the rule's patterns match the file path
                if (MatchesAnyPattern(normalizedPath, rule.ApplicableFilePatterns))
                {
                    matchingRules.Add(rule);
                }
            }

            return matchingRules;
        }
    }

    /// <summary>
    /// Gets all registered rules.
    /// </summary>
    /// <returns>A read-only collection of all registered rules.</returns>
    public IReadOnlyList<IDetectionRule> GetAllRules()
    {
        lock (_lock)
        {
            return _rules.ToList();
        }
    }

    /// <summary>
    /// Determines if a file path matches any of the specified glob patterns.
    /// </summary>
    /// <param name="normalizedPath">The normalized file path to check.</param>
    /// <param name="patterns">The glob patterns to match against.</param>
    /// <returns>True if the path matches any pattern; otherwise, false.</returns>
    private static bool MatchesAnyPattern(string normalizedPath, IReadOnlyList<string> patterns)
    {
        // For each pattern, determine if we should match against the full path or just the filename
        foreach (var pattern in patterns)
        {
            var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
            matcher.AddInclude(pattern);

            // If pattern contains ** or /, it's meant to match paths with directories
            // Otherwise, it's meant to match just filenames
            string pathToMatch;
            if (pattern.Contains("**") || pattern.Contains("/") || pattern.Contains("\\"))
            {
                // Match against the full path
                pathToMatch = normalizedPath;
            }
            else
            {
                // Match against just the filename
                pathToMatch = Path.GetFileName(normalizedPath);
            }

            var result = matcher.Match(pathToMatch);
            if (result.HasMatches)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Normalizes a file path to use forward slashes and removes any leading path separators.
    /// </summary>
    /// <param name="path">The path to normalize.</param>
    /// <returns>The normalized path.</returns>
    private static string NormalizePath(string path)
    {
        // Replace backslashes with forward slashes
        var normalized = path.Replace('\\', '/');

        // Remove leading slashes for relative path matching
        normalized = normalized.TrimStart('/');

        return normalized;
    }
}
