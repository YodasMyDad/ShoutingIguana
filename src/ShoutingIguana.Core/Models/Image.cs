namespace ShoutingIguana.Core.Models;

/// <summary>
/// Represents an image found on a page.
/// </summary>
public class Image
{
    public int Id { get; set; }
    public int UrlId { get; set; } // Page where image was found
    public string SrcUrl { get; set; } = string.Empty;
    public string? AltText { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public int? HttpStatus { get; set; }
    public long? ContentLength { get; set; }
    
    // Navigation properties
    public Url Url { get; set; } = null!;
}

