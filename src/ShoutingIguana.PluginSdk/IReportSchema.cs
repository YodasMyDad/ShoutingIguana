namespace ShoutingIguana.PluginSdk;

/// <summary>
/// Defines the schema (columns) for a plugin report.
/// </summary>
public interface IReportSchema
{
    /// <summary>
    /// Unique key identifying the plugin task that owns this schema.
    /// Must match the IUrlTask.Key value.
    /// </summary>
    string TaskKey { get; }
    
    /// <summary>
    /// Schema version for future compatibility.
    /// Increment when making breaking changes to the schema.
    /// </summary>
    int SchemaVersion { get; }
    
    /// <summary>
    /// Collection of column definitions for this report.
    /// </summary>
    IReadOnlyList<IReportColumn> Columns { get; }
    
    /// <summary>
    /// Whether this report's rows are associated with URLs.
    /// True: Each row has a UrlId (e.g., page analysis reports).
    /// False: Rows are aggregate data not tied to specific URLs.
    /// </summary>
    bool IsUrlBased { get; }
}

