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

