namespace ShoutingIguana.Core.Models;

/// <summary>
/// Represents an hreflang tag linking to an alternate language version of a URL.
/// Can come from HTML <link rel="alternate" hreflang="xx"> or HTTP Link header.
/// </summary>
public class Hreflang
{
    public int Id { get; set; }
    public int UrlId { get; set; }
    public string LanguageCode { get; set; } = string.Empty; // "en", "en-US", "x-default"
    public string TargetUrl { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty; // "html" or "http"
    public bool IsXDefault { get; set; }
    
    // Navigation properties
    public Url Url { get; set; } = null!;
}

