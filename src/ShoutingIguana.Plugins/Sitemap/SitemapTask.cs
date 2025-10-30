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
                    var details = FindingDetailsBuilder.Create()
                        .AddItem($"Sitemap URL: {ctx.Url}")
                        .AddItem($"HTTP Status: {httpStatus}")
                        .BeginNested("💡 Recommendations")
                            .AddItem("Consider creating an XML sitemap to help search engines discover your content")
                            .AddItem("Sitemaps improve crawl efficiency and coverage")
                        .WithTechnicalMetadata("url", ctx.Url.ToString())
                        .WithTechnicalMetadata("httpStatus", httpStatus)
                        .Build();
                    
                    await ctx.Findings.ReportAsync(
                        Key,
                        Severity.Warning,
                        "SITEMAP_NOT_FOUND",
                        string.Format("Sitemap not found at {0} (HTTP {1})", ctx.Url, httpStatus),
                        details);
                }
                else if (httpStatus >= 400)
                {
                    // Check for restricted status codes
                    var isRestricted = httpStatus == 401 || httpStatus == 403 || httpStatus == 451;
                    
                    if (isRestricted)
                    {
                        // Restricted sitemap - informational
                        var restrictionType = httpStatus switch
                        {
                            401 => "requires authentication",
                            403 => "is forbidden/restricted",
                            451 => "is unavailable for legal reasons",
                            _ => "is restricted"
                        };
                        
                        var note = httpStatus switch
                        {
                            401 => "This sitemap requires authentication/login to access",
                            403 => "This sitemap is restricted and access is forbidden",
                            451 => "This sitemap is unavailable for legal reasons",
                            _ => "This sitemap has restricted access"
                        };
                        
                        var details = FindingDetailsBuilder.Create()
                            .AddItem($"Sitemap URL: {ctx.Url}")
                            .AddItem($"HTTP Status: {httpStatus}")
                            .AddItem($"ℹ️ {note}")
                            .BeginNested("💡 Note")
                                .AddItem("If this is expected, ensure search engines can access the sitemap")
                                .AddItem("Or provide an accessible alternative")
                            .WithTechnicalMetadata("url", ctx.Url.ToString())
                            .WithTechnicalMetadata("httpStatus", httpStatus)
                            .Build();
                        
                        await ctx.Findings.ReportAsync(
                            Key,
                            Severity.Info,
                            "SITEMAP_RESTRICTED",
                            string.Format("Sitemap at {0} {1} (HTTP {2})", ctx.Url, restrictionType, httpStatus),
                            details);
                    }
                    else
                    {
                        // Other error
                        var details = FindingDetailsBuilder.WithMetadata(
                            new Dictionary<string, object?> {
                                ["url"] = ctx.Url.ToString(),
                                ["httpStatus"] = httpStatus
                            },
                            $"Error accessing sitemap: HTTP {httpStatus}");
                        
                        await ctx.Findings.ReportAsync(
                            Key,
                            Severity.Error,
                            "SITEMAP_ERROR",
                            string.Format("Error accessing sitemap at {0} (HTTP {1})", ctx.Url, httpStatus),
                            details);
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
            var details = FindingDetailsBuilder.Create()
                .AddItem($"Sitemap URL: {ctx.Url}")
                .AddItem("❌ No content returned")
                .BeginNested("💡 Recommendations")
                    .AddItem("Ensure your sitemap.xml file contains valid XML content")
                    .AddItem("Check server configuration")
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "SITEMAP_EMPTY_CONTENT",
                $"Sitemap at {ctx.Url} returned no content",
                details);
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
                        var details = FindingDetailsBuilder.WithMetadata(
                            new Dictionary<string, object?> { ["url"] = ctx.Url.ToString() },
                            "Gzipped sitemap could not be decompressed",
                            "Content doesn't appear to be XML",
                            "ℹ️ May already be decompressed by HTTP client");
                        
                        await ctx.Findings.ReportAsync(
                            Key,
                            Severity.Error,
                            "SITEMAP_GZIP_ERROR",
                            $"Gzipped sitemap could not be decompressed and content doesn't appear to be XML",
                            details);
                        return;
                    }
                }
            }
            
            // Check uncompressed size (should be < 50MB)
            var sizeBytes = System.Text.Encoding.UTF8.GetByteCount(xmlContent);
            var sizeMB = sizeBytes / (1024.0 * 1024.0);
            
            if (sizeMB > 50)
            {
                var details = FindingDetailsBuilder.Create()
                    .AddItem($"Sitemap size: {sizeMB:F2}MB")
                    .AddItem($"Limit: 50MB")
                    .AddItem("❌ Exceeds maximum allowed size")
                    .BeginNested("💡 Recommendations")
                        .AddItem("Split into multiple sitemaps")
                        .AddItem("Create a sitemap index file")
                        .AddItem("Each sitemap should be under 50MB")
                    .WithTechnicalMetadata("url", ctx.Url.ToString())
                    .WithTechnicalMetadata("sizeBytes", sizeBytes)
                    .WithTechnicalMetadata("sizeMB", sizeMB)
                    .Build();
                
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Error,
                    "SITEMAP_SIZE_LIMIT_EXCEEDED",
                    $"Sitemap exceeds 50MB limit (current size: {sizeMB:F2}MB)",
                    details);
            }
            else if (sizeMB > 40)
            {
                var details = FindingDetailsBuilder.Create()
                    .AddItem($"Sitemap size: {sizeMB:F2}MB")
                    .AddItem($"Limit: 50MB")
                    .AddItem("⚠️ Approaching size limit")
                    .BeginNested("💡 Recommendations")
                        .AddItem("Consider splitting before reaching the limit")
                        .AddItem("Plan for sitemap index structure")
                    .WithTechnicalMetadata("url", ctx.Url.ToString())
                    .WithTechnicalMetadata("sizeMB", sizeMB)
                    .Build();
                
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Warning,
                    "SITEMAP_APPROACHING_SIZE_LIMIT",
                    $"Sitemap approaching 50MB limit (current size: {sizeMB:F2}MB)",
                    details);
            }

            // Parse XML sitemap
            var doc = XDocument.Parse(xmlContent);
            var ns = doc.Root?.Name.Namespace;

            if (ns == null)
            {
                var details = FindingDetailsBuilder.WithMetadata(
                    new Dictionary<string, object?> { ["url"] = ctx.Url.ToString() },
                    "Sitemap XML is missing namespace",
                    "⚠️ Should include xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\"");
                
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Warning,
                    "INVALID_SITEMAP_XML",
                    "Sitemap XML is missing namespace",
                    details);
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
            var details = FindingDetailsBuilder.Create()
                .AddItem("Failed to parse sitemap XML")
                .AddItem($"Error: {ex.Message}")
                .BeginNested("💡 Recommendations")
                    .AddItem("Validate XML syntax")
                    .AddItem("Ensure proper XML structure")
                    .AddItem("Use sitemap validator tools")
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("error", ex.Message)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Error,
                "SITEMAP_PARSE_ERROR",
                $"Failed to parse sitemap XML: {ex.Message}",
                details);
        }
    }

    private async Task AnalyzeSitemapIndexAsync(UrlContext ctx, XDocument doc, XNamespace ns)
    {
        var sitemapElements = doc.Descendants(ns + "sitemap").ToList();

        var details = FindingDetailsBuilder.WithMetadata(
            new Dictionary<string, object?> {
                ["url"] = ctx.Url.ToString(),
                ["sitemapCount"] = sitemapElements.Count
            },
            $"Sitemap index found with {sitemapElements.Count} sitemap(s)",
            "✅ Good practice for large sites");
        
        await ctx.Findings.ReportAsync(
            Key,
            Severity.Info,
            "SITEMAP_INDEX_FOUND",
            $"Sitemap index contains {sitemapElements.Count} sitemap(s)",
            details);

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
            var details = FindingDetailsBuilder.WithMetadata(
                new Dictionary<string, object?> { ["url"] = ctx.Url.ToString() },
                "Sitemap contains no URLs",
                "⚠️ Empty sitemap provides no value");
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "EMPTY_SITEMAP",
                "Sitemap contains no URLs",
                details);
            return;
        }

        // Check 50,000 URL limit
        if (urlElements.Count > 50000)
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem($"URL count: {urlElements.Count:N0}")
                .AddItem($"Limit: 50,000 URLs")
                .AddItem("❌ Exceeds maximum")
                .BeginNested("💡 Recommendations")
                    .AddItem("Split into multiple sitemaps")
                    .AddItem("Create a sitemap index file")
                    .AddItem("Each sitemap: max 50,000 URLs")
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("urlCount", urlElements.Count)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Error,
                "SITEMAP_URL_LIMIT_EXCEEDED",
                $"Sitemap exceeds 50,000 URL limit (contains {urlElements.Count} URLs)",
                details);
        }
        else if (urlElements.Count > 45000)
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem($"URL count: {urlElements.Count:N0}")
                .AddItem($"Limit: 50,000 URLs")
                .AddItem("⚠️ Approaching limit")
                .BeginNested("💡 Recommendations")
                    .AddItem("Consider splitting before reaching the limit")
                    .AddItem("Plan sitemap architecture now")
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("urlCount", urlElements.Count)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "SITEMAP_APPROACHING_URL_LIMIT",
                $"Sitemap approaching 50,000 URL limit (contains {urlElements.Count} URLs)",
                details);
        }

        var sitemapDetails = FindingDetailsBuilder.WithMetadata(
            new Dictionary<string, object?> {
                ["url"] = ctx.Url.ToString(),
                ["urlCount"] = urlElements.Count
            },
            $"Sitemap contains {urlElements.Count:N0} URLs",
            "✅ Successfully parsed");
        
        await ctx.Findings.ReportAsync(
            Key,
            Severity.Info,
            "SITEMAP_FOUND",
            $"Sitemap contains {urlElements.Count} URL(s)",
            sitemapDetails);

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
                var details = FindingDetailsBuilder.Create()
                    .AddItem("All URLs have priority 1.0")
                    .AddItem("⚠️ This defeats the purpose of priority values")
                    .BeginNested("💡 Recommendations")
                        .AddItem("Use priority to indicate relative importance (0.0 to 1.0)")
                        .AddItem("Homepage: 1.0, important pages: 0.8, others: 0.5")
                    .WithTechnicalMetadata("url", ctx.Url.ToString())
                    .Build();
                
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Warning,
                    "ALL_PRIORITY_MAX",
                    "All URLs have priority 1.0 (defeats the purpose of priorities)",
                    details);
            }
        }

        if (invalidUrls > 0)
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem($"Invalid URLs found: {invalidUrls}")
                .BeginNested("💡 Recommendations")
                    .AddItem("Remove or fix invalid URLs in sitemap")
                    .AddItem("All URLs must be absolute and valid")
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("invalidCount", invalidUrls)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "INVALID_SITEMAP_URLS",
                $"Sitemap contains {invalidUrls} invalid URL(s)",
                details);
        }
        
        if (futureLastmod > 0)
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem($"URLs with future lastmod dates: {futureLastmod}")
                .AddItem("⚠️ lastmod dates should not be in the future")
                .BeginNested("💡 Recommendations")
                    .AddItem("Use current or past dates only")
                    .AddItem("Check server clock if dates are consistently wrong")
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("futureCount", futureLastmod)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "FUTURE_LASTMOD_DATES",
                $"Sitemap contains {futureLastmod} URL(s) with lastmod dates in the future",
                details);
        }

        if (tooOldUrls > 10)
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem($"URLs with lastmod > 2 years old: {tooOldUrls}")
                .AddItem("ℹ️ May indicate stale content")
                .BeginNested("💡 Recommendations")
                    .AddItem("Review these URLs for relevance")
                    .AddItem("Update or remove outdated content")
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("outdatedCount", tooOldUrls)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "OUTDATED_SITEMAP_URLS",
                $"Sitemap contains {tooOldUrls} URL(s) with lastmod > 2 years old",
                details);
        }
        
        if (invalidPriority > 0)
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem($"URLs with invalid priority: {invalidPriority}")
                .AddItem("❌ Priority must be between 0.0 and 1.0")
                .BeginNested("💡 Recommendations")
                    .AddItem("Fix priority values to be in valid range")
                    .AddItem("Use 1.0 for most important, 0.5 for medium, 0.1 for least")
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("invalidCount", invalidPriority)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Error,
                "INVALID_PRIORITY_VALUES",
                $"Sitemap contains {invalidPriority} URL(s) with invalid priority (must be 0.0-1.0)",
                details);
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
                if (!string.IsNullOrEmpty(url.Address))
                {
                    statusCache[url.Address] = url.Status;
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
                    if (statusCache == null || !statusCache.TryGetValue(sitemapUrl, out var status))
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
                var builder = FindingDetailsBuilder.Create()
                    .AddItem($"Found {errors404.Count} URLs returning 404 Not Found")
                    .AddItem("❌ 404 pages waste crawl budget and confuse indexation");

                builder.BeginNested("📄 Example 404 URLs in sitemap");
                foreach (var url in errors404.Take(10))
                {
                    builder.AddItem(url);
                }
                if (errors404.Count > 10)
                {
                    builder.AddItem($"... and {errors404.Count - 10} more");
                }

                builder.BeginNested("⚠️ Impact")
                    .AddItem("Wastes Googlebot crawl budget")
                    .AddItem("These pages cannot be indexed")
                    .AddItem("May indicate broken internal links");

                builder.BeginNested("💡 Recommendations")
                    .AddItem("Remove 404 URLs from sitemap")
                    .AddItem("Fix the pages or redirect them with 301s")
                    .AddItem("Update sitemap generation logic");

                builder.WithTechnicalMetadata("sitemapUrl", ctx.Url.ToString())
                    .WithTechnicalMetadata("error404Count", errors404.Count)
                    .WithTechnicalMetadata("urls", errors404.Take(20).ToArray());

                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Error,
                    "SITEMAP_CONTAINS_404S",
                    $"Sitemap contains {errors404.Count} URL(s) returning 404 errors",
                    builder.Build());
            }

            // Report 5xx server errors (ERROR severity)
            if (errors5xx.Count > 0)
            {
                var builder = FindingDetailsBuilder.Create()
                    .AddItem($"Found {errors5xx.Count} URLs returning server errors (5xx)")
                    .AddItem("❌ Server errors indicate serious problems");

                builder.BeginNested("📄 URLs with server errors");
                foreach (var url in errors5xx.Take(10))
                {
                    builder.AddItem(url);
                }
                if (errors5xx.Count > 10)
                {
                    builder.AddItem($"... and {errors5xx.Count - 10} more");
                }

                builder.BeginNested("⚠️ Impact")
                    .AddItem("Search engines cannot index error pages")
                    .AddItem("May trigger site-wide quality signals")
                    .AddItem("Wastes crawl budget");

                builder.BeginNested("💡 Recommendations")
                    .AddItem("Fix server errors immediately")
                    .AddItem("Remove broken URLs from sitemap")
                    .AddItem("Investigate server/application issues");

                builder.WithTechnicalMetadata("sitemapUrl", ctx.Url.ToString())
                    .WithTechnicalMetadata("error5xxCount", errors5xx.Count)
                    .WithTechnicalMetadata("urls", errors5xx.Take(20).ToArray());

                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Error,
                    "SITEMAP_CONTAINS_5XX_ERRORS",
                    $"Sitemap contains {errors5xx.Count} URL(s) returning server errors",
                    builder.Build());
            }

            // Report redirects (WARNING severity)
            if (redirects.Count > 0)
            {
                var builder = FindingDetailsBuilder.Create()
                    .AddItem($"Found {redirects.Count} URLs that redirect (3xx)")
                    .AddItem("⚠️ Sitemap should contain final destination URLs");

                builder.BeginNested("📄 Redirecting URLs");
                foreach (var url in redirects.Take(10))
                {
                    builder.AddItem(url);
                }
                if (redirects.Count > 10)
                {
                    builder.AddItem($"... and {redirects.Count - 10} more");
                }

                builder.BeginNested("💡 Recommendations")
                    .AddItem("Update sitemap to point to final destination URLs")
                    .AddItem("Remove redirect URLs and add their targets instead")
                    .AddItem("Saves search engines an extra hop");

                builder.WithTechnicalMetadata("sitemapUrl", ctx.Url.ToString())
                    .WithTechnicalMetadata("redirectCount", redirects.Count)
                    .WithTechnicalMetadata("urls", redirects.Take(20).ToArray());

                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Warning,
                    "SITEMAP_CONTAINS_REDIRECTS",
                    $"Sitemap contains {redirects.Count} URL(s) that redirect",
                    builder.Build());
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

                var builder = FindingDetailsBuilder.Create()
                    .AddItem($"Found {orphanUrls.Count} potential orphan URLs")
                    .AddItem("ℹ️ URLs in sitemap but not discovered through crawling");
                
                builder.BeginNested("📄 Example orphan URLs");
                foreach (var url in orphanUrls.Take(5))
                {
                    builder.AddItem(url);
                }
                if (orphanUrls.Count > 5)
                {
                    builder.AddItem($"... and {orphanUrls.Count - 5} more");
                }
                
                builder.BeginNested("💡 Recommendations")
                    .AddItem("Review these URLs for internal linking")
                    .AddItem("Orphan URLs may not rank well without internal links")
                    .AddItem("Consider adding links from relevant pages");
                
                builder.WithTechnicalMetadata("sitemapUrl", ctx.Url.ToString())
                    .WithTechnicalMetadata("orphanCount", orphanUrls.Count)
                    .WithTechnicalMetadata("orphanUrls", orphanUrls.Take(10).ToList());
                
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Warning,
                    "ORPHAN_SITEMAP_URLS",
                    $"Found {orphanUrls.Count} URL(s) in sitemap that may not be linked internally",
                    builder.Build());
            }

            // Find missing URLs (crawled but not in sitemap)
            var missingFromSitemap = crawledAddresses
                .Where(crawledUrl => !sitemapUrls.Contains(crawledUrl))
                .ToList();

            if (missingFromSitemap.Count > 0)
            {
                _logger.LogInformation("Found {Count} crawled URLs missing from sitemap", missingFromSitemap.Count);

                var details = FindingDetailsBuilder.Create()
                    .AddItem($"Crawled URLs missing from sitemap: {missingFromSitemap.Count}")
                    .AddItem("ℹ️ These URLs exist but aren't in your sitemap")
                    .BeginNested("💡 Recommendations")
                        .AddItem("Consider adding important URLs to your sitemap.xml")
                        .AddItem("Sitemaps help search engines discover all content")
                    .WithTechnicalMetadata("sitemapUrl", ctx.Url.ToString())
                    .WithTechnicalMetadata("missingCount", missingFromSitemap.Count)
                    .Build();
                
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Info,
                    "URLS_MISSING_FROM_SITEMAP",
                    $"Found {missingFromSitemap.Count} crawled URL(s) not listed in sitemap",
                    details);
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

