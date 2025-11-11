using System.Text.Json;

namespace ShoutingIguana.Core.Models;

/// <summary>
/// Represents a report schema stored in the database.
/// Contains column definitions for a plugin's custom report format.
/// </summary>
public class ReportSchema
{
    public int Id { get; set; }
    public string TaskKey { get; set; } = string.Empty;
    public int SchemaVersion { get; set; }
    public string ColumnsJson { get; set; } = string.Empty;
    public bool IsUrlBased { get; set; }
    public DateTime CreatedUtc { get; set; }
    
    // Navigation property
    public ICollection<ReportRow> ReportRows { get; set; } = new List<ReportRow>();
    
    /// <summary>
    /// Gets the deserialized column definitions.
    /// </summary>
    public List<ReportColumnDefinition>? GetColumns()
    {
        if (string.IsNullOrWhiteSpace(ColumnsJson))
            return null;
        
        try
        {
            return JsonSerializer.Deserialize<List<ReportColumnDefinition>>(ColumnsJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return null;
        }
    }
    
    /// <summary>
    /// Sets the column definitions by serializing to JSON.
    /// </summary>
    public void SetColumns(List<ReportColumnDefinition> columns)
    {
        ColumnsJson = JsonSerializer.Serialize(columns, new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
    }
}

/// <summary>
/// Column definition for JSON serialization.
/// </summary>
public class ReportColumnDefinition
{
    public string Name { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public int ColumnType { get; set; } // Store enum as int for compatibility
    public int Width { get; set; }
    public bool IsSortable { get; set; }
    public bool IsFilterable { get; set; }
    public bool IsPrimaryKey { get; set; }
}

