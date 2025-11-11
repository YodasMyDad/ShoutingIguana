using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;
using ShoutingIguana.PluginSdk.Helpers;
using ShoutingIguana.Plugins.Shared;

namespace ShoutingIguana.Plugins.BrokenLinks;

/// <summary>
/// Analyzes pages for broken links (404s, 500s, etc.) with comprehensive diagnostics.
/// </summary>
public class BrokenLinksTask : UrlTaskBase, IDisposable
{
    private readonly ILogger _logger;
    private readonly IBrokenLinksChecker _checker;
    private readonly IRepositoryAccessor _repositoryAccessor;
    private readonly ExternalLinkChecker _externalChecker;
    private readonly bool _checkExternalLinks;
    private readonly bool _checkAnchorLinks;
    private bool _disposed;
    
    // Cache URL statuses per project to avoid database queries for every link (critical for performance)
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, Dictionary<string, int>> UrlStatusCacheByProject = new();
    
    // Semaphore to ensure only one thread loads URL statuses per project
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, SemaphoreSlim> UrlStatusLoadingSemaphores = new();

    // Helper class to track unique findings with occurrence counts
    private class FindingTracker
    {
        public Severity Severity { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public FindingDetails? Data { get; set; }
        public int OccurrenceCount { get; set; } = 1;
    }

    public BrokenLinksTask(ILogger logger, IBrokenLinksChecker checker, IRepositoryAccessor repositoryAccessor, bool checkExternalLinks = false, bool checkAnchorLinks = true)
    {
        _logger = logger;
        _checker = checker;
        _repositoryAccessor = repositoryAccessor;
        _checkExternalLinks = checkExternalLinks;
        _checkAnchorLinks = checkAnchorLinks;
        
        // Always create ExternalLinkChecker - needed for external link checking
        _externalChecker = new ExternalLinkChecker(logger, TimeSpan.FromSeconds(5));
    }

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
            
            // Extract diagnostic metadata from stored links
            var linkDiagnostics = ExtractLinkDiagnostics(storedLinks);
            
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
                
                var diagnosticInfo = linkDiagnostics.GetValueOrDefault(storedLink.ToUrl);
                await CheckLinkAsync(ctx, linkInfo, diagnosticInfo, findingsMap, ct);
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
                
                var diagnosticInfo = linkDiagnostics.GetValueOrDefault(link.Url);
                await CheckLinkAsync(ctx, link, diagnosticInfo, findingsMap, ct);
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
    
    /// <summary>
    /// Extracts diagnostic metadata from stored links (already loaded from database).
    /// Returns a dictionary mapping link URL to diagnostic info.
    /// </summary>
    private Dictionary<string, ElementDiagnosticInfo> ExtractLinkDiagnostics(List<PluginSdk.LinkInfo> storedLinks)
    {
        var diagnostics = new Dictionary<string, ElementDiagnosticInfo>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var storedLink in storedLinks)
        {
            // Only add if we have diagnostic data
            if (!string.IsNullOrEmpty(storedLink.DomPath))
            {
                var diagnosticInfo = new ElementDiagnosticInfo
                {
                    TagName = storedLink.ElementTag ?? "unknown",
                    DomPath = storedLink.DomPath,
                    IsVisible = storedLink.IsVisible ?? true,
                    BoundingBox = (storedLink.PositionX.HasValue && storedLink.PositionY.HasValue)
                        ? new BoundingBoxInfo
                        {
                            X = storedLink.PositionX.Value,
                            Y = storedLink.PositionY.Value,
                            Width = storedLink.ElementWidth ?? 0,
                            Height = storedLink.ElementHeight ?? 0
                        }
                        : null,
                    HtmlContext = storedLink.HtmlSnippet ?? "",
                    ParentElement = !string.IsNullOrEmpty(storedLink.ParentTag)
                        ? new ParentElementInfo { TagName = storedLink.ParentTag }
                        : null
                };
                
                diagnostics[storedLink.ToUrl] = diagnosticInfo;
            }
        }
        
        _logger.LogDebug("Extracted diagnostics for {Count} links", diagnostics.Count);
        
        return diagnostics;
    }

    private async Task CheckLinkAsync(UrlContext ctx, LinkInfo link, ElementDiagnosticInfo? diagnosticInfo, Dictionary<string, FindingTracker> findingsMap, CancellationToken ct)
    {
        bool isExternal = IsExternalLink(ctx.Project.BaseUrl, link.Url);

        // Check anchor links
        if (link.LinkType == "anchor" && !string.IsNullOrEmpty(link.AnchorId))
        {
            await CheckAnchorLinkAsync(ctx, link, diagnosticInfo, findingsMap, ct);
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
                var impactNote = "This internal link was discovered during the crawl but was not checked because the MaxUrlsToCrawl limit was reached.";
                var recommendation = "To check this URL, increase the 'Maximum URLs to Crawl' setting in your project settings and run the crawl again. Alternatively, this could indicate a broken link if the URL is invalid.";
                
                TrackFinding(findingsMap,
                    key,
                    Severity.Warning,
                    "LINK_NOT_CRAWLED",
                    $"Uncrawled {link.LinkType}: {link.Url} (not found in crawl database)",
                    CreateFindingData(ctx, link, null, false, diagnosticInfo, recommendation: recommendation, impactNote: impactNote));
                
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
                    $"External {link.LinkType} is slow to respond ({result.ResponseTime.TotalSeconds:F1}s): {link.Url}",
                    CreateFindingData(ctx, link, status, isExternal, diagnosticInfo, responseTime: result.ResponseTime));
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
                message = $"{link.LinkType} connection failed: {link.Url} (could be temporary)";
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
                
                message = $"Link to restricted area: {link.Url} {restrictionType} (HTTP {status.Value})";
            }
            else
            {
                // Broken links - errors
                code = $"BROKEN_{link.LinkType.ToUpperInvariant()}";
                message = $"Broken {link.LinkType}: {link.Url} returns {statusText}";
            }
            
            // Dedupe by link URL, code, and status code
            var key = $"{link.Url}|{code}|{status.Value}";
            
            // Generate recommendations based on the issue
            string? recommendation = null;
            string? impactNote = null;
            if (isConnectionFailed)
            {
                // Connection failure recommendations
                recommendation = "Connection failures can be temporary network issues, DNS problems, or server unavailability. Try checking the link again later. If this persists, the resource may be permanently unavailable.";
                impactNote = "Temporary connection failures don't necessarily indicate a broken link, but persistent failures harm user experience.";
            }
            else if (isRestricted)
            {
                var note = status.Value switch
                {
                    401 => "This page requires authentication/login to access.",
                    403 => "This page is restricted and access is forbidden.",
                    405 => "This resource does not allow HEAD requests (common for external scripts and APIs).",
                    451 => "This page is unavailable for legal reasons.",
                    _ => "This page has restricted access."
                };
                
                // Add special note for external 403 errors (common with social media)
                if (status.Value == 403 && isExternal)
                {
                    recommendation = $"{note} For external links (especially social media platforms like Twitter/X, LinkedIn, etc.), this often means the site blocks automated crawlers even though the page is publicly accessible in a browser. This is usually not an error - verify the link works in a browser. If it does, you can safely ignore this finding.";
                }
                // Add special note for 405 errors (common for external resources)
                else if (status.Value == 405 && isExternal)
                {
                    recommendation = $"{note} External scripts, APIs, and resources often return 405 for automated checks using HEAD requests. This is normal behavior and doesn't indicate a broken link. The resource is likely accessible in a browser. You can safely ignore this finding.";
                }
                else
                {
                    recommendation = $"{note} If this is expected (e.g., members-only area), this is not an error. Otherwise, check access permissions.";
                }
            }
            else if (ctx.Metadata.Depth <= 2 && !isExternal && link.LinkType == "hyperlink")
            {
                // Important page recommendations for actual broken links
                impactNote = $"Broken links on important pages (depth {ctx.Metadata.Depth}) negatively impact site quality signals and user trust, which can reduce overall rankings.";
                var action = status.Value == 404 
                    ? "Fix the link URL or implement a 301 redirect to the correct page."
                    : $"Investigate why this page returns {statusText} and fix the issue or update the link.";
                recommendation = $"{action} Broken links reduce 'site quality' signals that search engines use for ranking.";
            }
            else if (status.Value == 404)
            {
                impactNote = "Broken links harm user experience and reduce site quality signals, which can negatively impact rankings.";
                recommendation = "Fix the link URL or implement a 301 redirect to the correct page.";
            }
            
            // Add depth information and recommendations
            var data = CreateFindingData(ctx, link, status.Value, isExternal, diagnosticInfo, hasRedirect: hasRedirect, recommendation: recommendation, impactNote: impactNote);
            
            TrackFinding(findingsMap,
                key,
                severity,
                code,
                message,
                data);
        }
        // Report redirect chains for internal links
        else if (hasRedirect == true && !isExternal)
        {
            var key = $"{link.Url}|LINK_TO_REDIRECT";
            TrackFinding(findingsMap,
                key,
                Severity.Info,
                "LINK_TO_REDIRECT",
                $"Link points to URL that redirects: {link.Url}",
                CreateFindingData(ctx, link, status, isExternal, diagnosticInfo, hasRedirect: true));
        }
        // Report nofollow on internal links (SEO issue)
        else if (link.HasNofollow && !isExternal && link.LinkType == "hyperlink")
        {
            var key = $"{link.Url}|NOFOLLOW_INTERNAL_LINK";
            TrackFinding(findingsMap,
                key,
                Severity.Warning,
                "NOFOLLOW_INTERNAL_LINK",
                $"Internal link has nofollow attribute (prevents link equity): {link.Url}",
                CreateFindingData(ctx, link, status, isExternal, diagnosticInfo));
        }
    }

    private async Task CheckAnchorLinkAsync(UrlContext ctx, LinkInfo link, ElementDiagnosticInfo? diagnosticInfo, Dictionary<string, FindingTracker> findingsMap, CancellationToken _)
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
                    $"Anchor link points to non-existent ID: #{link.AnchorId}",
                    CreateFindingData(ctx, link, null, false, diagnosticInfo));
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error checking anchor link: {AnchorId}", link.AnchorId);
        }
    }


    private FindingDetails CreateFindingData(
        UrlContext ctx, 
        LinkInfo link, 
        int? httpStatus, 
        bool isExternal, 
        ElementDiagnosticInfo? diagnosticInfo,
        bool? hasRedirect = null,
        TimeSpan? responseTime = null,
        string? recommendation = null,
        string? impactNote = null)
    {
        var builder = FindingDetailsBuilder.Create()
            .AddItem($"Link destination: {link.Url}")
            .AddItem($"Link type: {link.LinkType}")
            .AddItem($"Source page: {ctx.Url}");
        
        if (!string.IsNullOrEmpty(link.AnchorText))
        {
            builder.AddItem($"Anchor text: \"{link.AnchorText}\"");
        }
        
        if (httpStatus.HasValue)
        {
            builder.AddItem($"HTTP status: {httpStatus.Value}");
        }
        
        if (hasRedirect == true)
        {
            builder.AddItem("⚠️ This URL has a redirect chain");
        }
        
        if (link.HasNofollow)
        {
            builder.AddItem("🔗 Link has nofollow attribute");
        }
        
        if (responseTime.HasValue)
        {
            builder.AddItem($"Response time: {responseTime.Value.TotalSeconds:F2} seconds");
        }
        
        if (isExternal)
        {
            builder.AddItem("🌐 External link");
        }
        
        // Add impact note if provided
        if (!string.IsNullOrEmpty(impactNote))
        {
            builder.BeginNested("📉 SEO Impact")
                .AddItem(impactNote);
        }
        
        // Add recommendation if provided
        if (!string.IsNullOrEmpty(recommendation))
        {
            builder.BeginNested("💡 Recommendations")
                .AddItem(recommendation);
        }
        
        // Add all diagnostic/technical info to TechnicalMetadata
        builder.WithTechnicalMetadata("sourceUrl", ctx.Url.ToString())
            .WithTechnicalMetadata("targetUrl", link.Url)
            .WithTechnicalMetadata("anchorText", link.AnchorText)
            .WithTechnicalMetadata("linkType", link.LinkType)
            .WithTechnicalMetadata("httpStatus", httpStatus)
            .WithTechnicalMetadata("isExternal", isExternal)
            .WithTechnicalMetadata("hasNofollow", link.HasNofollow)
            .WithTechnicalMetadata("hasRedirect", hasRedirect);
        
        if (responseTime.HasValue)
        {
            builder.WithTechnicalMetadata("responseTimeSeconds", responseTime.Value.TotalSeconds);
        }

        if (diagnosticInfo != null)
        {
            builder.WithTechnicalMetadata("elementInfo", new
            {
                diagnosticInfo.TagName,
                diagnosticInfo.DomPath,
                diagnosticInfo.Attributes,
                diagnosticInfo.ParentElement,
                diagnosticInfo.BoundingBox,
                diagnosticInfo.IsVisible,
                diagnosticInfo.HtmlContext,
                diagnosticInfo.ComputedStyle
            });
        }

        return builder.Build();
    }

    /// <summary>
    /// Track a finding in the deduplication map. If the same finding already exists, increment its occurrence count.
    /// </summary>
    private void TrackFinding(Dictionary<string, FindingTracker> findingsMap, string key, Severity severity, string code, string message, FindingDetails? data)
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
                Data = data,
                OccurrenceCount = 1
            };
        }
    }

    /// <summary>
    /// Report all unique findings as structured report rows for easy scanning.
    /// </summary>
    private async Task ReportUniqueFindings(UrlContext ctx, Dictionary<string, FindingTracker> findingsMap)
    {
        foreach (var kvp in findingsMap.Values)
        {
            var tracker = kvp;
            
            // Extract data from technical metadata
            var techData = tracker.Data?.TechnicalMetadata;
            var targetUrl = techData?.GetValueOrDefault("targetUrl")?.ToString() ?? "";
            var anchorText = techData?.GetValueOrDefault("anchorText")?.ToString() ?? "";
            var linkType = techData?.GetValueOrDefault("linkType")?.ToString() ?? "";
            var httpStatus = techData?.GetValueOrDefault("httpStatus");
            var isExternal = techData?.GetValueOrDefault("isExternal") as bool? ?? false;
            
            // Format status for display
            var statusDisplay = httpStatus switch
            {
                null => "Unknown",
                0 => "Connection Failed",
                int status => status.ToString(),
                _ => "Unknown"
            };
            
            // Create report row with SEO-friendly columns
            var row = ReportRow.Create()
                .Set("Severity", tracker.Severity.ToString())
                .Set("LinkedFrom", ctx.Url.ToString())
                .Set("BrokenLink", targetUrl)
                .Set("Status", statusDisplay)
                .Set("LinkText", anchorText)
                .Set("LinkType", linkType)
                .Set("IsExternal", isExternal ? "Yes" : "No")
                .Set("Occurrences", tracker.OccurrenceCount);
            
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
        }
    }

    private string ResolveUrl(Uri baseUri, string relativeUrl, Uri? baseTagUri = null)
    {
        return UrlHelper.Resolve(baseUri, relativeUrl, baseTagUri);
    }

    private bool IsExternalLink(string baseUrl, string targetUrl)
    {
        return UrlHelper.IsExternal(baseUrl, targetUrl);
    }

    private class LinkInfo
    {
        public string Url { get; set; } = string.Empty;
        public string AnchorText { get; set; } = string.Empty;
        public string LinkType { get; set; } = string.Empty;
        public bool HasNofollow { get; set; }
        public string? AnchorId { get; set; }
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
}

