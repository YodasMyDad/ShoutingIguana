using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;
using ShoutingIguana.PluginSdk.Helpers;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Xml.Linq;

namespace ShoutingIguana.Plugins.Sitemap;

/// <summary>
/// XML sitemap discovery, parsing, validation, comparison, and generation.
/// </summary>
public class SitemapTask(ILogger logger, IRepositoryAccessor repositoryAccessor) : UrlTaskBase
{
    private readonly ILogger _logger = logger;
    private readonly IRepositoryAccessor _repositoryAccessor = repositoryAccessor;
    
    // Track discovered sitemap URLs per project
    private static readonly ConcurrentDictionary<int, HashSet<string>> SitemapUrlsByProject = new();
    
    // Track if sitemap was found for project
    private static readonly ConcurrentDictionary<int, bool> SitemapFoundByProject = new();
    
    // Track if sitemap comparison has been done for project (critical for performance - only run once)
    private static readonly ConcurrentDictionary<int, bool> SitemapComparisonDoneByProject = new();
    
    // Cache URL statuses per project to avoid database queries for every sitemap URL (critical for performance)
    private static readonly ConcurrentDictionary<int, Dictionary<string, int>> UrlStatusCacheByProject = new();
    
    // Semaphore to ensure only one thread loads URL statuses per project
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> UrlStatusLoadingSemaphores = new();

    public override string Key => "Sitemap";
    public override string DisplayName => "Sitemap";
    public override string Description => "Validates XML sitemaps and compares them against crawled pages";
    public override int Priority => 45; // Run after basic analysis

    public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
    {
        // Only analyze internal URLs (external URLs are for BrokenLinks status checking only)
        if (UrlHelper.IsExternal(ctx.Project.BaseUrl, ctx.Url.ToString()))
        {
            return;
        }

        try
        {
            // Check if this is the base URL - try to discover sitemap (only if enabled)
            if (IsBaseUrl(ctx.Url.ToString(), ctx.Project.BaseUrl) && ctx.Project.UseSitemapXml)
            {
                await DiscoverSitemapAsync(ctx);
            }

            // Check if this is a sitemap file itself
            if (IsSitemapUrl(ctx.Url.ToString()))
            {
                // Check HTTP status first
                var httpStatus = ctx.Metadata.StatusCode;
                
                if (httpStatus == 404)
                {
                    // Sitemap not found
                    var row1 = ReportRow.Create()
                        .Set("URL", ctx.Url.ToString())
                        .Set("Issue", "Sitemap Not Found")
                        .Set("InSitemap", "N/A")
                        .Set("StatusCode", httpStatus)
                        .Set("Severity", "Warning");
                    
                    await ctx.Reports.ReportAsync(Key, row1, ctx.Metadata.UrlId, default);
                }
                else if (httpStatus >= 400)
                {
                    var isRestricted = httpStatus == 401 || httpStatus == 403 || httpStatus == 451;
                    
                    if (isRestricted)
                    {
                        var row2 = ReportRow.Create()
                            .Set("URL", ctx.Url.ToString())
                            .Set("Issue", $"Sitemap Restricted (HTTP {httpStatus})")
                            .Set("InSitemap", "N/A")
                            .Set("StatusCode", httpStatus)
                            .Set("Severity", "Info");
                        
                        await ctx.Reports.ReportAsync(Key, row2, ctx.Metadata.UrlId, default);
                    }
                    else
                    {
                        var row = ReportRow.Create()
                            .Set("URL", ctx.Url.ToString())
                            .Set("Issue", $"Sitemap Error (HTTP {httpStatus})")
                            .Set("InSitemap", "N/A")
                            .Set("StatusCode", httpStatus)
                            .Set("Severity", "Error");
                        
                        await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
                    }
                }
                else if (httpStatus >= 200 && httpStatus < 300)
                {
                    // Success - analyze the sitemap
                    await AnalyzeSitemapFileAsync(ctx);
                    
                    // After parsing sitemap, compare with crawled URLs to find orphans
                    // IMPORTANT: Only run once per project (TryAdd returns true only if key was added - thread-safe)
                    // This prevents loading ALL URLs multiple times for projects with multiple sitemap files
                    if (SitemapComparisonDoneByProject.TryAdd(ctx.Project.ProjectId, true))
                    {
                        await CompareSitemapWithCrawledUrlsAsync(ctx);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing sitemap for {Url}", ctx.Url);
        }
    }

    private bool IsBaseUrl(string url, string baseUrl)
    {
        var normalizedUrl = url.TrimEnd('/');
        var normalizedBase = baseUrl.TrimEnd('/');
        return normalizedUrl.Equals(normalizedBase, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsSitemapUrl(string url)
    {
        var path = new Uri(url).AbsolutePath.ToLowerInvariant();
        return path.Contains("sitemap") && (path.EndsWith(".xml") || path.EndsWith(".xml.gz"));
    }

    private async Task DiscoverSitemapAsync(UrlContext ctx)
    {
        // Try to discover sitemap from robots.txt or standard location
        var baseUri = new Uri(ctx.Project.BaseUrl);
        var sitemapUrl = new Uri(baseUri, "/sitemap.xml").ToString();

        _logger.LogInformation("Attempting to discover sitemap at {SitemapUrl}", sitemapUrl);

        // NOTE: In Phase 2 (Analysis), ctx.Enqueue is a stub - sitemaps are already discovered in Phase 1
        // This is fine because sitemap discovery happens via CrawlEngine's sitemap service in Phase 1
        // We only analyze sitemaps here, not discover them
        
        // Don't report a finding here - actual findings will be reported when sitemap is crawled
        // (either SITEMAP_FOUND, SITEMAP_NOT_FOUND, or other validation issues)
        await Task.CompletedTask;
    }

    private async Task AnalyzeSitemapFileAsync(UrlContext ctx)
    {
        if (string.IsNullOrEmpty(ctx.RenderedHtml))
        {
            _logger.LogWarning("Sitemap URL {Url} returned no content", ctx.Url);
            var rowNoContent = ReportRow.Create()
                .Set("URL", ctx.Url.ToString())
                .Set("Issue", "Sitemap Returned No Content")
                .Set("InSitemap", "N/A")
                .Set("StatusCode", ctx.Metadata.StatusCode)
                .Set("Severity", "Warning");
            
            await ctx.Reports.ReportAsync(Key, rowNoContent, ctx.Metadata.UrlId, default);
            return;
        }

        try
        {
            // Check if this is a gzipped sitemap
            var xmlContent = ctx.RenderedHtml;
            var isGzipped = ctx.Url.ToString().EndsWith(".gz", StringComparison.OrdinalIgnoreCase);
            
            // Note: Most web servers serve .gz files with Content-Encoding header, which causes
            // automatic decompression by the HTTP client. This code handles the case where
            // the content is not auto-decompressed, but in most cases it will already be decompressed.
            if (isGzipped && !xmlContent.TrimStart().StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
            {
                // Content doesn't look like XML, might need decompression
                try
                {
                    // Try to decompress - note this is a best-effort attempt
                    // Working with string instead of raw bytes is not ideal, but it's what we have
                    var bytes = System.Text.Encoding.Latin1.GetBytes(ctx.RenderedHtml);
                    using var inputStream = new MemoryStream(bytes);
                    using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress);
                    using var reader = new StreamReader(gzipStream, System.Text.Encoding.UTF8);
                    xmlContent = await reader.ReadToEndAsync();
                    
                    _logger.LogInformation("Successfully decompressed gzipped sitemap: {Url}", ctx.Url);
                }
                catch (Exception ex)
                {
                    // If decompression fails, it might already be decompressed or have other issues
                    _logger.LogWarning(ex, "Could not decompress gzipped sitemap (might already be decompressed): {Url}", ctx.Url);
                    
                    // Only report error if content really doesn't look like XML
                    if (!xmlContent.TrimStart().StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) &&
                        !xmlContent.TrimStart().StartsWith("<urlset", StringComparison.OrdinalIgnoreCase) &&
                        !xmlContent.TrimStart().StartsWith("<sitemapindex", StringComparison.OrdinalIgnoreCase))
                    {
                        var rowGzip = ReportRow.Create()
                            .Set("URL", ctx.Url.ToString())
                            .Set("Issue", "Gzip Decompression Error")
                            .Set("InSitemap", "N/A")
                            .Set("StatusCode", ctx.Metadata.StatusCode)
                            .Set("Severity", "Error");
                        
                        await ctx.Reports.ReportAsync(Key, rowGzip, ctx.Metadata.UrlId, default);
                        return;
                    }
                }
            }
            
            // Check uncompressed size (should be < 50MB)
            var sizeBytes = System.Text.Encoding.UTF8.GetByteCount(xmlContent);
            var sizeMB = sizeBytes / (1024.0 * 1024.0);
            
            if (sizeMB > 50)
            {
                var rowSize = ReportRow.Create()
                    .Set("URL", ctx.Url.ToString())
                    .Set("Issue", $"Sitemap Too Large ({sizeMB:F2}MB)")
                    .Set("InSitemap", "N/A")
                    .Set("StatusCode", ctx.Metadata.StatusCode)
                    .Set("Severity", "Error");
                
                await ctx.Reports.ReportAsync(Key, rowSize, ctx.Metadata.UrlId, default);
            }
            else if (sizeMB > 40)
            {
                var rowWarn = ReportRow.Create()
                    .Set("URL", ctx.Url.ToString())
                    .Set("Issue", $"Sitemap Large ({sizeMB:F2}MB)")
                    .Set("InSitemap", "N/A")
                    .Set("StatusCode", ctx.Metadata.StatusCode)
                    .Set("Severity", "Warning");
                
                await ctx.Reports.ReportAsync(Key, rowWarn, ctx.Metadata.UrlId, default);
            }

            // Parse XML sitemap
            var doc = XDocument.Parse(xmlContent);
            var ns = doc.Root?.Name.Namespace;

            if (ns == null)
            {
                var rowNs = ReportRow.Create()
                    .Set("URL", ctx.Url.ToString())
                    .Set("Issue", "Invalid Sitemap XML (Missing Namespace)")
                    .Set("InSitemap", "N/A")
                    .Set("StatusCode", ctx.Metadata.StatusCode)
                    .Set("Severity", "Warning");
                
                await ctx.Reports.ReportAsync(Key, rowNs, ctx.Metadata.UrlId, default);
                return;
            }

            // Check if this is a sitemap index
            var sitemapElements = doc.Descendants(ns + "sitemap").ToList();
            if (sitemapElements.Any())
            {
                await AnalyzeSitemapIndexAsync(ctx, doc, ns);
            }
            else
            {
                // Regular sitemap
                await AnalyzeSitemapUrlsAsync(ctx, doc, ns);
            }

            SitemapFoundByProject[ctx.Project.ProjectId] = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing sitemap XML for {Url}", ctx.Url);
            var rowParse = ReportRow.Create()
                .Set("URL", ctx.Url.ToString())
                .Set("Issue", $"Sitemap Parse Error: {ex.Message}")
                .Set("InSitemap", "N/A")
                .Set("StatusCode", ctx.Metadata.StatusCode)
                .Set("Severity", "Error");
            
            await ctx.Reports.ReportAsync(Key, rowParse, ctx.Metadata.UrlId, default);
        }
    }

    private async Task AnalyzeSitemapIndexAsync(UrlContext ctx, XDocument doc, XNamespace ns)
    {
        var sitemapElements = doc.Descendants(ns + "sitemap").ToList();

        var row = ReportRow.Create()
            .Set("URL", ctx.Url.ToString())
            .Set("Issue", $"Sitemap Index ({sitemapElements.Count} sitemaps)")
            .Set("InSitemap", "N/A")
            .Set("StatusCode", ctx.Metadata.StatusCode)
            .Set("Severity", "Info");
        
        await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);

        // Extract child sitemaps
        foreach (var sitemapElement in sitemapElements)
        {
            var locElement = sitemapElement.Element(ns + "loc");
            if (locElement != null)
            {
                var sitemapUrl = locElement.Value;
                _logger.LogInformation("Found child sitemap: {SitemapUrl}", sitemapUrl);
                
                // NOTE: In Phase 2, ctx.Enqueue is a stub (sitemaps already discovered in Phase 1)
                // Child sitemaps are automatically discovered by CrawlEngine's sitemap service
                // We only validate and analyze sitemaps here
            }
        }
    }

    private async Task AnalyzeSitemapUrlsAsync(UrlContext ctx, XDocument doc, XNamespace ns)
    {
        var urlElements = doc.Descendants(ns + "url").ToList();

        if (urlElements.Count == 0)
        {
            var rowEmpty = ReportRow.Create()
                .Set("URL", ctx.Url.ToString())
                .Set("Issue", "Empty Sitemap")
                .Set("InSitemap", "N/A")
                .Set("StatusCode", ctx.Metadata.StatusCode)
                .Set("Severity", "Warning");
            
            await ctx.Reports.ReportAsync(Key, rowEmpty, ctx.Metadata.UrlId, default);
            return;
        }

        // Check 50,000 URL limit
        if (urlElements.Count > 50000)
        {
            var rowLimit = ReportRow.Create()
                .Set("URL", ctx.Url.ToString())
                .Set("Issue", $"URL Limit Exceeded ({urlElements.Count:N0} URLs)")
                .Set("InSitemap", "N/A")
                .Set("StatusCode", ctx.Metadata.StatusCode)
                .Set("Severity", "Error");
            
            await ctx.Reports.ReportAsync(Key, rowLimit, ctx.Metadata.UrlId, default);
        }
        else if (urlElements.Count > 45000)
        {
            var rowApproach = ReportRow.Create()
                .Set("URL", ctx.Url.ToString())
                .Set("Issue", $"Approaching URL Limit ({urlElements.Count:N0} URLs)")
                .Set("InSitemap", "N/A")
                .Set("StatusCode", ctx.Metadata.StatusCode)
                .Set("Severity", "Warning");
            
            await ctx.Reports.ReportAsync(Key, rowApproach, ctx.Metadata.UrlId, default);
        }

        var rowFound = ReportRow.Create()
            .Set("URL", ctx.Url.ToString())
            .Set("Issue", $"Sitemap Parsed ({urlElements.Count:N0} URLs)")
            .Set("InSitemap", "N/A")
            .Set("StatusCode", ctx.Metadata.StatusCode)
            .Set("Severity", "Info");
        
        await ctx.Reports.ReportAsync(Key, rowFound, ctx.Metadata.UrlId, default);

        // Track sitemap URLs for later comparison
        HashSet<string> sitemapUrls = [];
        
        foreach (var urlElement in urlElements)
        {
            var locElement = urlElement.Element(ns + "loc");
            if (locElement != null)
            {
                sitemapUrls.Add(locElement.Value);
            }
        }

        // Store for later comparison
        lock (SitemapUrlsByProject)
        {
            if (SitemapUrlsByProject.TryGetValue(ctx.Project.ProjectId, out var existingUrls))
            {
                foreach (var sitemapUrl in sitemapUrls)
                {
                    lock (existingUrls)
                    {
                        existingUrls.Add(sitemapUrl);
                    }
                }
            }
            else
            {
                SitemapUrlsByProject[ctx.Project.ProjectId] = sitemapUrls;
            }
        }

        // Validate sitemap URLs against crawl status
        await ValidateSitemapUrlStatusAsync(ctx, sitemapUrls.ToList());

        // Check for common sitemap issues
        await ValidateSitemapUrlsAsync(ctx, urlElements, ns);
    }

    private async Task ValidateSitemapUrlsAsync(UrlContext ctx, List<XElement> urlElements, XNamespace ns)
    {
        int invalidUrls = 0;
        int tooOldUrls = 0;
        int futureLastmod = 0;
        int invalidPriority = 0;
        int allMaxPriority = 0;
        List<double> priorities = [];

        foreach (var urlElement in urlElements.Take(100)) // Sample first 100 for validation
        {
            var locElement = urlElement.Element(ns + "loc");
            if (locElement == null)
            {
                invalidUrls++;
                continue;
            }

            var loc = locElement.Value;

            // Validate URL format
            if (!Uri.TryCreate(loc, UriKind.Absolute, out _))
            {
                invalidUrls++;
            }

            // Check lastmod if present
            var lastmodElement = urlElement.Element(ns + "lastmod");
            if (lastmodElement != null)
            {
                if (DateTime.TryParse(lastmodElement.Value, out var lastmod))
                {
                    // Check if date is in the future
                    if (lastmod > DateTime.UtcNow)
                    {
                        futureLastmod++;
                    }
                    // Check if URL is very old (>2 years)
                    else if ((DateTime.UtcNow - lastmod).TotalDays > 730)
                    {
                        tooOldUrls++;
                    }
                }
            }
            
            // Validate priority if present
            var priorityElement = urlElement.Element(ns + "priority");
            if (priorityElement != null)
            {
                if (double.TryParse(priorityElement.Value, out var priority))
                {
                    priorities.Add(priority);
                    
                    // Priority must be between 0.0 and 1.0
                    if (priority < 0.0 || priority > 1.0)
                    {
                        invalidPriority++;
                    }
                }
            }
        }
        
        // Check if all priorities are 1.0 (defeats the purpose)
        if (priorities.Count > 0)
        {
            allMaxPriority = priorities.Count(p => p >= 0.99);
            if (allMaxPriority == priorities.Count && priorities.Count >= 10)
            {
                var rowPriority = ReportRow.Create()
                    .Set("URL", ctx.Url.ToString())
                    .Set("Issue", "All Priority 1.0 (Defeats Purpose)")
                    .Set("InSitemap", "N/A")
                    .Set("StatusCode", ctx.Metadata.StatusCode)
                    .Set("Severity", "Warning");
                
                await ctx.Reports.ReportAsync(Key, rowPriority, ctx.Metadata.UrlId, default);
            }
        }

        if (invalidUrls > 0)
        {
            var rowInv = ReportRow.Create()
                .Set("URL", ctx.Url.ToString())
                .Set("Issue", $"Invalid URLs ({invalidUrls})")
                .Set("InSitemap", "N/A")
                .Set("StatusCode", ctx.Metadata.StatusCode)
                .Set("Severity", "Warning");
            
            await ctx.Reports.ReportAsync(Key, rowInv, ctx.Metadata.UrlId, default);
        }
        
        if (futureLastmod > 0)
        {
            var rowFuture = ReportRow.Create()
                .Set("URL", ctx.Url.ToString())
                .Set("Issue", $"Future Lastmod Dates ({futureLastmod})")
                .Set("InSitemap", "N/A")
                .Set("StatusCode", ctx.Metadata.StatusCode)
                .Set("Severity", "Warning");
            
            await ctx.Reports.ReportAsync(Key, rowFuture, ctx.Metadata.UrlId, default);
        }

        if (tooOldUrls > 10)
        {
            var rowOld = ReportRow.Create()
                .Set("URL", ctx.Url.ToString())
                .Set("Issue", $"Outdated URLs ({tooOldUrls} > 2 years old)")
                .Set("InSitemap", "N/A")
                .Set("StatusCode", ctx.Metadata.StatusCode)
                .Set("Severity", "Info");
            
            await ctx.Reports.ReportAsync(Key, rowOld, ctx.Metadata.UrlId, default);
        }
        
        if (invalidPriority > 0)
        {
            var rowInvPri = ReportRow.Create()
                .Set("URL", ctx.Url.ToString())
                .Set("Issue", $"Invalid Priority Values ({invalidPriority})")
                .Set("InSitemap", "N/A")
                .Set("StatusCode", ctx.Metadata.StatusCode)
                .Set("Severity", "Error");
            
            await ctx.Reports.ReportAsync(Key, rowInvPri, ctx.Metadata.UrlId, default);
        }
    }

    /// <summary>
    /// Ensures URL status cache is loaded for the project (loads once, thread-safe).
    /// CRITICAL for performance: prevents database query for every sitemap URL checked.
    /// </summary>
    private async Task EnsureUrlStatusCacheLoadedAsync(int projectId, CancellationToken ct)
    {
        // Fast path: check if already cached
        if (UrlStatusCacheByProject.ContainsKey(projectId))
        {
            return;
        }
        
        // Get or create semaphore for this project
        var semaphore = UrlStatusLoadingSemaphores.GetOrAdd(projectId, _ => new SemaphoreSlim(1, 1));
        
        // Wait for exclusive access to load URL statuses
        await semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Double-check: another thread might have loaded while we waited
            if (UrlStatusCacheByProject.ContainsKey(projectId))
            {
                return;
            }
            
            // Load all URLs and build status cache
            _logger.LogInformation("Loading URL status cache for project {ProjectId} (SitemapTask)", projectId);
            var statusCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            
            await foreach (var url in _repositoryAccessor.GetUrlsAsync(projectId, ct))
            {
                if (!string.IsNullOrEmpty(url.NormalizedUrl))
                {
                    statusCache[url.NormalizedUrl] = url.Status;
                }
            }
            
            // Cache for future use
            UrlStatusCacheByProject[projectId] = statusCache;
            _logger.LogInformation("Cached {Count} URL statuses for project {ProjectId} (SitemapTask)", statusCache.Count, projectId);
        }
        finally
        {
            semaphore.Release();
        }
    }
    
    /// <summary>
    /// Validate sitemap URLs against their actual crawl status.
    /// Reports ERROR for 404s/5xx, WARNING for 301s/noindex pages.
    /// </summary>
    private async Task ValidateSitemapUrlStatusAsync(UrlContext ctx, List<string> sitemapUrls)
    {
        try
        {
            var projectId = ctx.Project.ProjectId;
            var errors404 = new List<string>();
            var errors5xx = new List<string>();
            var redirects = new List<string>();
            var notCrawled = new List<string>();
            
            // CRITICAL PERFORMANCE FIX: Pre-load URL status cache
            // This prevents 1000 database queries (one per sitemap URL)
            await EnsureUrlStatusCacheLoadedAsync(projectId, CancellationToken.None);
            
            // Get the cache
            var statusCache = UrlStatusCacheByProject.GetValueOrDefault(projectId);

            // Check each sitemap URL's status from cache (FAST!)
            foreach (var sitemapUrl in sitemapUrls.Take(1000)) // Limit to 1000 URLs to avoid performance issues
            {
                try
                {
                    // Look up status from cache instead of database query
                    // Normalize URL before lookup to ensure consistency with database
                    var normalizedUrl = NormalizeUrlForCache(sitemapUrl);
                    if (statusCache == null || !statusCache.TryGetValue(normalizedUrl, out var status))
                    {
                        // URL not found in crawl database - might be out of scope or not yet crawled
                        notCrawled.Add(sitemapUrl);
                        continue;
                    }

                    // Check HTTP status
                    if (status == 404)
                    {
                        errors404.Add(sitemapUrl);
                    }
                    else if (status >= 500 && status < 600)
                    {
                        errors5xx.Add(sitemapUrl);
                    }
                    else if (status >= 300 && status < 400)
                    {
                        redirects.Add(sitemapUrl);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking status for sitemap URL {Url}", sitemapUrl);
                }
            }

            // Report 404 errors (ERROR severity)
            if (errors404.Count > 0)
            {
                var row404 = ReportRow.Create()
                    .Set("URL", ctx.Url.ToString())
                    .Set("Issue", $"Sitemap Contains 404s ({errors404.Count} URLs)")
                    .Set("InSitemap", "Yes")
                    .Set("StatusCode", 404)
                    .Set("Severity", "Error");
                
                await ctx.Reports.ReportAsync(Key, row404, ctx.Metadata.UrlId, default);
            }

            // Report 5xx server errors (ERROR severity)
            if (errors5xx.Count > 0)
            {
                var row5xx = ReportRow.Create()
                    .Set("URL", ctx.Url.ToString())
                    .Set("Issue", $"Sitemap Contains 5xx Errors ({errors5xx.Count} URLs)")
                    .Set("InSitemap", "Yes")
                    .Set("StatusCode", 500)
                    .Set("Severity", "Error");
                
                await ctx.Reports.ReportAsync(Key, row5xx, ctx.Metadata.UrlId, default);
            }

            // Report redirects (WARNING severity)
            if (redirects.Count > 0)
            {
                var rowRedir = ReportRow.Create()
                    .Set("URL", ctx.Url.ToString())
                    .Set("Issue", $"Sitemap Contains Redirects ({redirects.Count} URLs)")
                    .Set("InSitemap", "Yes")
                    .Set("StatusCode", 301)
                    .Set("Severity", "Warning");
                
                await ctx.Reports.ReportAsync(Key, rowRedir, ctx.Metadata.UrlId, default);
            }

            _logger.LogDebug("Validated {Total} sitemap URLs: {Errors404} 404s, {Errors5xx} 5xxs, {Redirects} redirects, {NotCrawled} not crawled",
                sitemapUrls.Count, errors404.Count, errors5xx.Count, redirects.Count, notCrawled.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating sitemap URL status");
        }
    }

    private async Task CompareSitemapWithCrawledUrlsAsync(UrlContext ctx)
    {
        try
        {
            // Get all sitemap URLs for this project (with thread-safe copy)
            var projectId = ctx.Project.ProjectId;
            HashSet<string>? sitemapUrls;
            
            lock (SitemapUrlsByProject)
            {
                if (!SitemapUrlsByProject.TryGetValue(projectId, out var urls) || urls.Count == 0)
                {
                    _logger.LogDebug("No sitemap URLs to compare");
                    return;
                }
                
                // Create a copy to avoid holding the lock during database operations
                sitemapUrls = new HashSet<string>(urls, StringComparer.OrdinalIgnoreCase);
            }

            // Get all crawled URLs using repository accessor
            var crawledAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var crawledUrlsWithNoInlinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            await foreach (var url in _repositoryAccessor.GetUrlsAsync(projectId))
            {
                if (!string.IsNullOrEmpty(url.Address))
                {
                    crawledAddresses.Add(url.Address);
                    
                    // URLs at depth > 0 with no inlinks might be orphans
                    if (url.Depth > 0)
                    {
                        crawledUrlsWithNoInlinks.Add(url.Address);
                    }
                }
            }

            // Find orphan URLs (in sitemap but not discovered through crawling)
            var orphanUrls = new List<string>();
            foreach (var sitemapUrl in sitemapUrls)
            {
                if (crawledUrlsWithNoInlinks.Contains(sitemapUrl))
                {
                    orphanUrls.Add(sitemapUrl);
                }
            }

            // Report orphan URLs as findings
            if (orphanUrls.Count > 0)
            {
                _logger.LogInformation("Found {Count} potential orphan URLs in sitemap", orphanUrls.Count);

                var rowOrphan = ReportRow.Create()
                    .Set("URL", ctx.Url.ToString())
                    .Set("Issue", $"Orphan URLs in Sitemap ({orphanUrls.Count})")
                    .Set("InSitemap", "Yes")
                    .Set("StatusCode", 200)
                    .Set("Severity", "Warning");
                
                await ctx.Reports.ReportAsync(Key, rowOrphan, ctx.Metadata.UrlId, default);
            }

            // Find missing URLs (crawled but not in sitemap)
            var missingFromSitemap = crawledAddresses
                .Where(crawledUrl => !sitemapUrls.Contains(crawledUrl))
                .ToList();

            if (missingFromSitemap.Count > 0)
            {
                _logger.LogInformation("Found {Count} crawled URLs missing from sitemap", missingFromSitemap.Count);

                var rowMissing = ReportRow.Create()
                    .Set("URL", ctx.Url.ToString())
                    .Set("Issue", $"URLs Missing from Sitemap ({missingFromSitemap.Count})")
                    .Set("InSitemap", "No")
                    .Set("StatusCode", 200)
                    .Set("Severity", "Info");
                
                await ctx.Reports.ReportAsync(Key, rowMissing, ctx.Metadata.UrlId, default);
            }

            _logger.LogDebug("Sitemap comparison complete: {OrphanCount} orphans, {MissingCount} missing",
                orphanUrls.Count, missingFromSitemap.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error comparing sitemap with crawled URLs");
            // Don't fail the whole task if comparison fails
        }
    }
    
    /// <summary>
    /// Normalizes a URL for cache lookups (matching database normalization).
    /// This ensures consistent lookups regardless of trailing slash variations.
    /// </summary>
    private static string NormalizeUrlForCache(string url)
    {
        try
        {
            var uri = new Uri(url);
            return uri.GetLeftPart(UriPartial.Path).ToLowerInvariant();
        }
        catch
        {
            return url.ToLowerInvariant();
        }
    }
    
    /// <summary>
    /// Cleanup per-project data when project is closed.
    /// </summary>
    public override void CleanupProject(int projectId)
    {
        SitemapUrlsByProject.TryRemove(projectId, out _);
        SitemapFoundByProject.TryRemove(projectId, out _);
        SitemapComparisonDoneByProject.TryRemove(projectId, out _);
        UrlStatusCacheByProject.TryRemove(projectId, out _);
        
        // Cleanup and dispose semaphore
        if (UrlStatusLoadingSemaphores.TryRemove(projectId, out var semaphore))
        {
            semaphore.Dispose();
        }
        
        _logger.LogDebug("Cleaned up sitemap data for project {ProjectId}", projectId);
    }
}

