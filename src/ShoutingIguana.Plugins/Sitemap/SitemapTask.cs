using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Xml.Linq;

namespace ShoutingIguana.Plugins.Sitemap;

/// <summary>
/// XML sitemap discovery, parsing, validation, comparison, and generation.
/// </summary>
public class SitemapTask(ILogger logger, IServiceProvider serviceProvider) : UrlTaskBase
{
    private readonly ILogger _logger = logger;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    
    // Track discovered sitemap URLs per project
    private static readonly ConcurrentDictionary<int, HashSet<string>> SitemapUrlsByProject = new();
    
    // Track if sitemap was found for project
    private static readonly ConcurrentDictionary<int, bool> SitemapFoundByProject = new();

    public override string Key => "Sitemap";
    public override string DisplayName => "XML Sitemap";
    public override int Priority => 45; // Run after basic analysis

    public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
    {
        try
        {
            // Check if this is the base URL - try to discover sitemap
            if (IsBaseUrl(ctx.Url.ToString(), ctx.Project.BaseUrl))
            {
                await DiscoverSitemapAsync(ctx);
            }

            // Check if this is a sitemap file itself
            if (IsSitemapUrl(ctx.Url.ToString()))
            {
                // Check HTTP status first
                var httpStatus = ctx.Metadata.HttpStatus ?? 0;
                
                if (httpStatus == 404)
                {
                    // Sitemap not found
                    await ctx.Findings.ReportAsync(
                        Key,
                        Severity.Warning,
                        "SITEMAP_NOT_FOUND",
                        $"Sitemap not found at {ctx.Url} (HTTP {httpStatus})",
                        new
                        {
                            url = ctx.Url.ToString(),
                            httpStatus,
                            recommendation = "Consider creating an XML sitemap to help search engines discover your content"
                        });
                }
                else if (httpStatus >= 400)
                {
                    // Other error
                    await ctx.Findings.ReportAsync(
                        Key,
                        Severity.Error,
                        "SITEMAP_ERROR",
                        $"Error accessing sitemap at {ctx.Url} (HTTP {httpStatus})",
                        new
                        {
                            url = ctx.Url.ToString(),
                            httpStatus
                        });
                }
                else if (httpStatus >= 200 && httpStatus < 300)
                {
                    // Success - analyze the sitemap
                    await AnalyzeSitemapFileAsync(ctx);
                    
                    // After parsing sitemap, compare with crawled URLs to find orphans
                    await CompareSitemapWithCrawledUrlsAsync(ctx);
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

        // Queue sitemap URL for crawling with high priority
        await ctx.Enqueue.EnqueueAsync(sitemapUrl, depth: 1, priority: 10);

        await ctx.Findings.ReportAsync(
            Key,
            Severity.Info,
            "SITEMAP_DISCOVERY",
            "Sitemap discovery initiated",
            new
            {
                url = ctx.Url.ToString(),
                sitemapUrl,
                note = "Will attempt to fetch sitemap.xml from standard location"
            });
    }

    private async Task AnalyzeSitemapFileAsync(UrlContext ctx)
    {
        if (string.IsNullOrEmpty(ctx.RenderedHtml))
        {
            _logger.LogWarning("Sitemap URL {Url} returned no content", ctx.Url);
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "SITEMAP_EMPTY_CONTENT",
                $"Sitemap at {ctx.Url} returned no content",
                new
                {
                    url = ctx.Url.ToString(),
                    recommendation = "Ensure your sitemap.xml file contains valid XML content"
                });
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
                        await ctx.Findings.ReportAsync(
                            Key,
                            Severity.Error,
                            "SITEMAP_GZIP_ERROR",
                            $"Gzipped sitemap could not be decompressed and content doesn't appear to be XML",
                            new { url = ctx.Url.ToString(), note = "Sitemap may already be decompressed by HTTP client" });
                        return;
                    }
                }
            }
            
            // Check uncompressed size (should be < 50MB)
            var sizeBytes = System.Text.Encoding.UTF8.GetByteCount(xmlContent);
            var sizeMB = sizeBytes / (1024.0 * 1024.0);
            
            if (sizeMB > 50)
            {
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Error,
                    "SITEMAP_SIZE_LIMIT_EXCEEDED",
                    $"Sitemap exceeds 50MB limit (current size: {sizeMB:F2}MB)",
                    new
                    {
                        url = ctx.Url.ToString(),
                        sizeBytes,
                        sizeMB = $"{sizeMB:F2}MB",
                        limit = "50MB",
                        recommendation = "Split into multiple sitemaps using a sitemap index"
                    });
            }
            else if (sizeMB > 40)
            {
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Warning,
                    "SITEMAP_APPROACHING_SIZE_LIMIT",
                    $"Sitemap approaching 50MB limit (current size: {sizeMB:F2}MB)",
                    new
                    {
                        url = ctx.Url.ToString(),
                        sizeMB = $"{sizeMB:F2}MB",
                        limit = "50MB",
                        recommendation = "Consider splitting before reaching the limit"
                    });
            }

            // Parse XML sitemap
            var doc = XDocument.Parse(xmlContent);
            var ns = doc.Root?.Name.Namespace;

            if (ns == null)
            {
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Warning,
                    "INVALID_SITEMAP_XML",
                    "Sitemap XML is missing namespace",
                    new { url = ctx.Url.ToString() });
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
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Error,
                "SITEMAP_PARSE_ERROR",
                $"Failed to parse sitemap XML: {ex.Message}",
                new { url = ctx.Url.ToString(), error = ex.Message });
        }
    }

    private async Task AnalyzeSitemapIndexAsync(UrlContext ctx, XDocument doc, XNamespace ns)
    {
        var sitemapElements = doc.Descendants(ns + "sitemap").ToList();

        await ctx.Findings.ReportAsync(
            Key,
            Severity.Info,
            "SITEMAP_INDEX_FOUND",
            $"Sitemap index contains {sitemapElements.Count} sitemap(s)",
            new
            {
                url = ctx.Url.ToString(),
                sitemapCount = sitemapElements.Count
            });

        // Extract and queue child sitemaps
        foreach (var sitemapElement in sitemapElements)
        {
            var locElement = sitemapElement.Element(ns + "loc");
            if (locElement != null)
            {
                var sitemapUrl = locElement.Value;
                _logger.LogInformation("Found child sitemap: {SitemapUrl}", sitemapUrl);
                
                // Queue for crawling with high priority
                await ctx.Enqueue.EnqueueAsync(sitemapUrl, depth: ctx.Metadata.Depth + 1, priority: 10);
            }
        }
    }

    private async Task AnalyzeSitemapUrlsAsync(UrlContext ctx, XDocument doc, XNamespace ns)
    {
        var urlElements = doc.Descendants(ns + "url").ToList();

        if (urlElements.Count == 0)
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "EMPTY_SITEMAP",
                "Sitemap contains no URLs",
                new { url = ctx.Url.ToString() });
            return;
        }

        // Check 50,000 URL limit
        if (urlElements.Count > 50000)
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Error,
                "SITEMAP_URL_LIMIT_EXCEEDED",
                $"Sitemap exceeds 50,000 URL limit (contains {urlElements.Count} URLs)",
                new
                {
                    url = ctx.Url.ToString(),
                    urlCount = urlElements.Count,
                    limit = 50000,
                    recommendation = "Split into multiple sitemaps using a sitemap index"
                });
        }
        else if (urlElements.Count > 45000)
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "SITEMAP_APPROACHING_URL_LIMIT",
                $"Sitemap approaching 50,000 URL limit (contains {urlElements.Count} URLs)",
                new
                {
                    url = ctx.Url.ToString(),
                    urlCount = urlElements.Count,
                    limit = 50000,
                    recommendation = "Consider splitting before reaching the limit"
                });
        }

        await ctx.Findings.ReportAsync(
            Key,
            Severity.Info,
            "SITEMAP_FOUND",
            $"Sitemap contains {urlElements.Count} URL(s)",
            new
            {
                url = ctx.Url.ToString(),
                urlCount = urlElements.Count
            });

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
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Warning,
                    "ALL_PRIORITY_MAX",
                    "All URLs have priority 1.0 (defeats the purpose of priorities)",
                    new
                    {
                        url = ctx.Url.ToString(),
                        recommendation = "Use priority to indicate relative importance of URLs (0.0 to 1.0)"
                    });
            }
        }

        if (invalidUrls > 0)
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "INVALID_SITEMAP_URLS",
                $"Sitemap contains {invalidUrls} invalid URL(s)",
                new
                {
                    url = ctx.Url.ToString(),
                    invalidCount = invalidUrls,
                    recommendation = "Remove or fix invalid URLs in sitemap"
                });
        }
        
        if (futureLastmod > 0)
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "FUTURE_LASTMOD_DATES",
                $"Sitemap contains {futureLastmod} URL(s) with lastmod dates in the future",
                new
                {
                    url = ctx.Url.ToString(),
                    futureCount = futureLastmod,
                    recommendation = "lastmod dates should not be in the future"
                });
        }

        if (tooOldUrls > 10)
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "OUTDATED_SITEMAP_URLS",
                $"Sitemap contains {tooOldUrls} URL(s) with lastmod > 2 years old",
                new
                {
                    url = ctx.Url.ToString(),
                    outdatedCount = tooOldUrls,
                    recommendation = "Consider removing or updating old URLs"
                });
        }
        
        if (invalidPriority > 0)
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Error,
                "INVALID_PRIORITY_VALUES",
                $"Sitemap contains {invalidPriority} URL(s) with invalid priority (must be 0.0-1.0)",
                new
                {
                    url = ctx.Url.ToString(),
                    invalidCount = invalidPriority,
                    recommendation = "Priority must be between 0.0 and 1.0"
                });
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

            // Get URL repository to check which sitemap URLs were actually crawled
            var urlRepoType = Type.GetType("ShoutingIguana.Core.Repositories.IUrlRepository, ShoutingIguana.Core");
            if (urlRepoType == null)
            {
                _logger.LogDebug("Unable to load URL repository for sitemap comparison");
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var urlRepo = scope.ServiceProvider.GetService(urlRepoType);
            if (urlRepo == null)
            {
                return;
            }

            // Get all crawled URLs for the project
            var getByProjectMethod = urlRepoType.GetMethod("GetByProjectIdAsync");
            if (getByProjectMethod == null)
            {
                return;
            }

            var taskObj = getByProjectMethod.Invoke(urlRepo, new object[] { projectId });
            if (taskObj is Task task)
            {
                await task.ConfigureAwait(false);
                
                var resultProperty = task.GetType().GetProperty("Result");
                var allUrls = resultProperty?.GetValue(task) as System.Collections.IEnumerable;

                if (allUrls != null)
                {
                    // Build set of crawled URL addresses
                    var crawledAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var crawledUrlsWithNoInlinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var url in allUrls)
                    {
                        var urlType = url.GetType();
                        var address = urlType.GetProperty("Address")?.GetValue(url) as string;
                        var depth = (int)(urlType.GetProperty("Depth")?.GetValue(url) ?? 0);

                        if (!string.IsNullOrEmpty(address))
                        {
                            crawledAddresses.Add(address);
                            
                            // URLs at depth > 0 with no inlinks might be orphans
                            if (depth > 0)
                            {
                                crawledUrlsWithNoInlinks.Add(address);
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

                        // Report summary finding
                        await ctx.Findings.ReportAsync(
                            Key,
                            Severity.Warning,
                            "ORPHAN_SITEMAP_URLS",
                            $"Found {orphanUrls.Count} URL(s) in sitemap that may not be linked internally",
                            new
                            {
                                sitemapUrl = ctx.Url.ToString(),
                                orphanCount = orphanUrls.Count,
                                orphanUrls = orphanUrls.Take(10).ToList(), // Limit to first 10 in JSON
                                recommendation = "Review these URLs to ensure they are properly linked in your site navigation"
                            });
                    }

                    // Find missing URLs (crawled but not in sitemap)
                    var missingFromSitemap = crawledAddresses
                        .Where(crawledUrl => !sitemapUrls.Contains(crawledUrl))
                        .ToList();

                    if (missingFromSitemap.Count > 0)
                    {
                        _logger.LogInformation("Found {Count} crawled URLs missing from sitemap", missingFromSitemap.Count);

                        await ctx.Findings.ReportAsync(
                            Key,
                            Severity.Info,
                            "URLS_MISSING_FROM_SITEMAP",
                            $"Found {missingFromSitemap.Count} crawled URL(s) not listed in sitemap",
                            new
                            {
                                sitemapUrl = ctx.Url.ToString(),
                                missingCount = missingFromSitemap.Count,
                                recommendation = "Consider adding important URLs to your sitemap.xml"
                            });
                    }

                    _logger.LogDebug("Sitemap comparison complete: {OrphanCount} orphans, {MissingCount} missing",
                        orphanUrls.Count, missingFromSitemap.Count);
                }
            }
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
        _logger.LogDebug("Cleaned up sitemap data for project {ProjectId}", projectId);
    }
}

