namespace ShoutingIguana.PluginSdk;

/// <summary>
/// Sink for reporting findings discovered during URL analysis.
/// </summary>
public interface IFindingSink
{
    /// <summary>
    /// Report a finding.
    /// </summary>
    /// <param name="taskKey">Key of the task reporting the finding</param>
    /// <param name="severity">Severity level</param>
    /// <param name="code">Finding code (e.g., "BROKEN_LINK_404")</param>
    /// <param name="message">Human-readable message</param>
    /// <param name="details">Optional structured details with hierarchical information</param>
    Task ReportAsync(string taskKey, Severity severity, string code, string message, FindingDetails? details = null);
    
    /// <summary>
    /// Report a finding (legacy overload for backward compatibility).
    /// </summary>
    /// <param name="taskKey">Key of the task reporting the finding</param>
    /// <param name="severity">Severity level</param>
    /// <param name="code">Finding code (e.g., "BROKEN_LINK_404")</param>
    /// <param name="message">Human-readable message</param>
    /// <param name="data">Optional data (will be wrapped in TechnicalMetadata for backward compatibility)</param>
    [Obsolete("Use the overload accepting FindingDetails instead. This method wraps data in TechnicalMetadata.")]
    Task ReportAsync(string taskKey, Severity severity, string code, string message, object? data);
}

/// <summary>
/// Severity levels for findings.
/// </summary>
public enum Severity
{
    /// <summary>
    /// Informational finding (e.g., redirect detected).
    /// </summary>
    Info = 0,
    
    /// <summary>
    /// Warning that should be reviewed (e.g., missing meta description).
    /// </summary>
    Warning = 1,
    
    /// <summary>
    /// Error that should be fixed (e.g., broken link).
    /// </summary>
    Error = 2
}

