namespace ShoutingIguana.PluginSdk;

/// <summary>
/// Builder for creating report schemas with fluent API.
/// </summary>
public class ReportSchema : IReportSchema
{
    private readonly List<IReportColumn> _columns = new();
    
    /// <inheritdoc/>
    public string TaskKey { get; set; } = string.Empty;
    
    /// <inheritdoc/>
    public int SchemaVersion => 1;
    
    /// <inheritdoc/>
    public IReadOnlyList<IReportColumn> Columns => _columns.AsReadOnly();
    
    /// <inheritdoc/>
    public bool IsUrlBased { get; set; } = true;
    
    /// <summary>
    /// Creates a new report schema for the specified task.
    /// </summary>
    /// <param name="taskKey">Task key that matches IUrlTask.Key</param>
    public static ReportSchema Create(string taskKey)
    {
        return new ReportSchema { TaskKey = taskKey };
    }
    
    /// <summary>
    /// Adds a column to this schema.
    /// </summary>
    public ReportSchema AddColumn(string name, ReportColumnType columnType)
    {
        _columns.Add(ReportColumn.Create(name, columnType));
        return this;
    }
    
    /// <summary>
    /// Adds a column with custom configuration.
    /// </summary>
    public ReportSchema AddColumn(IReportColumn column)
    {
        _columns.Add(column);
        return this;
    }
    
    /// <summary>
    /// Adds a column with display name.
    /// </summary>
    public ReportSchema AddColumn(string name, ReportColumnType columnType, string displayName)
    {
        _columns.Add(ReportColumn.Create(name, columnType).WithDisplayName(displayName));
        return this;
    }
    
    /// <summary>
    /// Adds a primary key column (shown first, highlighted).
    /// </summary>
    public ReportSchema AddPrimaryColumn(string name, ReportColumnType columnType)
    {
        _columns.Add(ReportColumn.Create(name, columnType).AsPrimaryKey());
        return this;
    }
    
    /// <summary>
    /// Adds a primary key column with display name.
    /// </summary>
    public ReportSchema AddPrimaryColumn(string name, ReportColumnType columnType, string displayName)
    {
        _columns.Add(ReportColumn.Create(name, columnType).WithDisplayName(displayName).AsPrimaryKey());
        return this;
    }
    
    /// <summary>
    /// Marks this report as not URL-based (aggregate data).
    /// </summary>
    public ReportSchema AsAggregateReport()
    {
        IsUrlBased = false;
        return this;
    }
    
    /// <summary>
    /// Validates the schema and returns it.
    /// Ensures Severity column is always present and always first.
    /// </summary>
    public ReportSchema Build()
    {
        if (string.IsNullOrWhiteSpace(TaskKey))
            throw new InvalidOperationException("TaskKey cannot be empty");
        
        if (_columns.Count == 0)
            throw new InvalidOperationException("Schema must have at least one column");
        
        // Check for duplicate column names
        var duplicates = _columns.GroupBy(c => c.Name).Where(g => g.Count() > 1).Select(g => g.Key);
        if (duplicates.Any())
            throw new InvalidOperationException($"Duplicate column names found: {string.Join(", ", duplicates)}");
        
        // CRITICAL: Ensure Severity column is always present and always first
        // Plugins should use ReportRow.SetSeverity(Severity enum) instead of Set("Severity", "Info")
        var severityColumn = _columns.FirstOrDefault(c => c.Name.Equals("Severity", StringComparison.OrdinalIgnoreCase));
        
        if (severityColumn == null)
        {
            // Add Severity column if not present
            // Note: Severity values should use the Severity enum via ReportRow.SetSeverity()
            severityColumn = ReportColumn.Create("Severity", ReportColumnType.String)
                .WithDisplayName("Severity")
                .WithWidth(120);
            _columns.Insert(0, severityColumn);
        }
        else if (_columns.IndexOf(severityColumn) != 0)
        {
            // Move existing Severity column to first position
            _columns.Remove(severityColumn);
            _columns.Insert(0, severityColumn);
        }
        
        return this;
    }
}

