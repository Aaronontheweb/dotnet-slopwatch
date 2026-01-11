namespace Slopwatch.Detection;

/// <summary>
/// Represents the severity level of a detected code pattern.
/// </summary>
public enum DetectionSeverity
{
    /// <summary>
    /// Informational finding that may indicate a pattern worth reviewing
    /// but is not necessarily problematic.
    /// </summary>
    Info = 0,

    /// <summary>
    /// Warning-level finding that indicates a potentially problematic pattern
    /// that should be reviewed.
    /// </summary>
    Warning = 1,

    /// <summary>
    /// Error-level finding that indicates a significant issue that should
    /// be addressed before merging.
    /// </summary>
    Error = 2
}
