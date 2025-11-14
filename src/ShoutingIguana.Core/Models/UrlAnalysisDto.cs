namespace ShoutingIguana.Core.Models;

/// <summary>
/// Lightweight DTO for URL analysis that excludes the large RenderedHtml field.
/// Used during Phase 2 analysis to minimize memory usage.
/// </summary>
public class UrlAnalysisDto
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
    
    // Meta data fields
    public string? Title { get; set; }
    public string? MetaDescription { get; set; }
    public string? CanonicalUrl { get; set; }
    public string? MetaRobots { get; set; }
    public string? RedirectTarget { get; set; }
    
    // Enhanced Canonical fields
    public string? CanonicalHtml { get; set; }
    public string? CanonicalHttp { get; set; }
    public bool HasMultipleCanonicals { get; set; }
    public bool HasCrossDomainCanonical { get; set; }
    public string? CanonicalIssues { get; set; }
    
    // Parsed Robots directives
    public bool? RobotsNoindex { get; set; }
    public bool? RobotsNofollow { get; set; }
    public bool? RobotsNoarchive { get; set; }
    public bool? RobotsNosnippet { get; set; }
    public bool? RobotsNoimageindex { get; set; }
    public string? RobotsSource { get; set; }
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
    
    // Duplicate Content Detection
    public string? ContentHash { get; set; }
    public long? SimHash { get; set; }
    
    // Indexability Computation
    public bool? IsIndexable { get; set; }
    
    // NOTE: RenderedHtml is intentionally excluded to save memory
    // It's loaded separately only when needed
    
    // Navigation properties
    public List<HeaderSnapshot> Headers { get; set; } = [];
}

/// <summary>
/// Lightweight projection of HTTP headers for plugin analysis.
/// </summary>
public sealed record HeaderSnapshot(string Name, string Value);

