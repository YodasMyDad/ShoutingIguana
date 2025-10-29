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

            // Load stored diagnostic metadata from Links table (captured in Phase 1)
            // This includes ALL resources that were discovered during crawling, including
            // stylesheets, scripts, images, etc. that may have returned 404
            var linkDiagnostics = await LoadStoredLinkDiagnosticsAsync(ctx);
            
            // FIRST: Check all links from the Links table (Phase 1 captured data)
            // This ensures we catch resources like stylesheets that may have been requested
            // by the browser but failed to load (404) and may not be in final HTML
            var storedLinks = await _repositoryAccessor.GetLinksByFromUrlAsync(ctx.Project.ProjectId, ctx.Metadata.UrlId);
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
    /// Loads stored diagnostic metadata from the Links table (captured in Phase 1).
    /// Returns a dictionary mapping link URL to diagnostic info.
    /// </summary>
    private async Task<Dictionary<string, ElementDiagnosticInfo>> LoadStoredLinkDiagnosticsAsync(UrlContext ctx)
    {
        var diagnostics = new Dictionary<string, ElementDiagnosticInfo>(StringComparer.OrdinalIgnoreCase);
        
        try
        {
            // Get all outgoing links from this URL (includes diagnostic metadata)
            var storedLinks = await _repositoryAccessor.GetLinksByFromUrlAsync(ctx.Project.ProjectId, ctx.Metadata.UrlId);
            
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
            
            _logger.LogDebug("Loaded diagnostics for {Count} links from database for {Url}", diagnostics.Count, ctx.Url);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error loading stored link diagnostics for {Url}", ctx.Url);
        }
        
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
            // Internal link - check database
            // Phase 2: All internal resources are guaranteed to be crawled and in database
            status = await _checker.CheckLinkStatusAsync(ctx.Project.ProjectId, link.Url, ct);
            
            // CRITICAL: If status is null, it means the URL is not in the database or hasn't been crawled
            // This is unexpected in Phase 2 and should be reported as a broken link
            if (!status.HasValue)
            {
                // Report as broken - URL should have been crawled but wasn't found
                var key = $"{link.Url}|LINK_NOT_CRAWLED";
                var impactNote = "This internal resource was discovered but not found in the crawl database. This typically indicates a broken link or a resource that failed to load.";
                var recommendation = "Verify this URL is accessible and returns a valid response. Check for typos in the URL or missing files on the server.";
                
                TrackFinding(findingsMap,
                    key,
                    Severity.Error,
                    $"BROKEN_{link.LinkType.ToUpperInvariant()}",
                    $"Broken {link.LinkType}: {link.Url} (not found in crawl database)",
                    CreateFindingData(ctx, link, null, false, diagnosticInfo, recommendation: recommendation, impactNote: impactNote));
                
                return; // Don't continue checking this link
            }
            
            // Check if this URL has a redirect
            var urlWithAnchor = link.Url.Split('#')[0];
            hasRedirect = await CheckForRedirectAsync(ctx.Project.ProjectId, urlWithAnchor, ct);
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
            var isRestricted = status.Value == 401 || status.Value == 403 || status.Value == 451;
            var severity = isRestricted ? Severity.Info : Severity.Error;
            var statusText = status.Value == 0 ? "Connection Failed" : status.Value.ToString();
            
            string code;
            string message;
            
            if (isRestricted)
            {
                // Restricted pages - informational only
                code = status.Value switch
                {
                    401 => "AUTH_REQUIRED_LINK",
                    403 => "FORBIDDEN_LINK",
                    451 => "UNAVAILABLE_LEGAL",
                    _ => "RESTRICTED_LINK"
                };
                
                var restrictionType = status.Value switch
                {
                    401 => "requires authentication",
                    403 => "is forbidden/restricted",
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
            if (isRestricted)
            {
                var note = status.Value switch
                {
                    401 => "This page requires authentication/login to access.",
                    403 => "This page is restricted and access is forbidden.",
                    451 => "This page is unavailable for legal reasons.",
                    _ => "This page has restricted access."
                };
                
                // Add special note for external 403 errors (common with social media)
                if (status.Value == 403 && isExternal)
                {
                    recommendation = $"{note} For external links (especially social media platforms like Twitter/X, LinkedIn, etc.), this often means the site blocks automated crawlers even though the page is publicly accessible in a browser. This is usually not an error - verify the link works in a browser. If it does, you can safely ignore this finding.";
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

    private async Task<bool> CheckForRedirectAsync(int projectId, string url, CancellationToken ct)
    {
        try
        {
            var status = await _checker.CheckLinkStatusAsync(projectId, url, ct);
            return status.HasValue && status.Value >= 300 && status.Value < 400;
        }
        catch
        {
            return false;
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
    /// Report all unique findings with occurrence counts to the findings sink.
    /// </summary>
    private async Task ReportUniqueFindings(UrlContext ctx, Dictionary<string, FindingTracker> findingsMap)
    {
        foreach (var kvp in findingsMap.Values)
        {
            var tracker = kvp;
            
            // Add occurrence count to the message if > 1
            var message = tracker.Message;
            if (tracker.OccurrenceCount > 1)
            {
                message += $" (occurs {tracker.OccurrenceCount} times on this page)";
            }

            // Add occurrence count to finding details if there are duplicates
            var details = tracker.Data;
            if (details != null && tracker.OccurrenceCount > 1)
            {
                // Add occurrence count to technical metadata
                details.TechnicalMetadata ??= new Dictionary<string, object?>();
                details.TechnicalMetadata["occurrenceCount"] = tracker.OccurrenceCount;
            }

            await ctx.Findings.ReportAsync(
                Key,
                tracker.Severity,
                tracker.Code,
                message,
                details);
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

