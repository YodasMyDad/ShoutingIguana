using ShoutingIguana.PluginSdk.Helpers;

namespace ShoutingIguana.PluginSdk;

/// <summary>
/// Provides simplified access to application repositories for plugins.
/// Eliminates the need for reflection-based repository access patterns.
/// </summary>
/// <remarks>
/// <para>
/// This interface allows plugins to query URL and redirect data without needing to 
/// reference the Core or Data assemblies. All repository interactions are handled
/// through simple DTOs that avoid coupling to internal domain models.
/// </para>
/// <para>
/// <b>Common use cases:</b>
/// - Check if a URL was crawled and get its status
/// - Find all URLs matching certain criteria
/// - Detect redirect chains
/// - Validate canonical targets exist
/// - Check for duplicate content across URLs
/// </para>
/// </remarks>
/// <example>
/// <para><b>Check if a canonical target exists:</b></para>
/// <code>
/// public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
/// {
///     var canonical = GetCanonicalUrl(ctx.RenderedHtml);
///     
///     if (!string.IsNullOrEmpty(canonical))
///     {
///         var accessor = ctx.RepositoryAccessor; // Available via UrlContext
///         var targetUrl = await accessor.GetUrlByAddressAsync(
///             ctx.Project.ProjectId, 
///             canonical);
///         
///         if (targetUrl == null)
///         {
///             await ctx.Findings.ReportAsync(
///                 Key,
///                 Severity.Warning,
///                 "CANONICAL_TARGET_NOT_FOUND",
///                 $"Canonical points to uncrawled URL: {canonical}",
///                 null);
///         }
///         else if (targetUrl.Status >= 400)
///         {
///             await ctx.Findings.ReportAsync(
///                 Key,
///                 Severity.Error,
///                 "CANONICAL_TARGET_ERROR",
///                 $"Canonical points to error page ({targetUrl.Status}): {canonical}",
///                 null);
///         }
///     }
/// }
/// </code>
/// 
/// <para><b>Find redirect chains:</b></para>
/// <code>
/// var redirect = await accessor.GetRedirectAsync(projectId, url);
/// if (redirect != null)
/// {
///     if (redirect.IsPermanent)
///     {
///         // 301/308 permanent redirect
///     }
///     else
///     {
///         // 302/307 temporary redirect
///     }
/// }
/// </code>
/// 
/// <para><b>Get all URLs for duplicate detection:</b></para>
/// <code>
/// var urlsByTitle = new Dictionary&lt;string, List&lt;string&gt;&gt;();
/// 
/// await foreach (var url in accessor.GetUrlsAsync(projectId))
/// {
///     if (url.IsIndexable)
///     {
///         // Group by title for duplicate detection
///     }
/// }
/// </code>
/// </example>
public interface IRepositoryAccessor
{
    /// <summary>
    /// Gets a specific URL by its address for a project. Returns null if not found.
    /// </summary>
    /// <param name="projectId">The project ID to search within.</param>
    /// <param name="address">The URL address to find (should be normalized for best results).</param>
    /// <returns>
    /// URL information if found; null if the URL hasn't been crawled yet or doesn't exist in the project.
    /// </returns>
    /// <remarks>
    /// Use this to validate that URLs referenced in findings (canonicals, redirects, etc.) actually exist.
    /// Consider using <see cref="UrlHelper.Normalize"/> on the address before querying for consistent results.
    /// </remarks>
    /// <example>
    /// <code>
    /// var canonicalUrl = await accessor.GetUrlByAddressAsync(
    ///     ctx.Project.ProjectId,
    ///     "https://example.com/page");
    ///     
    /// if (canonicalUrl == null)
    /// {
    ///     // URL hasn't been crawled yet
    /// }
    /// else if (canonicalUrl.Status == 404)
    /// {
    ///     // URL returns 404
    /// }
    /// </code>
    /// </example>
    Task<UrlInfo?> GetUrlByAddressAsync(int projectId, string address);
    
    /// <summary>
    /// Gets all URLs for a project as an async stream.
    /// Use this when you need to process all URLs (e.g., for duplicate detection, statistics).
    /// </summary>
    /// <param name="projectId">The project ID to retrieve URLs for.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the enumeration.</param>
    /// <returns>
    /// An async enumerable of all URLs in the project. Process with <c>await foreach</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method streams URLs to avoid loading everything into memory at once.
    /// For large projects (10,000+ URLs), this is much more efficient than loading all URLs at once.
    /// </para>
    /// <para>
    /// <b>Performance tip:</b> Filter URLs as you iterate instead of collecting them all first.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Efficient streaming approach
    /// await foreach (var url in accessor.GetUrlsAsync(projectId, ct))
    /// {
    ///     if (url.IsIndexable &amp;&amp; url.Status == 200)
    ///     {
    ///         // Process only indexable, successful URLs
    ///         ProcessUrl(url);
    ///     }
    /// }
    /// 
    /// // For small datasets where you need random access:
    /// var urls = await accessor.GetUrlsAsync(projectId, ct).ToListAsync(ct);
    /// </code>
    /// </example>
    IAsyncEnumerable<UrlInfo> GetUrlsAsync(int projectId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets all redirects for a project as an async stream.
    /// Useful for detecting redirect chains and analyzing redirect patterns.
    /// </summary>
    /// <param name="projectId">The project ID to retrieve redirects for.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the enumeration.</param>
    /// <returns>
    /// An async enumerable of all redirects in the project. Process with <c>await foreach</c>.
    /// </returns>
    /// <remarks>
    /// Redirects include both permanent (301, 308) and temporary (302, 307) redirects.
    /// Use the <see cref="RedirectInfo.StatusCode"/> or <see cref="RedirectInfo.IsPermanent"/> 
    /// to distinguish between them.
    /// </remarks>
    /// <example>
    /// <code>
    /// var redirectChains = new Dictionary&lt;string, List&lt;RedirectInfo&gt;&gt;();
    /// 
    /// await foreach (var redirect in accessor.GetRedirectsAsync(projectId, ct))
    /// {
    ///     // Group by source URL to build redirect chains
    ///     if (!redirectChains.ContainsKey(redirect.SourceUrl))
    ///     {
    ///         redirectChains[redirect.SourceUrl] = new List&lt;RedirectInfo&gt;();
    ///     }
    ///     redirectChains[redirect.SourceUrl].Add(redirect);
    /// }
    /// </code>
    /// </example>
    IAsyncEnumerable<RedirectInfo> GetRedirectsAsync(int projectId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Checks if a specific URL has a redirect configured. Returns null if no redirect exists.
    /// </summary>
    /// <param name="projectId">The project ID to search within.</param>
    /// <param name="sourceUrl">The source URL to check for redirects.</param>
    /// <returns>
    /// Redirect information if the URL redirects; null if it doesn't redirect or wasn't found.
    /// </returns>
    /// <remarks>
    /// This is more efficient than <see cref="GetRedirectsAsync"/> when you only need to check a single URL.
    /// For checking multiple URLs, consider using <see cref="GetRedirectsAsync"/> and building a lookup dictionary.
    /// </remarks>
    /// <example>
    /// <code>
    /// var redirect = await accessor.GetRedirectAsync(projectId, currentUrl);
    /// 
    /// if (redirect != null)
    /// {
    ///     if (!redirect.IsPermanent)
    ///     {
    ///         await ctx.Findings.ReportAsync(
    ///             Key,
    ///             Severity.Warning,
    ///             "TEMPORARY_REDIRECT",
    ///             $"URL uses temporary redirect (HTTP {redirect.StatusCode})",
    ///             FindingDetailsBuilder.Simple(
    ///                 $"From: {currentUrl}",
    ///                 $"To: {redirect.ToUrl}",
    ///                 "Consider using 301 for permanent redirects"
    ///             ));
    ///     }
    /// }
    /// </code>
    /// </example>
    Task<RedirectInfo?> GetRedirectAsync(int projectId, string sourceUrl);
    
    /// <summary>
    /// Gets all outgoing links from a specific URL.
    /// Useful for link analysis, link graph visualization, and internal linking plugins.
    /// </summary>
    /// <param name="projectId">The project ID to search within.</param>
    /// <param name="fromUrlId">The ID of the source URL to get outgoing links from.</param>
    /// <returns>
    /// A list of links originating from the specified URL, including target URL address, anchor text, and link type.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is optimized for per-URL analysis during crawling. Each link includes the target URL's address
    /// so you don't need to make additional queries to resolve URL IDs.
    /// </para>
    /// <para>
    /// Use this for:
    /// - Analyzing outgoing links from a page
    /// - Building link graphs
    /// - Checking anchor text distribution
    /// - Identifying orphaned pages
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var outgoingLinks = await accessor.GetLinksByFromUrlAsync(
    ///     ctx.Project.ProjectId, 
    ///     ctx.Metadata.UrlId);
    /// 
    /// foreach (var link in outgoingLinks)
    /// {
    ///     // Analyze each link
    ///     await ctx.Findings.ReportAsync(
    ///         Key,
    ///         Severity.Info,
    ///         "LINK_FOUND",
    ///         $"Links to: {link.ToUrl}",
    ///         FindingDetailsBuilder.Create()
    ///             .AddItem($"Anchor text: {link.AnchorText}")
    ///             .AddItem($"Link type: {link.LinkType}")
    ///             .Build());
    /// }
    /// </code>
    /// </example>
    Task<List<LinkInfo>> GetLinksByFromUrlAsync(int projectId, int fromUrlId);
    
    /// <summary>
    /// Gets custom extraction rules defined for a project.
    /// Used by the Custom Extraction plugin to retrieve user-defined data extraction patterns.
    /// </summary>
    /// <param name="projectId">The project ID to retrieve extraction rules for.</param>
    /// <returns>A list of extraction rules defined for the project.</returns>
    /// <remarks>
    /// <para>
    /// This is a specialized method used primarily by the Custom Extraction plugin.
    /// Most plugins won't need this method.
    /// </para>
    /// <para>
    /// Extraction rules allow users to define custom data extraction patterns using:
    /// - CSS selectors
    /// - XPath expressions
    /// - Regular expressions
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var rules = await accessor.GetCustomExtractionRulesAsync(projectId);
    /// foreach (var rule in rules.Where(r => r.IsEnabled))
    /// {
    ///     // Apply extraction rule to page
    ///     var value = ExtractValue(html, rule.SelectorType, rule.Selector);
    ///     // Report extracted data
    /// }
    /// </code>
    /// </example>
    Task<List<CustomExtractionRuleInfo>> GetCustomExtractionRulesAsync(int projectId);
}

/// <summary>
/// Simplified URL information for plugin access.
/// Contains essential URL data without coupling to internal domain models.
/// </summary>
/// <param name="Address">The full URL address.</param>
/// <param name="Status">HTTP status code (200, 404, 301, etc.).</param>
/// <param name="ContentType">Content-Type header value (e.g., "text/html", "image/jpeg").</param>
/// <param name="Depth">Crawl depth from the start URL (0 = start URL, 1 = linked from start, etc.).</param>
/// <param name="IsIndexable">
/// Whether the URL is indexable by search engines.
/// False if blocked by robots, has noindex, or is an error page.
/// </param>
/// <remarks>
/// Use this to:
/// - Check URL status codes
/// - Filter by content type
/// - Identify important pages by depth
/// - Find indexable vs non-indexable pages
/// </remarks>
/// <example>
/// <code>
/// if (urlInfo.IsIndexable &amp;&amp; urlInfo.Status == 200)
/// {
///     // This is a successfully crawled, indexable page
/// }
/// 
/// if (urlInfo.Depth == 0)
/// {
///     // This is the homepage or start URL
/// }
/// 
/// if (urlInfo.ContentType?.Contains("text/html") == true)
/// {
///     // This is an HTML page
/// }
/// </code>
/// </example>
public record UrlInfo(
    string Address, 
    int Status, 
    string? ContentType, 
    int Depth, 
    bool IsIndexable);

/// <summary>
/// Simplified redirect information for plugin access.
/// </summary>
/// <param name="SourceUrl">The original URL that redirects.</param>
/// <param name="ToUrl">The target URL that the source redirects to.</param>
/// <param name="StatusCode">HTTP redirect status code (301, 302, 307, 308).</param>
/// <param name="Position">
/// Position in redirect chain (0 = first redirect, 1 = second redirect in chain, etc.).
/// </param>
/// <remarks>
/// <para>
/// <b>Redirect types:</b>
/// - 301: Permanent redirect (SEO-friendly, passes link equity)
/// - 302: Temporary redirect (doesn't pass full link equity)
/// - 307: Temporary redirect (POST-preserving)
/// - 308: Permanent redirect (POST-preserving)
/// </para>
/// <para>
/// Use <see cref="IsPermanent"/> as a convenience property to check for 301/308.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// if (!redirect.IsPermanent)
/// {
///     // Warn about temporary redirects in production
/// }
/// 
/// if (redirect.Position > 0)
/// {
///     // This is part of a redirect chain (multiple hops)
/// }
/// </code>
/// </example>
public record RedirectInfo(
    string SourceUrl,
    string ToUrl, 
    int StatusCode, 
    int Position)
{
    /// <summary>
    /// Gets whether this is a permanent redirect (301 or 308).
    /// Permanent redirects are preferred for SEO as they pass link equity.
    /// </summary>
    public bool IsPermanent => StatusCode == 301 || StatusCode == 308;
}

/// <summary>
/// Represents a link from one URL to another, including metadata like anchor text and link type.
/// </summary>
/// <param name="FromUrlId">The ID of the source URL (where the link originates).</param>
/// <param name="ToUrlId">The ID of the target URL (where the link points to).</param>
/// <param name="ToUrl">The address of the target URL for convenience.</param>
/// <param name="AnchorText">The text of the link (for hyperlinks) or alt text (for images).</param>
/// <param name="LinkType">The type of link (Internal, External, etc.).</param>
/// <remarks>
/// Use this to analyze linking patterns, build link graphs, or check anchor text optimization.
/// </remarks>
public record LinkInfo(
    int FromUrlId,
    int ToUrlId,
    string ToUrl,
    string? AnchorText,
    string LinkType);

/// <summary>
/// Represents a custom data extraction rule.
/// Used by the Custom Extraction plugin for user-defined data extraction patterns.
/// </summary>
/// <param name="Id">Unique identifier for this rule.</param>
/// <param name="ProjectId">The project this rule belongs to.</param>
/// <param name="Name">User-friendly name for this extraction rule.</param>
/// <param name="FieldName">The field name to use when reporting extracted data.</param>
/// <param name="SelectorType">Type of selector (CSS, XPath, or Regex).</param>
/// <param name="Selector">The selector pattern or regex to use for extraction.</param>
/// <param name="IsEnabled">Whether this rule is currently active.</param>
/// <remarks>
/// Extraction rules allow users to extract custom data from pages without writing code.
/// The Custom Extraction plugin applies these rules and reports the extracted values as findings.
/// </remarks>
public record CustomExtractionRuleInfo(
    int Id,
    int ProjectId,
    string Name,
    string FieldName,
    SelectorType SelectorType,
    string Selector,
    bool IsEnabled);

/// <summary>
/// Type of selector used for data extraction.
/// </summary>
public enum SelectorType
{
    /// <summary>
    /// CSS selector (e.g., "h1.title", ".product-price").
    /// </summary>
    Css = 0,
    
    /// <summary>
    /// XPath expression (e.g., "//h1[@class='title']").
    /// </summary>
    XPath = 1,
    
    /// <summary>
    /// Regular expression pattern.
    /// </summary>
    Regex = 2
}

