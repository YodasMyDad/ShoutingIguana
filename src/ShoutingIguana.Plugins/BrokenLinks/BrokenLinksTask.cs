using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Plugins.BrokenLinks;

/// <summary>
/// Analyzes pages for broken links (404s, 500s, etc.).
/// </summary>
public class BrokenLinksTask : UrlTaskBase
{
    private readonly ILogger _logger;
    private readonly IBrokenLinksChecker _checker;

    public BrokenLinksTask(ILogger logger, IBrokenLinksChecker checker)
    {
        _logger = logger;
        _checker = checker;
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

            // Extract all links
            var links = new List<LinkInfo>();

            // Hyperlinks (a tags)
            var aNodes = doc.DocumentNode.SelectNodes("//a[@href]");
            if (aNodes != null)
            {
                foreach (var node in aNodes)
                {
                    var href = node.GetAttributeValue("href", "");
                    var anchorText = node.InnerText?.Trim() ?? "";
                    
                    if (!string.IsNullOrEmpty(href) && !href.StartsWith("#") && !href.StartsWith("javascript:") && !href.StartsWith("mailto:"))
                    {
                        links.Add(new LinkInfo
                        {
                            Url = ResolveUrl(ctx.Url, href),
                            AnchorText = anchorText,
                            LinkType = "hyperlink"
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

            // Check each link against the database to see if it's broken
            foreach (var link in links)
            {
                // Only check internal links (same domain as project base URL)
                if (!IsExternalLink(ctx.Project.BaseUrl, link.Url))
                {
                    var status = await _checker.CheckLinkStatusAsync(ctx.Project.ProjectId, link.Url, ct);
                    
                    if (status.HasValue && (status.Value >= 400 || status.Value == 0))
                    {
                        var severity = status.Value >= 500 || status.Value == 0 ? Severity.Error : Severity.Warning;
                        var statusText = status.Value == 0 ? "Connection Failed" : status.Value.ToString();
                        
                        await ctx.Findings.ReportAsync(
                            Key,
                            severity,
                            $"BROKEN_{link.LinkType.ToUpperInvariant()}",
                            $"Broken {link.LinkType}: {link.Url} returns {statusText}",
                            new
                            {
                                sourceUrl = ctx.Url.ToString(),
                                targetUrl = link.Url,
                                anchorText = link.AnchorText,
                                linkType = link.LinkType,
                                httpStatus = status.Value
                            });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing broken links for {Url}", ctx.Url);
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
    }
}

