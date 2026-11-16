namespace Slopwatch.Analysis;

/// <summary>
/// Controls how slopwatch treats files that declare themselves generated.
/// </summary>
public enum GeneratedCodeMode
{
    /// <summary>
    /// Skip generated files entirely. This is the default: findings in generated code
    /// cannot be acted on because the file is regenerated on the next build.
    /// </summary>
    Skip,

    /// <summary>
    /// Analyze generated files exactly like hand-written source.
    /// </summary>
    Analyze,

    /// <summary>
    /// Analyze generated files and report findings, marking each result as generated
    /// so consumers can filter or label them.
    /// </summary>
    AnalyzeAndReport
}
