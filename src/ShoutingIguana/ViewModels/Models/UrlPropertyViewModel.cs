namespace ShoutingIguana.ViewModels.Models;

/// <summary>
/// Represents a single property of a URL for display in the Overview tab details panel.
/// </summary>
public class UrlPropertyViewModel
{
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
    public string Category { get; set; } = string.Empty;
}

