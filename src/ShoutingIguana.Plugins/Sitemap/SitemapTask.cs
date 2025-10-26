using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;
using System.Collections.Concurrent;
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
                await AnalyzeSitemapFileAsync(ctx);
                
                // After parsing sitemap, compare with crawled URLs to find orphans
                await CompareSitemapWithCrawledUrlsAsync(ctx);
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
            return;
        }

        try
        {
            // Parse XML sitemap
            var xmlContent = ctx.RenderedHtml;
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
                    // Check if URL is very old (>2 years)
                    if ((DateTime.UtcNow - lastmod).TotalDays > 730)
                    {
                        tooOldUrls++;
                    }
                }
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

