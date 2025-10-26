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
    
    // Stage 2: Meta data fields
    public string? Title { get; set; }
    public string? MetaDescription { get; set; }
    public string? CanonicalUrl { get; set; } // DEPRECATED - use CanonicalHtml/CanonicalHttp
    public string? MetaRobots { get; set; } // DEPRECATED - use parsed RobotsNoindex/etc
    public string? RedirectTarget { get; set; } // Final URL after redirects
    
    // Enhanced Canonical fields
    public string? CanonicalHtml { get; set; }
    public string? CanonicalHttp { get; set; }
    public bool HasMultipleCanonicals { get; set; }
    public bool HasCrossDomainCanonical { get; set; }
    public string? CanonicalIssues { get; set; } // JSON: validation errors
    
    // Parsed Robots directives
    public bool? RobotsNoindex { get; set; }
    public bool? RobotsNofollow { get; set; }
    public bool? RobotsNoarchive { get; set; }
    public bool? RobotsNosnippet { get; set; }
    public bool? RobotsNoimageindex { get; set; }
    public string? RobotsSource { get; set; } // "meta", "http", "both-conflict"
    public string? XRobotsTag { get; set; }
    public bool HasRobotsConflict { get; set; }
    
    // Language fields
    public string? HtmlLang { get; set; }
    public string? ContentLanguageHeader { get; set; }
    
    // Meta Refresh
    public bool HasMetaRefresh { get; set; }
    public int? MetaRefreshDelay { get; set; }
    public string? MetaRefreshTarget { get; set; }
    
    // JavaScript detection
    public bool HasJsChanges { get; set; }
    public string? JsChangedElements { get; set; }
    
    // Redirect enhancements
    public bool IsRedirectLoop { get; set; }
    public int? RedirectChainLength { get; set; }
    public bool IsSoft404 { get; set; }
    
    // Special HTTP headers
    public string? CacheControl { get; set; }
    public string? Vary { get; set; }
    public string? ContentEncoding { get; set; }
    public string? LinkHeader { get; set; }
    public bool HasHsts { get; set; }
    
    // Stage 3: Duplicate Content Detection
    public string? ContentHash { get; set; } // SHA-256 hash for exact duplicate detection
    public long? SimHash { get; set; } // 64-bit SimHash for near-duplicate detection
    
    // Stage 3: Indexability Computation
    public bool? IsIndexable { get; set; } // Computed: false if noindex or blocked or 4xx/5xx
    
    // Navigation properties
    public Project Project { get; set; } = null!;
    public Url? DiscoveredFromUrl { get; set; }
    public ICollection<Link> LinksFrom { get; set; } = [];
    public ICollection<Link> LinksTo { get; set; } = [];
    public ICollection<Header> Headers { get; set; } = [];
    public ICollection<Finding> Findings { get; set; } = [];
    public ICollection<Redirect> Redirects { get; set; } = [];
    public ICollection<Image> Images { get; set; } = [];
    public ICollection<Hreflang> Hreflangs { get; set; } = [];
    public ICollection<StructuredData> StructuredData { get; set; } = [];
}

public enum UrlStatus
{
    Pending = 0,
    Crawling = 1,
    Completed = 2,
    Failed = 3
}

