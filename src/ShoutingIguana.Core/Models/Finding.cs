using System.Collections.Generic;
using System.Text.Json;
using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Core.Models;

/// <summary>
/// Represents a finding discovered by a plugin during URL analysis.
/// </summary>
public class Finding
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int UrlId { get; set; }
    public string TaskKey { get; set; } = string.Empty;
    public Severity Severity { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? DataJson { get; set; }
    public DateTime CreatedUtc { get; set; }
    
    // Navigation properties
    public Project Project { get; set; } = null!;
    public Url Url { get; set; } = null!;
    
    /// <summary>
    /// Gets the structured finding details from DataJson.
    /// Returns null if DataJson is null or cannot be parsed.
    /// </summary>
    public FindingDetails? GetDetails()
    {
        if (string.IsNullOrWhiteSpace(DataJson))
            return null;
            
        try
        {
            return JsonSerializer.Deserialize<FindingDetails>(DataJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            // If it's old format JSON (raw dictionary), convert entries to flat detail strings for backward compatibility
            try
            {
                var rawData = JsonSerializer.Deserialize<Dictionary<string, object?>>(DataJson);
                if (rawData != null)
                {
                    var items = new List<string>();
                    foreach (var kvp in rawData)
                    {
                        var valueText = kvp.Value?.ToString() ?? "(none)";
                        items.Add($"{kvp.Key}: {valueText}");
                    }

                    return new FindingDetails
                    {
                        Items = items
                    };
                }
            }
            catch
            {
                // If even that fails, return null
            }
            
            return null;
        }
    }
    
    /// <summary>
    /// Sets the structured finding details and serializes to DataJson.
    /// </summary>
    public void SetDetails(FindingDetails? details)
    {
        if (details == null)
        {
            DataJson = null;
            return;
        }
        
        DataJson = JsonSerializer.Serialize(details, new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
    }
}

