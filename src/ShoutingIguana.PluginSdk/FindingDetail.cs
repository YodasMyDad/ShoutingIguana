namespace ShoutingIguana.PluginSdk;

/// <summary>
/// Represents a structured detail item that can be nested hierarchically.
/// </summary>
public class FindingDetail
{
    /// <summary>
    /// The text content of this detail item.
    /// </summary>
    public string Text { get; set; } = string.Empty;
    
    /// <summary>
    /// Optional nested child details for hierarchical structures.
    /// </summary>
    public List<FindingDetail>? Children { get; set; }
    
    /// <summary>
    /// Optional metadata associated with this specific detail item.
    /// </summary>
    public Dictionary<string, object?>? Metadata { get; set; }
}

/// <summary>
/// Represents the complete structured details for a finding.
/// </summary>
public class FindingDetails
{
    /// <summary>
    /// The list of top-level detail items.
    /// </summary>
    public List<FindingDetail> Items { get; set; } = new();
    
    /// <summary>
    /// Technical metadata for advanced users (diagnostic data, raw responses, etc.).
    /// This data is hidden by default but can be toggled on for debugging.
    /// </summary>
    public Dictionary<string, object?>? TechnicalMetadata { get; set; }
}

