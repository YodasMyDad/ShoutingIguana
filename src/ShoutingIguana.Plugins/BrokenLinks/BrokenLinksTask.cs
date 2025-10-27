using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;
using ShoutingIguana.Plugins.Shared;

namespace ShoutingIguana.Plugins.BrokenLinks;

/// <summary>
/// Analyzes pages for broken links (404s, 500s, etc.) with comprehensive diagnostics.
/// </summary>
public class BrokenLinksTask : UrlTaskBase
{
    private readonly ILogger _logger;
    private readonly IBrokenLinksChecker _checker;
    private readonly ExternalLinkChecker? _externalChecker;
    private readonly bool _checkExternalLinks;
    private readonly bool _checkAnchorLinks;

    // Helper class to track unique findings with occurrence counts
    private class FindingTracker
    {
        public Severity Severity { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public object? Data { get; set; }
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
                        Url = ResolveUrl(ctx.Url, href),
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
                        Url = ResolveUrl(ctx.Url, src),
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
                        Url = ResolveUrl(ctx.Url, href),
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
                        Url = ResolveUrl(ctx.Url, src),
                        LinkType = "script"
                    });
                }
            }
        }

        return Task.FromResult(links);
    }

    private async Task AnalyzeLinksWithDiagnosticsAsync(UrlContext ctx, List<LinkInfo> links, Dictionary<string, FindingTracker> findingsMap, CancellationToken ct)
    {
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
                    var resolvedUrl = ResolveUrl(ctx.Url, href);
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
            // External link - check via HTTP
            var result = await _externalChecker.CheckUrlAsync(link.Url, ct);
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

        // Report broken links
        if (status.HasValue && (status.Value >= 400 || status.Value == 0))
        {
            // All broken links (4xx and 5xx) should be errors as they prevent users from accessing content
            var severity = Severity.Error;
            var statusText = status.Value == 0 ? "Connection Failed" : status.Value.ToString();
            
            // Dedupe by link URL and status code
            var key = $"{link.Url}|BROKEN_{link.LinkType.ToUpperInvariant()}|{status.Value}";
            
            // Add depth information and specific recommendations for important pages
            var data = CreateFindingData(ctx, link, status.Value, isExternal, diagnosticInfo, hasRedirect: hasRedirect);
            var dataDict = data as Dictionary<string, object?>;
            
            // Check if this is an important page (from source page perspective)
            if (dataDict != null && ctx.Metadata.Depth <= 2 && !isExternal && link.LinkType == "hyperlink")
            {
                dataDict["note"] = $"This broken link is found on an important page (depth {ctx.Metadata.Depth})";
                dataDict["recommendation"] = status.Value == 404 
                    ? "Fix the link URL or implement a 301 redirect to the correct page"
                    : $"Investigate why this page returns {statusText} and fix the issue or update the link";
            }
            
            TrackFinding(findingsMap,
                key,
                severity,
                $"BROKEN_{link.LinkType.ToUpperInvariant()}",
                $"Broken {link.LinkType}: {link.Url} returns {statusText}",
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

    private async Task CheckAnchorLinkAsync(UrlContext ctx, LinkInfo link, ElementDiagnosticInfo? diagnosticInfo, Dictionary<string, FindingTracker> findingsMap, CancellationToken ct)
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

    private object CreateFindingData(
        UrlContext ctx, 
        LinkInfo link, 
        int? httpStatus, 
        bool isExternal, 
        ElementDiagnosticInfo? diagnosticInfo,
        bool? hasRedirect = null,
        TimeSpan? responseTime = null)
    {
        var data = new Dictionary<string, object?>
        {
            ["sourceUrl"] = ctx.Url.ToString(),
            ["targetUrl"] = link.Url,
            ["anchorText"] = link.AnchorText,
            ["linkType"] = link.LinkType,
            ["httpStatus"] = httpStatus,
            ["isExternal"] = isExternal,
            ["hasNofollow"] = link.HasNofollow,
            ["hasRedirect"] = hasRedirect
        };

        if (responseTime.HasValue)
        {
            data["responseTimeSeconds"] = responseTime.Value.TotalSeconds;
        }

        if (diagnosticInfo != null)
        {
            data["elementInfo"] = new
            {
                diagnosticInfo.TagName,
                diagnosticInfo.DomPath,
                diagnosticInfo.Attributes,
                diagnosticInfo.ParentElement,
                diagnosticInfo.BoundingBox,
                diagnosticInfo.IsVisible,
                diagnosticInfo.HtmlContext,
                diagnosticInfo.ComputedStyle
            };
        }

        return data;
    }

    /// <summary>
    /// Track a finding in the deduplication map. If the same finding already exists, increment its occurrence count.
    /// </summary>
    private void TrackFinding(Dictionary<string, FindingTracker> findingsMap, string key, Severity severity, string code, string message, object? data)
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

            // Add occurrence count to the data object if there are duplicates
            object? dataWithCount = tracker.Data;
            if (tracker.Data != null && tracker.OccurrenceCount > 1)
            {
                // Check if data is already a dictionary (from CreateFindingData)
                if (tracker.Data is Dictionary<string, object?> existingDict)
                {
                    // Create a shallow copy to avoid mutation issues
                    var dataDict = new Dictionary<string, object?>(existingDict)
                    {
                        ["occurrenceCount"] = tracker.OccurrenceCount
                    };
                    dataWithCount = dataDict;
                }
                else
                {
                    // Fall back to reflection for other types (anonymous types, etc.)
                    var dataDict = new Dictionary<string, object?>();
                    var dataType = tracker.Data.GetType();
                    foreach (var prop in dataType.GetProperties())
                    {
                        try
                        {
                            dataDict[prop.Name] = prop.GetValue(tracker.Data);
                        }
                        catch
                        {
                            // Skip properties that can't be read
                        }
                    }
                    dataDict["occurrenceCount"] = tracker.OccurrenceCount;
                    dataWithCount = dataDict;
                }
            }

            await ctx.Findings.ReportAsync(
                Key,
                tracker.Severity,
                tracker.Code,
                message,
                dataWithCount);
        }
    }

    private string ResolveUrl(Uri baseUri, string relativeUrl)
    {
        try
        {
            if (Uri.TryCreate(relativeUrl, UriKind.Absolute, out var absoluteUri))
            {
                return absoluteUri.ToString();
            }
            
            if (Uri.TryCreate(baseUri, relativeUrl, out var resolvedUri))
            {
                return resolvedUri.ToString();
            }
            
            return relativeUrl;
        }
        catch
        {
            return relativeUrl;
        }
    }

    private bool IsExternalLink(string baseUrl, string targetUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            return true;
        }
        
        if (Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri))
        {
            // Remove www. prefix for comparison
            var baseHost = baseUri.Host.ToLowerInvariant();
            var targetHost = uri.Host.ToLowerInvariant();
            
            if (baseHost.StartsWith("www."))
                baseHost = baseHost.Substring(4);
            if (targetHost.StartsWith("www."))
                targetHost = targetHost.Substring(4);
                
            return baseHost != targetHost;
        }
        return false; // Relative URL is internal
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

