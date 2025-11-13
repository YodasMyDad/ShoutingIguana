using System;
using System.Collections.Concurrent;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;
using ShoutingIguana.PluginSdk.Helpers;
using ShoutingIguana.Plugins.Shared;

namespace ShoutingIguana.Plugins.BrokenLinks;

/// <summary>
/// Analyzes pages for broken links (404s, 500s, etc.) with comprehensive diagnostics.
/// </summary>
public class BrokenLinksTask(ILogger logger, IBrokenLinksChecker checker, IRepositoryAccessor repositoryAccessor, bool checkExternalLinks = false, bool checkAnchorLinks = true) : UrlTaskBase, IDisposable
{
    private readonly ILogger _logger = logger;
    private readonly IBrokenLinksChecker _checker = checker;
    private readonly IRepositoryAccessor _repositoryAccessor = repositoryAccessor;
    private readonly ExternalLinkChecker _externalChecker = new ExternalLinkChecker(logger, TimeSpan.FromSeconds(5));
    private readonly bool _checkExternalLinks = checkExternalLinks;
    private readonly bool _checkAnchorLinks = checkAnchorLinks;
    private bool _disposed;
    
    // Cache URL statuses per project to avoid database queries for every link (critical for performance)
    private static readonly ConcurrentDictionary<int, Dictionary<string, int>> UrlStatusCacheByProject = new();
    
    // Semaphore to ensure only one thread loads URL statuses per project
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> UrlStatusLoadingSemaphores = new();

    public override string Key => "BrokenLinks";
    public override string DisplayName => "Broken Links";
    public override string Description => "Detects broken links, 404 errors, and missing resources on your pages";
    public override int Priority => 50;

    public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.RenderedHtml))
        {
            return;
        }

        // Only analyze successful pages
        if (ctx.Metadata.StatusCode < 200 || ctx.Metadata.StatusCode >= 300)
        {
            return;
        }

        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(ctx.RenderedHtml);

            // Track findings to deduplicate them
            var findingsMap = new Dictionary<string, FindingTracker>();
            
            // CRITICAL PERFORMANCE FIX: Pre-load URL status cache for this project
            // This prevents database query for every link (50+ queries per page = massive bottleneck!)
            await EnsureUrlStatusCacheLoadedAsync(ctx.Project.ProjectId, ct);

            // FIRST: Load all links from the Links table (Phase 1 captured data) - ONCE
            // This includes ALL resources that were discovered during crawling, including
            // stylesheets, scripts, images, etc. that may have returned 404
            var storedLinks = await _repositoryAccessor.GetLinksByFromUrlAsync(ctx.Project.ProjectId, ctx.Metadata.UrlId);
            
            // Check all links from the Links table
            // This ensures we catch resources like stylesheets that may have been requested
            // by the browser but failed to load (404) and may not be in final HTML
            var checkedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            foreach (var storedLink in storedLinks)
            {
                checkedUrls.Add(storedLink.ToUrl);
                
                var linkInfo = new LinkInfo
                {
                    Url = storedLink.ToUrl,
                    AnchorText = storedLink.AnchorText ?? "",
                    LinkType = storedLink.LinkType.ToLowerInvariant(),
                    HasNofollow = false // Will be determined if needed
                };
                
                await CheckLinkAsync(ctx, linkInfo, findingsMap, ct);
            }
            
            // SECOND: Extract links from HTML to catch any that might not be in Links table
            // (edge cases, dynamically generated links, etc.)
            var htmlLinks = await ExtractLinksFromHtmlAsync(doc, ctx);
            
            foreach (var link in htmlLinks)
            {
                // Skip if already checked from Links table
                if (checkedUrls.Contains(link.Url))
                {
                    continue;
                }
                
                await CheckLinkAsync(ctx, link, findingsMap, ct);
            }

            // Report all unique findings with occurrence counts
            await ReportUniqueFindings(ctx, findingsMap);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing broken links for {Url}", ctx.Url);
        }
    }

    private Task<List<LinkInfo>> ExtractLinksFromHtmlAsync(HtmlDocument doc, UrlContext ctx)
    {
        var links = new List<LinkInfo>();
        
        // Extract base tag if present (respects browser behavior for relative URLs)
        Uri? baseTagUri = UrlHelper.ExtractBaseTag(ctx.RenderedHtml!, ctx.Url);

        // Hyperlinks (a tags)
        var aNodes = doc.DocumentNode.SelectNodes("//a[@href]");
        if (aNodes != null)
        {
            foreach (var node in aNodes)
            {
                var href = node.GetAttributeValue("href", "");
                var anchorText = node.InnerText?.Trim() ?? "";
                var rel = node.GetAttributeValue("rel", "");
                
                if (!string.IsNullOrEmpty(href) && !href.StartsWith("javascript:") && !href.StartsWith("mailto:") && !href.StartsWith("tel:"))
                {
                    // Handle anchor links
                    if (href.StartsWith("#"))
                    {
                        if (_checkAnchorLinks && href.Length > 1)
                        {
                    links.Add(new LinkInfo
                    {
                        Url = ctx.Url + href,
                        AnchorText = anchorText,
                        LinkType = "anchor",
                        HasNofollow = rel.Contains("nofollow", StringComparison.OrdinalIgnoreCase),
                        AnchorId = href.Substring(1)
                    });
                        }
                        continue;
                    }
                    
                    links.Add(new LinkInfo
                    {
                        Url = ResolveUrl(ctx.Url, href, baseTagUri),
                        AnchorText = anchorText,
                        LinkType = "hyperlink",
                        HasNofollow = rel.Contains("nofollow", StringComparison.OrdinalIgnoreCase)
                    });
                }
            }
        }

        // Images (img tags)
        var imgNodes = doc.DocumentNode.SelectNodes("//img[@src]");
        if (imgNodes != null)
        {
            foreach (var node in imgNodes)
            {
                var src = node.GetAttributeValue("src", "");
                var alt = node.GetAttributeValue("alt", "");
                
                if (!string.IsNullOrEmpty(src) && !src.StartsWith("data:"))
                {
                    links.Add(new LinkInfo
                    {
                        Url = ResolveUrl(ctx.Url, src, baseTagUri),
                        AnchorText = alt,
                        LinkType = "image"
                    });
                }
            }
        }

        // CSS files (link tags with rel=stylesheet)
        var linkNodes = doc.DocumentNode.SelectNodes("//link[@rel='stylesheet'][@href]");
        if (linkNodes != null)
        {
            foreach (var node in linkNodes)
            {
                var href = node.GetAttributeValue("href", "");
                if (!string.IsNullOrEmpty(href) && !href.StartsWith("data:"))
                {
                    links.Add(new LinkInfo
                    {
                        Url = ResolveUrl(ctx.Url, href, baseTagUri),
                        LinkType = "stylesheet"
                    });
                }
            }
        }

        // JavaScript files (script tags with src)
        var scriptNodes = doc.DocumentNode.SelectNodes("//script[@src]");
        if (scriptNodes != null)
        {
            foreach (var node in scriptNodes)
            {
                var src = node.GetAttributeValue("src", "");
                if (!string.IsNullOrEmpty(src) && !src.StartsWith("data:"))
                {
                    links.Add(new LinkInfo
                    {
                        Url = ResolveUrl(ctx.Url, src, baseTagUri),
                        LinkType = "script"
                    });
                }
            }
        }

        return Task.FromResult(links);
    }

    /// <summary>
    /// Ensures URL status cache is loaded for the project (loads once, thread-safe).
    /// CRITICAL for performance: prevents database query for every link checked.
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
            _logger.LogInformation("Loading URL status cache for project {ProjectId} (first time)", projectId);
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
            _logger.LogInformation("Cached {Count} URL statuses for project {ProjectId}", statusCache.Count, projectId);
        }
        finally
        {
            semaphore.Release();
        }
    }
    
    /// <summary>
    /// Gets URL status from cache (fast lookup, no database query).
    /// Returns null if URL not found in cache.
    /// </summary>
    private int? GetUrlStatusFromCache(int projectId, string url)
    {
        if (UrlStatusCacheByProject.TryGetValue(projectId, out var cache))
        {
            // Normalize URL before lookup to ensure consistency with database
            var normalizedUrl = NormalizeUrlForCache(url);
            if (cache.TryGetValue(normalizedUrl, out var status))
            {
                return status;
            }
        }
        return null;
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
    

    private async Task CheckLinkAsync(UrlContext ctx, LinkInfo link, Dictionary<string, FindingTracker> findingsMap, CancellationToken ct)
    {
        bool isExternal = IsExternalLink(ctx.Project.BaseUrl, link.Url);
        var displayLinkType = string.IsNullOrWhiteSpace(link.LinkType) ? "link" : link.LinkType;

        // Check anchor links
        if (link.LinkType == "anchor" && !string.IsNullOrEmpty(link.AnchorId))
        {
            await CheckAnchorLinkAsync(ctx, link, findingsMap, ct);
            return;
        }

        int? status = null;
        bool? hasRedirect = null;

        if (!isExternal)
        {
            // Internal link - check from cache (FAST - no database query!)
            // Phase 2: All internal resources are guaranteed to be crawled and in database
            status = GetUrlStatusFromCache(ctx.Project.ProjectId, link.Url);
            
            // CRITICAL: If status is null, it means the URL is not in the database or hasn't been crawled
            // This could be because MaxUrlsToCrawl was reached before this URL was crawled
            if (!status.HasValue)
            {
                // Report as warning - URL wasn't crawled, not necessarily broken
                var key = $"{link.Url}|LINK_NOT_CRAWLED";
                
                TrackFinding(findingsMap,
                    key,
                    Severity.Warning,
                    "LINK_NOT_CRAWLED",
                    $"Link target {link.Url} was not crawled yet, so we cannot confirm the status of this {displayLinkType}. Re-run the crawl or validate the URL manually.",
                    link.Url,
                    link.AnchorText,
                    link.LinkType,
                    "Not Crawled",
                    isExternal);
                
                return; // Don't continue checking this link
            }
            
            // Check if this URL has a redirect (use cache instead of database query)
            var urlWithAnchor = link.Url.Split('#')[0];
            var redirectStatus = GetUrlStatusFromCache(ctx.Project.ProjectId, urlWithAnchor);
            hasRedirect = redirectStatus.HasValue && redirectStatus.Value >= 300 && redirectStatus.Value < 400;
        }
        else if (_checkExternalLinks)
        {
            // External link - check via HTTP using project's User-Agent setting
            var result = await _externalChecker.CheckUrlAsync(link.Url, ctx.Project.UserAgent, ct);
            status = result.StatusCode;
            
            // Report slow external links
            if (result.IsSuccess && result.ResponseTime.TotalSeconds > 5)
            {
                var key = $"{link.Url}|SLOW_EXTERNAL_LINK";
                TrackFinding(findingsMap,
                    key,
                    Severity.Info,
                    "SLOW_EXTERNAL_LINK",
                    $"External {displayLinkType} is slow to respond ({result.ResponseTime.TotalSeconds:F1}s) and can delay page load; consider optimizing, caching, or replacing this resource: {link.Url}",
                    link.Url,
                    link.AnchorText,
                    link.LinkType,
                    "200 OK (Slow)",
                    true);
            }
        }

        // Report broken or restricted links
        if (status.HasValue && (status.Value >= 400 || status.Value == 0))
        {
            // Categorize status codes
            var isRestricted = status.Value == 401 || status.Value == 403 || status.Value == 405 || status.Value == 451;
            var isConnectionFailed = status.Value == 0;
            
            // Connection failures are warnings (could be transient), restricted access is info, true errors are errors
            var severity = isConnectionFailed ? Severity.Warning : (isRestricted ? Severity.Info : Severity.Error);
            var statusText = status.Value == 0 ? "Connection Failed" : status.Value.ToString();
            
            string code;
            string message;
            
            if (isConnectionFailed)
            {
                // Connection failures - warnings (transient issues)
                code = $"CONNECTION_FAILED_{link.LinkType.ToUpperInvariant()}";
                message = $"Cannot reach {link.Url}; the {displayLinkType} failed to connect, so visitors will hit a missing resource. Confirm the host is online or update the link.";
            }
            else if (isRestricted)
            {
                // Restricted pages - informational only
                code = status.Value switch
                {
                    401 => "AUTH_REQUIRED_LINK",
                    403 => "FORBIDDEN_LINK",
                    405 => "METHOD_NOT_ALLOWED_LINK",
                    451 => "UNAVAILABLE_LEGAL",
                    _ => "RESTRICTED_LINK"
                };
                
                var restrictionType = status.Value switch
                {
                    401 => "requires authentication",
                    403 => "is forbidden/restricted",
                    405 => "does not allow HEAD requests",
                    451 => "is unavailable for legal reasons",
                    _ => "is restricted"
                };
                
                message = $"Restricted {displayLinkType} ({restrictionType}): {link.Url} returns HTTP {status.Value}. Public visitors cannot access it, so remove or replace the link or provide credentials.";
            }
            else
            {
                // Broken links - errors
                code = $"BROKEN_{link.LinkType.ToUpperInvariant()}";
                message = $"Broken {displayLinkType}: {link.Url} returns HTTP {statusText}, so visitors land on an error page. Update or remove the link to keep navigation working.";
            }
            
            // Dedupe by link URL, code, and status code
            var key = $"{link.Url}|{code}|{status.Value}";
            
            TrackFinding(findingsMap,
                key,
                severity,
                code,
                message,
                link.Url,
                link.AnchorText,
                link.LinkType,
                statusText,
                isExternal);
        }
        // Report redirect chains for internal links
        else if (hasRedirect == true && !isExternal)
        {
            var key = $"{link.Url}|LINK_TO_REDIRECT";
            TrackFinding(findingsMap,
                key,
                Severity.Info,
                "LINK_TO_REDIRECT",
                $"This {displayLinkType} points to {link.Url}, which redirects before serving content. Link directly to the final destination to avoid extra hops and keep the navigation snappy.",
                link.Url,
                link.AnchorText,
                link.LinkType,
                status.HasValue ? status.Value.ToString() : "Unknown",
                isExternal);
        }
        // Report nofollow on internal links (SEO issue)
        else if (link.HasNofollow && !isExternal && link.LinkType == "hyperlink")
        {
            var key = $"{link.Url}|NOFOLLOW_INTERNAL_LINK";
            TrackFinding(findingsMap,
                key,
                Severity.Warning,
                "NOFOLLOW_INTERNAL_LINK",
                $"Internal {displayLinkType} includes rel=\"nofollow\", so crawlers won't pass link equity through it. Remove the attribute if the link should stay crawlable: {link.Url}",
                link.Url,
                link.AnchorText,
                link.LinkType,
                status.HasValue ? status.Value.ToString() : "Unknown",
                isExternal);
        }
    }

    private async Task CheckAnchorLinkAsync(UrlContext ctx, LinkInfo link, Dictionary<string, FindingTracker> findingsMap, CancellationToken _)
    {
        if (string.IsNullOrEmpty(link.AnchorId))
            return;

        try
        {
            bool anchorExists = false;
            
            // Check if the target anchor exists - works in both Phase 1 and Phase 2
            if (ctx.Page != null)
            {
                // Phase 1: Use live browser page for accurate detection
                var targetElement = await ctx.Page.QuerySelectorAsync($"#{link.AnchorId}, [name='{link.AnchorId}']");
                anchorExists = targetElement != null;
            }
            else if (!string.IsNullOrEmpty(ctx.RenderedHtml))
            {
                // Phase 2: Check saved HTML using HtmlAgilityPack
                var doc = new HtmlDocument();
                doc.LoadHtml(ctx.RenderedHtml);
                
                // Check for id attribute or name attribute matching the anchor
                var targetElement = doc.DocumentNode.SelectSingleNode($"//*[@id='{link.AnchorId}'] | //*[@name='{link.AnchorId}']");
                anchorExists = targetElement != null;
            }
            
            if (!anchorExists)
            {
                // Dedupe by anchor ID only
                var key = $"#{link.AnchorId}|BROKEN_ANCHOR_LINK";
                TrackFinding(findingsMap,
                key,
                Severity.Warning,
                "BROKEN_ANCHOR_LINK",
                $"Anchor link #{link.AnchorId} does not exist on this page, so clicking it will not move the user. Add a matching id or name attribute or remove the reference.",
                    ctx.Url.ToString() + "#" + link.AnchorId,
                    link.AnchorText,
                    "anchor",
                    "Anchor Not Found",
                    false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error checking anchor link: {AnchorId}", link.AnchorId);
        }
    }

    /// <summary>
    /// Track a finding in the deduplication map. If the same finding already exists, increment its occurrence count.
    /// </summary>
    private void TrackFinding(Dictionary<string, FindingTracker> findingsMap, string key, Severity severity, string code, string message, string targetUrl, string anchorText, string linkType, string httpStatus, bool isExternal)
    {
        if (findingsMap.TryGetValue(key, out var existing))
        {
            // Increment occurrence count for duplicate findings
            existing.OccurrenceCount++;
        }
        else
        {
            // Add new unique finding
            findingsMap[key] = new FindingTracker
            {
                Severity = severity,
                Code = code,
                Message = message,
                TargetUrl = targetUrl,
                AnchorText = anchorText,
                LinkType = linkType,
                HttpStatus = httpStatus,
                IsExternal = isExternal,
                OccurrenceCount = 1
            };
        }
    }

    /// <summary>
    /// Converts issue codes to friendly readable text for display in the UI.
    /// </summary>
    private static string ConvertCodeToFriendlyText(string code, string linkType)
    {
        return code switch
        {
            "FORBIDDEN_LINK" => "Forbidden Link",
            "AUTH_REQUIRED_LINK" => "Authentication Required",
            "METHOD_NOT_ALLOWED_LINK" => "Method Not Allowed",
            "UNAVAILABLE_LEGAL" => "Unavailable for Legal Reasons",
            "LINK_NOT_CRAWLED" => "Link Not Crawled",
            "SLOW_EXTERNAL_LINK" => "Slow External Link",
            "NOFOLLOW_INTERNAL_LINK" => "Nofollow on Internal Link",
            "BROKEN_ANCHOR_LINK" => "Broken Anchor Link",
            "LINK_TO_REDIRECT" => "Link Points to Redirect",
            "RESTRICTED_LINK" => "Restricted Link",
            _ when code.StartsWith("CONNECTION_FAILED_", StringComparison.OrdinalIgnoreCase) =>
                $"Connection Failed ({CapitalizeLinkType(linkType)})",
            _ when code.StartsWith("BROKEN_", StringComparison.OrdinalIgnoreCase) =>
                $"Broken {CapitalizeLinkType(linkType)}",
            _ => code // Fallback to code if no match
        };
    }

    /// <summary>
    /// Capitalizes link type for display (e.g., "hyperlink" -> "Hyperlink").
    /// </summary>
    private static string CapitalizeLinkType(string linkType)
    {
        if (string.IsNullOrWhiteSpace(linkType))
            return "Link";
        
        return linkType switch
        {
            "hyperlink" => "Hyperlink",
            "image" => "Image",
            "stylesheet" => "Stylesheet",
            "script" => "Script",
            "anchor" => "Anchor",
            _ => char.ToUpperInvariant(linkType[0]) + (linkType.Length > 1 ? linkType.Substring(1) : "")
        };
    }

    /// <summary>
    /// Report all unique findings as structured report rows for easy scanning.
    /// </summary>
    private async Task ReportUniqueFindings(UrlContext ctx, Dictionary<string, FindingTracker> findingsMap)
    {
        foreach (var kvp in findingsMap.Values)
        {
            var tracker = kvp;
            
            // Create report row with SEO-friendly columns using data from FindingTracker
            var linkText = string.IsNullOrWhiteSpace(tracker.AnchorText)
                ? tracker.TargetUrl
                : tracker.AnchorText;

            // Convert code to friendly text for display
            var friendlyIssue = ConvertCodeToFriendlyText(tracker.Code, tracker.LinkType);

            var row = ReportRow.Create()
                .SetSeverity(tracker.Severity)
                .Set("Issue", friendlyIssue)
                .Set("Description", tracker.Message)
                .Set("LinkedFrom", ctx.Url.ToString())
                .Set("BrokenLink", tracker.TargetUrl)
                .Set("Status", tracker.HttpStatus)
                .Set("LinkText", linkText)
                .Set("LinkType", tracker.LinkType)
                .Set("IsExternal", tracker.IsExternal ? "Yes" : "No")
                .Set("Occurrences", tracker.OccurrenceCount);
            
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
        }
    }

    /// <summary>
    /// Cleanup per-project data when project is closed.
    /// </summary>
    public override void CleanupProject(int projectId)
    {
        UrlStatusCacheByProject.TryRemove(projectId, out _);
        
        // Cleanup and dispose semaphore
        if (UrlStatusLoadingSemaphores.TryRemove(projectId, out var semaphore))
        {
            semaphore.Dispose();
        }
        
        _logger.LogDebug("Cleaned up broken links cache for project {ProjectId}", projectId);
    }
    
    /// <summary>
    /// Dispose of resources, particularly the ExternalLinkChecker HttpClient.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _externalChecker.Dispose();
        _disposed = true;
    }
    
    private string ResolveUrl(Uri baseUri, string relativeUrl, Uri? baseTagUri = null)
    {
        return UrlHelper.Resolve(baseUri, relativeUrl, baseTagUri);
    }

    private bool IsExternalLink(string baseUrl, string targetUrl)
    {
        return UrlHelper.IsExternal(baseUrl, targetUrl);
    }
    
    // Private classes at end
    private class FindingTracker
    {
        public Severity Severity { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string TargetUrl { get; set; } = string.Empty;
        public string AnchorText { get; set; } = string.Empty;
        public string LinkType { get; set; } = string.Empty;
        public string HttpStatus { get; set; } = string.Empty;
        public bool IsExternal { get; set; }
        public int OccurrenceCount { get; set; } = 1;
    }
    
    private class LinkInfo
    {
        public string Url { get; set; } = string.Empty;
        public string AnchorText { get; set; } = string.Empty;
        public string LinkType { get; set; } = string.Empty;
        public bool HasNofollow { get; set; }
        public string? AnchorId { get; set; }
    }
}

