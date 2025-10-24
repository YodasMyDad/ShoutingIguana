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
    /// <param name="data">Optional structured data (will be serialized to JSON)</param>
    Task ReportAsync(string taskKey, Severity severity, string code, string message, object? data = null);
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

