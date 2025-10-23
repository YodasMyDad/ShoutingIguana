namespace ShoutingIguana.Core.Models;

public class Url
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Address { get; set; } = string.Empty;
    public string NormalizedUrl { get; set; } = string.Empty;
    public string Scheme { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int Depth { get; set; }
    public int? DiscoveredFromUrlId { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime? LastCrawledUtc { get; set; }
    public UrlStatus Status { get; set; }
    public int? HttpStatus { get; set; }
    public string? ContentType { get; set; }
    public long? ContentLength { get; set; }
    public bool? RobotsAllowed { get; set; }
    
    // Navigation properties
    public Project Project { get; set; } = null!;
    public Url? DiscoveredFromUrl { get; set; }
    public ICollection<Link> LinksFrom { get; set; } = [];
    public ICollection<Link> LinksTo { get; set; } = [];
    public ICollection<Header> Headers { get; set; } = [];
}

public enum UrlStatus
{
    Pending = 0,
    Crawling = 1,
    Completed = 2,
    Failed = 3
}

