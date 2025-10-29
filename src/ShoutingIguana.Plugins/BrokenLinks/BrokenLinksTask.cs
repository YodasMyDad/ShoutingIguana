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
    private readonly ExternalLinkChecker? _externalChecker;
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

    public BrokenLinksTask(ILogger logger, IBrokenLinksChecker checker, bool checkExternalLinks = false, bool checkAnchorLinks = true)
    {
        _logger = logger;
        _checker = checker;
        _checkExternalLinks = checkExternalLinks;
        _checkAnchorLinks = checkAnchorLinks;
        
        if (_checkExternalLinks)
        {
            _externalChecker = new ExternalLinkChecker(logger, TimeSpan.FromSeconds(5));
        }
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

            // Extract all links from HTML
            var links = await ExtractLinksFromHtmlAsync(doc, ctx);

            // Track findings to deduplicate them
            var findingsMap = new Dictionary<string, FindingTracker>();

            // If we have access to the browser page, also extract with diagnostics
            if (ctx.Page != null)
            {
                await AnalyzeLinksWithDiagnosticsAsync(ctx, links, findingsMap, ct);
            }
            else
            {
                // Fallback to HTML-only analysis
                await AnalyzeLinksFromHtmlAsync(ctx, links, findingsMap, ct);
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

    private async Task AnalyzeLinksWithDiagnosticsAsync(UrlContext ctx, List<LinkInfo> links, Dictionary<string, FindingTracker> findingsMap, CancellationToken ct)
    {
        // Extract base tag for URL resolution
        Uri? baseTagUri = null;
        if (!string.IsNullOrEmpty(ctx.RenderedHtml))
        {
            baseTagUri = UrlHelper.ExtractBaseTag(ctx.RenderedHtml, ctx.Url);
        }
        
        // Get all anchor elements from the page for diagnostic info
        var anchorElements = await ctx.Page!.QuerySelectorAllAsync("a[href]");
        var anchorDiagnostics = new Dictionary<string, ElementDiagnosticInfo>();

        foreach (var element in anchorElements)
        {
            try
            {
                var href = await element.GetAttributeAsync("href");
                if (!string.IsNullOrEmpty(href))
                {
                    var resolvedUrl = ResolveUrl(ctx.Url, href, baseTagUri);
                    var diagnosticInfo = await ElementDiagnostics.GetElementInfoAsync(ctx.Page, element);
                    anchorDiagnostics[resolvedUrl] = diagnosticInfo;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error getting diagnostic info for anchor element");
            }
        }

        // Check each link
        foreach (var link in links)
        {
            await CheckLinkAsync(ctx, link, anchorDiagnostics.GetValueOrDefault(link.Url), findingsMap, ct);
        }
    }

    private async Task AnalyzeLinksFromHtmlAsync(UrlContext ctx, List<LinkInfo> links, Dictionary<string, FindingTracker> findingsMap, CancellationToken ct)
    {
        foreach (var link in links)
        {
            await CheckLinkAsync(ctx, link, null, findingsMap, ct);
        }
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
            status = await _checker.CheckLinkStatusAsync(ctx.Project.ProjectId, link.Url, ct);
            
            // Check if this URL has a redirect
            var urlWithAnchor = link.Url.Split('#')[0];
            hasRedirect = await CheckForRedirectAsync(ctx.Project.ProjectId, urlWithAnchor, ct);
        }
        else if (_checkExternalLinks && _externalChecker != null)
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
        if (ctx.Page == null || string.IsNullOrEmpty(link.AnchorId))
            return;

        try
        {
            // Check if the target anchor exists in the page
            var targetElement = await ctx.Page.QuerySelectorAsync($"#{link.AnchorId}, [name='{link.AnchorId}']");
            
            if (targetElement == null)
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

        _externalChecker?.Dispose();
        _disposed = true;
    }
}

