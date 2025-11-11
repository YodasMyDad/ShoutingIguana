namespace ShoutingIguana.PluginSdk;

/// <summary>
/// Defines metadata for a single column in a plugin report.
/// </summary>
public interface IReportColumn
{
    /// <summary>
    /// Column identifier (must be unique within a report schema).
    /// Use PascalCase without spaces (e.g., "SourceUrl", "StatusCode").
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// Display name shown in the UI (e.g., "Source URL", "Status Code").
    /// If null or empty, Name is used as display name.
    /// </summary>
    string? DisplayName { get; }
    
    /// <summary>
    /// Data type of the column.
    /// </summary>
    ReportColumnType ColumnType { get; }
    
    /// <summary>
    /// Preferred column width in pixels (0 for auto-sizing).
    /// </summary>
    int Width { get; }
    
    /// <summary>
    /// Whether this column can be sorted.
    /// </summary>
    bool IsSortable { get; }
    
    /// <summary>
    /// Whether this column can be filtered.
    /// </summary>
    bool IsFilterable { get; }
    
    /// <summary>
    /// Whether this is a primary/key column (shown first, typically bold).
    /// </summary>
    bool IsPrimaryKey { get; }
}

