namespace ShoutingIguana.Core.Models;

/// <summary>
/// Represents a redirect in a redirect chain.
/// </summary>
public class Redirect
{
    public int Id { get; set; }
    public int UrlId { get; set; }
    public string FromUrl { get; set; } = string.Empty;
    public string ToUrl { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public int Position { get; set; } // Order in chain (0 = initial)
    
    // Navigation properties
    public Url Url { get; set; } = null!;
}

