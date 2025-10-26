using Microsoft.Extensions.Logging;

namespace ShoutingIguana.PluginSdk;

/// <summary>
/// Context passed to URL tasks containing all information about the crawled URL.
/// </summary>
public sealed record UrlContext(
    /// <summary>
    /// The URL being analyzed.
    /// </summary>
    Uri Url,
    
    /// <summary>
    /// Abstraction over browser page (if JavaScript rendering was used).
    /// Null if fetched via HttpClient only.
    /// </summary>
    IBrowserPage? Page,
    
    /// <summary>
    /// Raw HTTP response (if fetched via HttpClient).
    /// May be null if fetched via Playwright.
    /// </summary>
    HttpResponseMessage? HttpResponse,
    
    /// <summary>
    /// Rendered HTML content (after JavaScript execution if applicable).
    /// </summary>
    string? RenderedHtml,
    
    /// <summary>
    /// HTTP response headers.
    /// </summary>
    IReadOnlyDictionary<string, string> Headers,
    
    /// <summary>
    /// Project-wide crawl settings.
    /// </summary>
    ProjectSettings Project,
    
    /// <summary>
    /// URL metadata (status code, content type, depth, etc.).
    /// </summary>
    UrlMetadata Metadata,
    
    /// <summary>
    /// Sink for reporting findings discovered by this task.
    /// </summary>
    IFindingSink Findings,
    
    /// <summary>
    /// Service for enqueueing new URLs discovered during analysis.
    /// </summary>
    IUrlEnqueue Enqueue,
    
    /// <summary>
    /// Logger for this task execution.
    /// </summary>
    ILogger Logger);

/// <summary>
/// Metadata about the crawled URL.
/// </summary>
public sealed record UrlMetadata(
    int UrlId,
    int StatusCode,
    string? ContentType,
    long? ContentLength,
    int Depth,
    DateTime CrawledUtc,
    
    // Enhanced SEO metadata (parsed by crawler)
    string? CanonicalHtml = null,
    string? CanonicalHttp = null,
    bool HasMultipleCanonicals = false,
    bool HasCrossDomainCanonical = false,
    bool? RobotsNoindex = null,
    bool? RobotsNofollow = null,
    string? XRobotsTag = null,
    bool HasRobotsConflict = false,
    bool HasMetaRefresh = false,
    int? MetaRefreshDelay = null,
    string? MetaRefreshTarget = null,
    string? HtmlLang = null);

/// <summary>
/// Project crawl settings.
/// </summary>
public sealed record ProjectSettings(
    int ProjectId,
    string BaseUrl,
    int MaxDepth,
    string UserAgent,
    bool RespectRobotsTxt);

