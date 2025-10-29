namespace ShoutingIguana.Core.Models;

public class Link
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int FromUrlId { get; set; }
    public int ToUrlId { get; set; }
    public string? AnchorText { get; set; }
    public LinkType LinkType { get; set; }
    
    // SEO rel attributes
    public string? RelAttribute { get; set; }
    public bool IsNofollow { get; set; }
    public bool IsUgc { get; set; }
    public bool IsSponsored { get; set; }
    
    // Element diagnostics (captured in Phase 1 with live browser page)
    public string? DomPath { get; set; }         // CSS selector path to element
    public string? ElementTag { get; set; }      // HTML tag name (a, link, script, img)
    public bool? IsVisible { get; set; }         // Element visibility on page
    public int? PositionX { get; set; }          // Bounding box X coordinate
    public int? PositionY { get; set; }          // Bounding box Y coordinate
    public int? ElementWidth { get; set; }       // Bounding box width
    public int? ElementHeight { get; set; }      // Bounding box height
    public string? HtmlSnippet { get; set; }     // Surrounding HTML context (max 500 chars)
    public string? ParentTag { get; set; }       // Parent element tag name
    
    // Navigation properties
    public Project Project { get; set; } = null!;
    public Url FromUrl { get; set; } = null!;
    public Url ToUrl { get; set; } = null!;
}

public enum LinkType
{
    Hyperlink = 0,
    Image = 1,
    Script = 2,
    Stylesheet = 3,
    Other = 4
}

