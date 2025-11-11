namespace ShoutingIguana.PluginSdk;

/// <summary>
/// Details for a finding (used internally by tracker patterns).
/// </summary>
public class FindingDetails
{
    /// <summary>
    /// Flat list of detail items.
    /// </summary>
    public List<string> Items { get; set; } = new();

    /// <summary>
    /// Technical metadata for debugging.
    /// </summary>
    public Dictionary<string, object?>? TechnicalMetadata { get; set; }
}

