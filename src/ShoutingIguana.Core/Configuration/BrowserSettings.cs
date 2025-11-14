namespace ShoutingIguana.Core.Configuration;

/// <summary>
/// Browser-related settings.
/// </summary>
public class BrowserSettings
{
    public bool IsBrowserInstalled { get; set; }
    public DateTime? LastInstalledUtc { get; set; }
    public int ViewportWidth { get; set; } = 1920;
    public int ViewportHeight { get; set; } = 1080;
}

