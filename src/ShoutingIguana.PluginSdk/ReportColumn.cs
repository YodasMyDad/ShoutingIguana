namespace ShoutingIguana.PluginSdk;

/// <summary>
/// Implementation of IReportColumn with fluent configuration.
/// </summary>
public class ReportColumn : IReportColumn
{
    /// <inheritdoc/>
    public string Name { get; set; } = string.Empty;
    
    /// <inheritdoc/>
    public string? DisplayName { get; set; }
    
    /// <inheritdoc/>
    public ReportColumnType ColumnType { get; set; }
    
    /// <inheritdoc/>
    public int Width { get; set; }
    
    /// <inheritdoc/>
    public bool IsSortable { get; set; } = true;
    
    /// <inheritdoc/>
    public bool IsFilterable { get; set; } = true;
    
    /// <inheritdoc/>
    public bool IsPrimaryKey { get; set; }
    
    /// <summary>
    /// Creates a new report column with the specified name and type.
    /// </summary>
    public static ReportColumn Create(string name, ReportColumnType columnType)
    {
        return new ReportColumn
        {
            Name = name,
            ColumnType = columnType,
            Width = GetDefaultWidth(columnType)
        };
    }
    
    /// <summary>
    /// Sets the display name for this column.
    /// </summary>
    public ReportColumn WithDisplayName(string displayName)
    {
        DisplayName = displayName;
        return this;
    }
    
    /// <summary>
    /// Sets the preferred width for this column.
    /// </summary>
    public ReportColumn WithWidth(int width)
    {
        Width = width;
        return this;
    }
    
    /// <summary>
    /// Marks this column as not sortable.
    /// </summary>
    public ReportColumn NotSortable()
    {
        IsSortable = false;
        return this;
    }
    
    /// <summary>
    /// Marks this column as not filterable.
    /// </summary>
    public ReportColumn NotFilterable()
    {
        IsFilterable = false;
        return this;
    }
    
    /// <summary>
    /// Marks this column as a primary key column.
    /// </summary>
    public ReportColumn AsPrimaryKey()
    {
        IsPrimaryKey = true;
        return this;
    }
    
    private static int GetDefaultWidth(ReportColumnType columnType)
    {
        return columnType switch
        {
            ReportColumnType.String => 250,
            ReportColumnType.Integer => 100,
            ReportColumnType.Decimal => 100,
            ReportColumnType.DateTime => 150,
            ReportColumnType.Boolean => 80,
            ReportColumnType.Url => 400,
            _ => 150
        };
    }
}

