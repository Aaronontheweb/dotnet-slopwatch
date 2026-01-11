using System;

namespace Slopwatch.Suppression;

/// <summary>
/// Suppresses one or more Slopwatch rules for a code element.
/// </summary>
/// <remarks>
/// This attribute requires detailed justification to prevent abuse by LLMs.
/// The justification should demonstrate human understanding of why the code
/// pattern is legitimate despite triggering a slop detection rule.
///
/// <para>
/// <strong>Design Philosophy:</strong> This attribute is intentionally designed
/// to be LLM-resistant by requiring human-level context and justification that
/// large language models find difficult to fabricate convincingly.
/// </para>
///
/// <para>
/// <strong>Usage Guidelines:</strong>
/// - Scope suppressions as narrowly as possible (prefer method-level over class-level)
/// - Provide specific technical details in the justification
/// - Reference issue trackers for known bugs or planned work
/// - Include reviewer approval for Error-severity rule suppressions
/// - Avoid generic phrases like "false positive" or "not needed"
/// </para>
///
/// <example>
/// Platform-specific test:
/// <code>
/// [Fact(Skip = "Windows-only test - requires COM components")]
/// [SlopwatchSuppress(
///     "SW001",
///     "This test validates Windows COM automation which requires COM components " +
///     "only available on Windows. Cannot run in Linux CI environment.",
///     "Platform",
///     issueUrl: "https://github.com/myorg/myrepo/issues/1234")]
/// public void TestWindowsComInterop()
/// {
///     // Test implementation
/// }
/// </code>
/// </example>
/// </remarks>
[AttributeUsage(
    AttributeTargets.Method |
    AttributeTargets.Class |
    AttributeTargets.Property |
    AttributeTargets.Field,
    AllowMultiple = true,
    Inherited = false)]
public sealed class SlopwatchSuppressAttribute : Attribute
{
    /// <summary>
    /// Gets the rule ID being suppressed (e.g., "SW001", "SW002").
    /// </summary>
    /// <remarks>
    /// Rule IDs follow the format "SW###" where ### is a zero-padded number.
    /// Multiple rules can be suppressed by applying multiple attributes.
    /// </remarks>
    public string RuleId { get; }

    /// <summary>
    /// Gets the detailed justification for suppressing this rule.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The justification must be a human-readable explanation that demonstrates
    /// understanding of why the code pattern is legitimate. Generic justifications
    /// like "not needed" or "false positive" are insufficient and will cause
    /// the attribute constructor to throw an exception.
    /// </para>
    ///
    /// <para>
    /// <strong>Good examples:</strong>
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// "Test requires Windows COM components not available in Linux CI environment"
    /// </description></item>
    /// <item><description>
    /// "P/Invoke signature for kernel32.dll ReadProcessMemory requires unsafe code"
    /// </description></item>
    /// <item><description>
    /// "Testing rate limiter behavior requires actual time delays to validate throttling"
    /// </description></item>
    /// <item><description>
    /// "Optional configuration file - FileNotFoundException is expected and handled with defaults"
    /// </description></item>
    /// </list>
    ///
    /// <para>
    /// <strong>Bad examples (will be rejected):</strong>
    /// </para>
    /// <list type="bullet">
    /// <item><description>"false positive"</description></item>
    /// <item><description>"not needed"</description></item>
    /// <item><description>"test doesn't work"</description></item>
    /// <item><description>"legacy code"</description></item>
    /// </list>
    /// </remarks>
    public string Justification { get; }

    /// <summary>
    /// Gets the category of suppression, indicating the reason type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Valid categories:</strong>
    /// </para>
    /// <list type="table">
    /// <item>
    /// <term>Platform</term>
    /// <description>Platform-specific code (Windows-only, Linux-only, ARM vs x64, etc.)</description>
    /// </item>
    /// <item>
    /// <term>Interop</term>
    /// <description>Interop with native code, COM, P/Invoke, unmanaged memory</description>
    /// </item>
    /// <item>
    /// <term>External</term>
    /// <description>External dependencies not available (database, API, infrastructure)</description>
    /// </item>
    /// <item>
    /// <term>Generated</term>
    /// <description>Auto-generated code that can't be modified (EF migrations, gRPC, etc.)</description>
    /// </item>
    /// <item>
    /// <term>TDD</term>
    /// <description>Test-driven development - feature not yet implemented (red phase)</description>
    /// </item>
    /// <item>
    /// <term>Performance</term>
    /// <description>Performance-critical code requiring unsafe patterns or optimizations</description>
    /// </item>
    /// <item>
    /// <term>Testing</term>
    /// <description>Test infrastructure or testing-specific patterns (rate limit tests, etc.)</description>
    /// </item>
    /// <item>
    /// <term>Legacy</term>
    /// <description>Legacy code with planned refactoring or migration path</description>
    /// </item>
    /// <item>
    /// <term>Library</term>
    /// <description>Third-party library quirks, bugs, or limitations</description>
    /// </item>
    /// </list>
    /// </remarks>
    public string Category { get; }

    /// <summary>
    /// Gets the optional issue or ticket URL for tracking this suppression.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For suppressions related to known issues, bugs, or planned work,
    /// include a reference to the tracking ticket (GitHub issue, Jira, Azure DevOps, etc.).
    /// </para>
    ///
    /// <para>
    /// <strong>Recommended for:</strong>
    /// </para>
    /// <list type="bullet">
    /// <item><description>Error-severity rule suppressions</description></item>
    /// <item><description>Suppressions related to upstream library bugs</description></item>
    /// <item><description>Suppressions for TDD red phase (feature not yet implemented)</description></item>
    /// <item><description>Suppressions for planned refactoring or technical debt</description></item>
    /// </list>
    ///
    /// <para>
    /// <strong>Examples:</strong>
    /// </para>
    /// <list type="bullet">
    /// <item><description>"https://github.com/myorg/myrepo/issues/1234"</description></item>
    /// <item><description>"https://jira.company.com/browse/PROJ-5678"</description></item>
    /// <item><description>"#1234" (for same repository)</description></item>
    /// <item><description>"https://dev.azure.com/org/project/_workitems/edit/1234"</description></item>
    /// </list>
    /// </remarks>
    public string? IssueUrl { get; }

    /// <summary>
    /// Gets the optional reviewer or approver who verified this suppression.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For high-risk suppressions (Error severity rules), consider including
    /// the name or username of the person who reviewed and approved the suppression.
    /// This provides an audit trail and accountability.
    /// </para>
    ///
    /// <para>
    /// <strong>Recommended for:</strong>
    /// </para>
    /// <list type="bullet">
    /// <item><description>Error-severity rule suppressions (SW001, SW003, SW006, SW012, etc.)</description></item>
    /// <item><description>Security-sensitive suppressions (hardcoded credentials, validation removal)</description></item>
    /// <item><description>Suppressions in critical business logic</description></item>
    /// </list>
    ///
    /// <para>
    /// <strong>Examples:</strong>
    /// </para>
    /// <list type="bullet">
    /// <item><description>"john.doe@company.com"</description></item>
    /// <item><description>"@johndoe"</description></item>
    /// <item><description>"John Doe (Tech Lead)"</description></item>
    /// <item><description>"Security Team (security@company.com)"</description></item>
    /// </list>
    /// </remarks>
    public string? Reviewer { get; }

    /// <summary>
    /// Creates a new instance of the SlopwatchSuppressAttribute.
    /// </summary>
    /// <param name="ruleId">
    /// The rule ID to suppress (e.g., "SW001", "SW002"). Must match the format "SW###".
    /// </param>
    /// <param name="justification">
    /// Detailed justification for the suppression. Must be at least 20 characters
    /// and demonstrate human understanding of the context. Generic phrases like
    /// "false positive" or "not needed" will cause this constructor to throw.
    /// </param>
    /// <param name="category">
    /// The category of suppression. Must be one of: Platform, Interop, External,
    /// Generated, TDD, Performance, Testing, Legacy, Library.
    /// </param>
    /// <param name="issueUrl">
    /// Optional issue or ticket URL for tracking this suppression. Recommended for
    /// Error-severity rules, TDD suppressions, and known bugs.
    /// </param>
    /// <param name="reviewer">
    /// Optional reviewer or approver who verified this suppression. Recommended for
    /// Error-severity rules and security-sensitive code.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="ruleId"/>, <paramref name="justification"/>,
    /// or <paramref name="category"/> is null or whitespace.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when:
    /// <list type="bullet">
    /// <item><description>
    /// <paramref name="justification"/> is too short (less than 20 characters)
    /// </description></item>
    /// <item><description>
    /// <paramref name="justification"/> appears to be a generic placeholder
    /// (contains phrases like "false positive", "not needed", etc.)
    /// </description></item>
    /// <item><description>
    /// <paramref name="ruleId"/> doesn't match the expected format "SW###"
    /// </description></item>
    /// </list>
    /// </exception>
    public SlopwatchSuppressAttribute(
        string ruleId,
        string justification,
        string category,
        string? issueUrl = null,
        string? reviewer = null)
    {
        if (string.IsNullOrWhiteSpace(ruleId))
            throw new ArgumentNullException(nameof(ruleId));

        if (string.IsNullOrWhiteSpace(justification))
            throw new ArgumentNullException(nameof(justification));

        if (string.IsNullOrWhiteSpace(category))
            throw new ArgumentNullException(nameof(category));

        // Validate rule ID format (SW### where ### is a number)
        if (!System.Text.RegularExpressions.Regex.IsMatch(ruleId, @"^SW\d{3}$"))
            throw new ArgumentException(
                $"Rule ID must match format 'SW###' where ### is a 3-digit number. Got: '{ruleId}'",
                nameof(ruleId));

        // Enforce minimum justification quality
        if (justification.Length < 20)
            throw new ArgumentException(
                "Justification must be at least 20 characters and explain why the pattern is legitimate. " +
                $"Got {justification.Length} characters.",
                nameof(justification));

        // Detect common generic justifications that LLMs might use
        // This list is intentionally limited to avoid false positives, but catches
        // the most common lazy justifications
        var lowerJustification = justification.ToLowerInvariant().Trim();

        // Exact match patterns (the entire justification is just a generic phrase)
        var exactMatchPhrases = new[]
        {
            "false positive",
            "not needed",
            "not required",
            "doesn't work",
            "does not work",
            "legacy code",
            "technical debt",
            "todo",
            "fix me",
            "fixme",
            "temporary",
            "temp fix",
            "hack",
            "workaround"
        };

        foreach (var phrase in exactMatchPhrases)
        {
            if (lowerJustification == phrase)
            {
                throw new ArgumentException(
                    $"Justification '{justification}' is too generic. " +
                    "Provide specific technical details about why this pattern is legitimate. " +
                    "Example: 'Test requires Windows COM components not available in Linux CI environment.'",
                    nameof(justification));
            }
        }

        // Substring patterns that indicate generic justifications
        // These are phrases that, if they appear, suggest the justification lacks specificity
        var suspiciousSubstrings = new[]
        {
            "this is fine",
            "i know what i'm doing",
            "trust me",
            "it works",
            "it's fine",
            "suppress warning",
            "ignore this",
            "skip this"
        };

        foreach (var substring in suspiciousSubstrings)
        {
            if (lowerJustification.Contains(substring))
            {
                throw new ArgumentException(
                    $"Justification contains suspicious phrase '{substring}'. " +
                    "Provide specific technical details about platform constraints, dependencies, " +
                    "or architectural decisions that necessitate this suppression.",
                    nameof(justification));
            }
        }

        // Validate category against known valid categories
        if (!SuppressionCategory.IsValid(category))
        {
            throw new ArgumentException(
                $"Category '{category}' is not valid. " +
                $"Valid categories are: {SuppressionCategory.GetValidCategoriesDescription()}",
                nameof(category));
        }

        RuleId = ruleId;
        Justification = justification;
        Category = category;
        IssueUrl = issueUrl;
        Reviewer = reviewer;
    }

    /// <summary>
    /// Returns a string representation of this suppression attribute for debugging.
    /// </summary>
    /// <returns>A formatted string containing the rule ID, category, and justification.</returns>
    public override string ToString()
    {
        var parts = new System.Collections.Generic.List<string>
        {
            $"Rule: {RuleId}",
            $"Category: {Category}",
            $"Justification: {Justification}"
        };

        if (!string.IsNullOrWhiteSpace(IssueUrl))
            parts.Add($"Issue: {IssueUrl}");

        if (!string.IsNullOrWhiteSpace(Reviewer))
            parts.Add($"Reviewer: {Reviewer}");

        return string.Join(", ", parts);
    }
}
