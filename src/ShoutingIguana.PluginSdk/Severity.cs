namespace ShoutingIguana.PluginSdk;

/// <summary>
/// Severity level for issues.
/// Ordered by priority: Error (highest), Warning, Info (lowest).
/// </summary>
public enum Severity
{
    /// <summary>
    /// Error - requires immediate attention.
    /// </summary>
    Error = 0,
    
    /// <summary>
    /// Warning - should be reviewed.
    /// </summary>
    Warning = 1,
    
    /// <summary>
    /// Informational - no action required.
    /// </summary>
    Info = 2
}

