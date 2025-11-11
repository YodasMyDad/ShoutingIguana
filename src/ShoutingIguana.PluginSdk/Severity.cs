namespace ShoutingIguana.PluginSdk;

/// <summary>
/// Severity level for issues.
/// </summary>
public enum Severity
{
    /// <summary>
    /// Informational - no action required.
    /// </summary>
    Info = 0,
    
    /// <summary>
    /// Warning - should be reviewed.
    /// </summary>
    Warning = 1,
    
    /// <summary>
    /// Error - requires attention.
    /// </summary>
    Error = 2
}

