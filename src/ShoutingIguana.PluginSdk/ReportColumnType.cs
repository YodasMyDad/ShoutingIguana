namespace ShoutingIguana.PluginSdk;

/// <summary>
/// Defines the data types supported for report columns.
/// </summary>
public enum ReportColumnType
{
    /// <summary>
    /// Text string value.
    /// </summary>
    String = 0,
    
    /// <summary>
    /// Integer numeric value.
    /// </summary>
    Integer = 1,
    
    /// <summary>
    /// Decimal numeric value.
    /// </summary>
    Decimal = 2,
    
    /// <summary>
    /// Date and time value.
    /// </summary>
    DateTime = 3,
    
    /// <summary>
    /// Boolean true/false value.
    /// </summary>
    Boolean = 4,
    
    /// <summary>
    /// URL/hyperlink value (clickable in UI).
    /// </summary>
    Url = 5
}

