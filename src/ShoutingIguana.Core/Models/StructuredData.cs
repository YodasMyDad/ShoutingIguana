namespace ShoutingIguana.Core.Models;

/// <summary>
/// Represents structured data found on a page (JSON-LD, Microdata, RDFa).
/// </summary>
public class StructuredData
{
    public int Id { get; set; }
    public int UrlId { get; set; }
    public string Type { get; set; } = string.Empty; // "json-ld", "microdata", "rdfa"
    public string SchemaType { get; set; } = string.Empty; // "Organization", "Product", "Article", etc.
    public string RawData { get; set; } = string.Empty; // Full JSON or HTML snippet
    public bool IsValid { get; set; }
    public string? ValidationErrors { get; set; }
    
    // Navigation properties
    public Url Url { get; set; } = null!;
}

