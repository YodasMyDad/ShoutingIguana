using System.Text.Json;

namespace ShoutingIguana.Core.Models;

/// <summary>
/// Represents a single data row in a plugin report.
/// Stores flexible column data as JSON.
/// </summary>
public class ReportRow
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string TaskKey { get; set; } = string.Empty;
    public int? UrlId { get; set; } // Nullable for aggregate reports
    public string RowDataJson { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public PluginSdk.Severity? Severity { get; set; }
    public string? IssueText { get; set; }
    
    // Navigation properties
    public Project Project { get; set; } = null!;
    public ReportSchema? ReportSchema { get; set; }
    public Url? Url { get; set; }
    
    /// <summary>
    /// Gets the row data as a dictionary with properly typed values.
    /// </summary>
    public Dictionary<string, object?>? GetData()
    {
        if (string.IsNullOrWhiteSpace(RowDataJson))
            return null;
        
        try
        {
            // Deserialize to JsonElement first to properly handle type conversion
            using var doc = JsonDocument.Parse(RowDataJson);
            var result = new Dictionary<string, object?>();
            
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                var value = ConvertJsonElement(property.Value);
                
                // Special handling for Severity column - convert to enum
                if (string.Equals(property.Name, "Severity", StringComparison.OrdinalIgnoreCase))
                {
                    // Handle string values (e.g., "Error", "Warning", "Info")
                    if (value is string severityStr &&
                        Enum.TryParse<PluginSdk.Severity>(severityStr, true, out var severityEnumFromStr))
                    {
                        result[property.Name] = severityEnumFromStr;
                    }
                    // Handle integer values (e.g., 0=Error, 1=Warning, 2=Info)
                    else if (value is int severityInt &&
                             Enum.IsDefined(typeof(PluginSdk.Severity), severityInt))
                    {
                        result[property.Name] = (PluginSdk.Severity)severityInt;
                    }
                    else
                    {
                        result[property.Name] = value;
                    }
                }
                else
                {
                    result[property.Name] = value;
                }
            }
            
            return result;
        }
        catch
        {
            return null;
        }
    }
    
    /// <summary>
    /// Converts a JsonElement to its actual CLR type.
    /// </summary>
    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt32(out var intVal) ? intVal :
                                    element.TryGetInt64(out var longVal) ? longVal :
                                    element.TryGetDecimal(out var decVal) ? decVal :
                                    (object?)element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToArray(),
            JsonValueKind.Object => ConvertJsonObjectToDictionary(element),
            _ => null
        };
    }
    
    /// <summary>
    /// Converts a JsonElement object to a dictionary.
    /// </summary>
    private static Dictionary<string, object?> ConvertJsonObjectToDictionary(JsonElement element)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var property in element.EnumerateObject())
        {
            dict[property.Name] = ConvertJsonElement(property.Value);
        }
        return dict;
    }
    
    /// <summary>
    /// Sets the row data by serializing to JSON.
    /// </summary>
    public void SetData(Dictionary<string, object?> data)
    {
        RowDataJson = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
    }
    
    /// <summary>
    /// Sets the row data from a PluginSdk ReportRow.
    /// </summary>
    public void SetData(PluginSdk.ReportRow row)
    {
        var data = new Dictionary<string, object?>();
        foreach (var kvp in row.Data)
        {
            data[kvp.Key] = kvp.Value;
        }
        SetData(data);
    }
}

