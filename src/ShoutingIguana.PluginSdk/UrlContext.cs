using Microsoft.Extensions.Logging;

namespace ShoutingIguana.PluginSdk;

/// <summary>
/// Context passed to URL tasks containing all information about the crawled URL.
/// Provides everything needed to analyze a URL and report findings.
/// </summary>
/// <param name="Url">The URL being analyzed.</param>
/// <param name="Page">Abstraction over browser page (if JavaScript rendering was used). Null if fetched via HttpClient only.</param>
/// <param name="HttpResponse">Raw HTTP response (if fetched via HttpClient). May be null if fetched via Playwright.</param>
/// <param name="RenderedHtml">Rendered HTML content (after JavaScript execution if applicable).</param>
/// <param name="Headers">HTTP response headers.</param>
/// <param name="Project">Project-wide crawl settings.</param>
/// <param name="Metadata">URL metadata (status code, content type, depth, etc.).</param>
/// <param name="Findings">Sink for reporting findings discovered by this task.</param>
/// <param name="Reports">Sink for reporting custom data rows with plugin-defined columns.</param>
/// <param name="Enqueue">Service for enqueueing new URLs discovered during analysis.</param>
/// <param name="Logger">Logger for this task execution.</param>
/// <remarks>
/// <para>
/// This context is provided to your task's <see cref="IUrlTask.ExecuteAsync"/> method
/// for each URL that gets crawled.
/// </para>
/// <para>
/// <b>Key members to use:</b>
/// - <c>ctx.Url</c> - The current URL
/// - <c>ctx.RenderedHtml</c> - HTML content to analyze
/// - <c>ctx.Metadata.StatusCode</c> - HTTP status
/// - <c>ctx.Findings.ReportAsync()</c> - Report issues (legacy)
/// - <c>ctx.Reports.ReportAsync()</c> - Report custom data rows
/// - <c>ctx.Logger</c> - Log messages
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
/// {
///     // Skip non-HTML pages
///     if (ctx.Metadata.ContentType?.Contains("text/html") != true)
///         return;
///         
///     // Skip error pages
///     if (ctx.Metadata.StatusCode &lt; 200 || ctx.Metadata.StatusCode &gt;= 300)
///         return;
///     
///     // Parse HTML
///     var doc = new HtmlDocument();
///     doc.LoadHtml(ctx.RenderedHtml);
///     
///     // Analyze and report findings
///     await ctx.Findings.ReportAsync(Key, Severity.Warning, "CODE", "Message", details);
/// }
/// </code>
/// </example>
public sealed record UrlContext(
    Uri Url,
    IBrowserPage? Page,
    HttpResponseMessage? HttpResponse,
    string? RenderedHtml,
    IReadOnlyDictionary<string, string> Headers,
    ProjectSettings Project,
    UrlMetadata Metadata,
    IFindingSink Findings,
    IReportSink Reports,
    IUrlEnqueue Enqueue,
    ILogger Logger);

/// <summary>
/// Metadata about the crawled URL, including HTTP status and SEO-specific data.
/// </summary>
/// <param name="UrlId">Internal database ID for this URL.</param>
/// <param name="StatusCode">HTTP status code (200, 404, 301, etc.).</param>
/// <param name="ContentType">Content-Type header value (e.g., "text/html", "image/jpeg").</param>
/// <param name="ContentLength">Content-Length in bytes, if available.</param>
/// <param name="Depth">Crawl depth from the start URL (0 = start URL, 1 = linked from start, etc.).</param>
/// <param name="CrawledUtc">When this URL was crawled (UTC timestamp).</param>
/// <param name="CanonicalHtml">Canonical URL from HTML &lt;link rel="canonical"&gt; tag, if present.</param>
/// <param name="CanonicalHttp">Canonical URL from Link HTTP header, if present.</param>
/// <param name="HasMultipleCanonicals">True if page has conflicting canonical declarations.</param>
/// <param name="HasCrossDomainCanonical">True if canonical points to a different domain.</param>
/// <param name="RobotsNoindex">True if page has noindex directive (meta or HTTP header).</param>
/// <param name="RobotsNofollow">True if page has nofollow directive (meta or HTTP header).</param>
/// <param name="XRobotsTag">Raw X-Robots-Tag HTTP header value, if present.</param>
/// <param name="HasRobotsConflict">True if meta robots and X-Robots-Tag have conflicting directives.</param>
/// <param name="HasMetaRefresh">True if page has a meta refresh tag.</param>
/// <param name="MetaRefreshDelay">Delay in seconds for meta refresh, if present.</param>
/// <param name="MetaRefreshTarget">Target URL for meta refresh, if present.</param>
/// <param name="HtmlLang">Language code from &lt;html lang="..."&gt; attribute, if present.</param>
/// <param name="IsRedirectLoop">True if URL encountered an infinite redirect loop (ERR_TOO_MANY_REDIRECTS).</param>
/// <remarks>
/// <para>
/// This metadata is pre-parsed by the crawler to avoid plugins re-parsing the same data.
/// Use these fields instead of parsing HTML yourself when the data is already extracted.
/// </para>
/// <para>
/// <b>Common usage patterns:</b>
/// - Check <c>ctx.Metadata.StatusCode</c> to skip error pages
/// - Check <c>ctx.Metadata.ContentType</c> to filter HTML pages
/// - Use <c>ctx.Metadata.RobotsNoindex</c> for indexability checks
/// - Access <c>ctx.Metadata.CanonicalHtml</c> for canonical validation
/// </para>
/// </remarks>
public sealed record UrlMetadata(
    int UrlId,
    int StatusCode,
    string? ContentType,
    long? ContentLength,
    int Depth,
    DateTime CrawledUtc,
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
    string? HtmlLang = null,
    bool IsRedirectLoop = false);

/// <summary>
/// Project crawl settings passed to plugins.
/// </summary>
/// <param name="ProjectId">The unique ID of the current project.</param>
/// <param name="BaseUrl">The starting URL for the crawl (typically the homepage).</param>
/// <param name="MaxDepth">Maximum crawl depth allowed.</param>
/// <param name="UserAgent">User-Agent string used for HTTP requests.</param>
/// <param name="RespectRobotsTxt">Whether the crawler respects robots.txt rules.</param>
/// <param name="UseSitemapXml">Whether the crawler discovers URLs from sitemap.xml.</param>
/// <remarks>
/// Use these settings to adapt your plugin's behavior to the project configuration.
/// For example, use <c>BaseUrl</c> to determine if a link is external with <see cref="Helpers.UrlHelper.IsExternal"/>.
/// </remarks>
public sealed record ProjectSettings(
    int ProjectId,
    string BaseUrl,
    int MaxDepth,
    string UserAgent,
    bool RespectRobotsTxt,
    bool UseSitemapXml);

