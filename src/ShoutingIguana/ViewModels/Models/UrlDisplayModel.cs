using ShoutingIguana.Core.Models;

namespace ShoutingIguana.ViewModels.Models;

/// <summary>
/// Wraps a URL with display-specific properties for the Overview tab.
/// </summary>
public class UrlDisplayModel
{
    public Url Url { get; set; } = null!;
    public string Type { get; set; } = "Internal";
    public bool IsInternal { get; set; } = true;
}

