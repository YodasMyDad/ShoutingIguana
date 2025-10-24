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

            // If we have access to the browser page, also extract with diagnostics
            if (ctx.Page != null)
            {
                await AnalyzeLinksWithDiagnosticsAsync(ctx, links, ct);
            }
            else
            {
                // Fallback to HTML-only analysis
                await AnalyzeLinksFromHtmlAsync(ctx, links, ct);
            }
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

    private async Task AnalyzeLinksWithDiagnosticsAsync(UrlContext ctx, List<LinkInfo> links, CancellationToken ct)
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
            await CheckLinkAsync(ctx, link, anchorDiagnostics.GetValueOrDefault(link.Url), ct);
        }
    }

    private async Task AnalyzeLinksFromHtmlAsync(UrlContext ctx, List<LinkInfo> links, CancellationToken ct)
    {
        foreach (var link in links)
        {
            await CheckLinkAsync(ctx, link, null, ct);
        }
    }

    private async Task CheckLinkAsync(UrlContext ctx, LinkInfo link, ElementDiagnosticInfo? diagnosticInfo, CancellationToken ct)
    {
        bool isExternal = IsExternalLink(ctx.Project.BaseUrl, link.Url);

        // Check anchor links
        if (link.LinkType == "anchor" && !string.IsNullOrEmpty(link.AnchorId))
        {
            await CheckAnchorLinkAsync(ctx, link, diagnosticInfo, ct);
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
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Info,
                    "SLOW_EXTERNAL_LINK",
                    $"External {link.LinkType} is slow to respond ({result.ResponseTime.TotalSeconds:F1}s): {link.Url}",
                    CreateFindingData(ctx, link, status, isExternal, diagnosticInfo, responseTime: result.ResponseTime));
            }
        }

        // Report broken links
        if (status.HasValue && (status.Value >= 400 || status.Value == 0))
        {
            var severity = status.Value >= 500 || status.Value == 0 ? Severity.Error : Severity.Warning;
            var statusText = status.Value == 0 ? "Connection Failed" : status.Value.ToString();
            
            await ctx.Findings.ReportAsync(
                Key,
                severity,
                $"BROKEN_{link.LinkType.ToUpperInvariant()}",
                $"Broken {link.LinkType}: {link.Url} returns {statusText}",
                CreateFindingData(ctx, link, status.Value, isExternal, diagnosticInfo, hasRedirect: hasRedirect));
        }
        // Report redirect chains for internal links
        else if (hasRedirect == true && !isExternal)
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "LINK_TO_REDIRECT",
                $"Link points to URL that redirects: {link.Url}",
                CreateFindingData(ctx, link, status, isExternal, diagnosticInfo, hasRedirect: true));
        }
        // Report nofollow on internal links (SEO issue)
        else if (link.HasNofollow && !isExternal && link.LinkType == "hyperlink")
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "NOFOLLOW_INTERNAL_LINK",
                $"Internal link has nofollow attribute (prevents link equity): {link.Url}",
                CreateFindingData(ctx, link, status, isExternal, diagnosticInfo));
        }
    }

    private async Task CheckAnchorLinkAsync(UrlContext ctx, LinkInfo link, ElementDiagnosticInfo? diagnosticInfo, CancellationToken ct)
    {
        if (ctx.Page == null || string.IsNullOrEmpty(link.AnchorId))
            return;

        try
        {
            // Check if the target anchor exists in the page
            var targetElement = await ctx.Page.QuerySelectorAsync($"#{link.AnchorId}, [name='{link.AnchorId}']");
            
            if (targetElement == null)
            {
                await ctx.Findings.ReportAsync(
                    Key,
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

