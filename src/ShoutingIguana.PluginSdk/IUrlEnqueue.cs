namespace ShoutingIguana.PluginSdk;

/// <summary>
/// Service for enqueueing new URLs discovered during analysis.
/// Access via <see cref="UrlContext.Enqueue"/> in your task's ExecuteAsync method.
/// </summary>
/// <remarks>
/// <para>
/// Use this to add newly discovered URLs to the crawl queue.
/// Useful for plugins that discover URLs from sources other than HTML links
/// (e.g., sitemaps, API responses, custom discovery logic).
/// </para>
/// <para>
/// URLs are automatically deduplicated - enqueuing the same URL multiple times
/// will only add it once.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
/// {
///     // Parse sitemap.xml
///     var sitemapUrls = ParseSitemapXml(ctx.RenderedHtml);
///     
///     // Enqueue discovered URLs
///     foreach (var url in sitemapUrls)
///     {
///         await ctx.Enqueue.EnqueueAsync(
///             url, 
///             depth: ctx.Metadata.Depth + 1, 
///             priority: 50);
///     }
///     
///     ctx.Logger.LogInformation("Enqueued {Count} URLs from sitemap", sitemapUrls.Count);
/// }
/// </code>
/// </example>
public interface IUrlEnqueue
{
    /// <summary>
    /// Enqueues a URL for crawling if not already crawled or queued.
    /// </summary>
    /// <param name="url">
    /// The URL to enqueue (can be relative or absolute).
    /// Relative URLs will be resolved against the current page.
    /// </param>
    /// <param name="depth">
    /// Depth of the URL from the start URL.
    /// Typically <c>ctx.Metadata.Depth + 1</c> for links found on the current page.
    /// </param>
    /// <param name="priority">
    /// Priority for crawling (lower numbers = higher priority, default: 100).
    /// Use lower values (e.g., 50) for important URLs like sitemaps.
    /// </param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// <para>
    /// The URL will only be crawled if:
    /// - It hasn't been crawled before
    /// - It passes robots.txt validation
    /// - The depth doesn't exceed project maximum depth
    /// - It's within the allowed domain scope
    /// </para>
    /// <para>
    /// Duplicate calls with the same URL are ignored (deduplication handled automatically).
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Enqueue a discovered URL
    /// await ctx.Enqueue.EnqueueAsync(
    ///     "https://example.com/page", 
    ///     depth: 2, 
    ///     priority: 100);
    /// 
    /// // Enqueue with high priority
    /// await ctx.Enqueue.EnqueueAsync(
    ///     sitemapUrl, 
    ///     depth: 1, 
    ///     priority: 10); // Higher priority
    /// </code>
    /// </example>
    Task EnqueueAsync(string url, int depth, int priority = 100);
}

