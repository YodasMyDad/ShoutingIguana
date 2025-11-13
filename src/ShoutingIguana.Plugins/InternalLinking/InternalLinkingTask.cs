using System;
using System.Collections.Concurrent;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;
using ShoutingIguana.PluginSdk.Helpers;

namespace ShoutingIguana.Plugins.InternalLinking;

/// <summary>
/// Internal link analysis: inlinks, outlinks, anchor text, orphan pages, and link equity.
/// </summary>
public class InternalLinkingTask(ILogger logger) : UrlTaskBase
{
    private readonly ILogger _logger = logger;
    
    // Track outbound links per URL
    private static readonly ConcurrentDictionary<int, ConcurrentDictionary<string, int>> OutlinkCountsByProject = new();
    
    // Track inbound links (URL -> count)
    private static readonly ConcurrentDictionary<int, ConcurrentDictionary<string, int>> InlinkCountsByProject = new();
    
    // Track anchor text per URL with source page information
    // Key: ProjectId -> TargetUrl -> List<(SourceUrl, AnchorText)>
    private static readonly ConcurrentDictionary<int, ConcurrentDictionary<string, List<(string SourceUrl, string AnchorText)>>> AnchorTextsByProject = new();

    private const string IssueNoOutlinks = "No Outlinks";
    private const string IssueFewOutlinks = "Few Outlinks";
    private const string IssueOrphanPage = "Orphan Page";
    private const string IssueGenericAnchor = "Generic Anchor";

    public override string Key => "InternalLinking";
    public override string DisplayName => "Internal Linking";
    public override string Description => "Analyzes internal links, anchor text, orphan pages, and link equity";
    public override int Priority => 40; // Run after basic analysis

    public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
    {
        // Only analyze HTML pages
        if (ctx.Metadata.ContentType?.Contains("text/html") != true)
        {
            return;
        }

        if (string.IsNullOrEmpty(ctx.RenderedHtml))
        {
            return;
        }

        // Only analyze successful pages (skip 4xx, 5xx errors)
        if (ctx.Metadata.StatusCode < 200 || ctx.Metadata.StatusCode >= 300)
        {
            return;
        }

        // Only analyze internal URLs (external URLs are for BrokenLinks status checking only)
        if (UrlHelper.IsExternal(ctx.Project.BaseUrl, ctx.Url.ToString()))
        {
            return;
        }

        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(ctx.RenderedHtml);
            
            // Extract base tag if present (respects browser behavior for relative URLs)
            Uri? baseTagUri = UrlHelper.ExtractBaseTag(ctx.RenderedHtml, ctx.Url);

            // Extract internal links
            var internalLinks = ExtractInternalLinks(doc, ctx.Url, ctx.Project.BaseUrl, baseTagUri);

            // Track outlinks
            TrackOutlinks(ctx.Project.ProjectId, ctx.Url.ToString(), internalLinks.Count);

            // Track inlinks for discovered URLs
            foreach (var link in internalLinks)
            {
                TrackInlink(ctx.Project.ProjectId, link.TargetUrl);
                TrackAnchorText(ctx.Project.ProjectId, link.TargetUrl, ctx.Url.ToString(), link.AnchorText);
            }

            // Analyze this URL's linking
            await AnalyzeOutlinksAsync(ctx, internalLinks.Count);
            await AnalyzeOrphanStatusAsync(ctx);
            await AnalyzeAnchorTextAsync(ctx);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing internal linking for {Url}", ctx.Url);
        }
    }

    private List<InternalLink> ExtractInternalLinks(HtmlDocument doc, Uri currentUrl, string baseUrl, Uri? baseTagUri)
    {
        List<InternalLink> links = [];
        var linkNodes = doc.DocumentNode.SelectNodes("//a[@href]");

        if (linkNodes == null)
        {
            return links;
        }

        var baseDomain = new Uri(baseUrl).Host;

        foreach (var linkNode in linkNodes)
        {
            var href = linkNode.GetAttributeValue("href", "");
            if (string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            // Resolve URL using UrlHelper (respects base tag)
            var resolvedUrl = UrlHelper.Resolve(currentUrl, href, baseTagUri);
            if (!Uri.TryCreate(resolvedUrl, UriKind.Absolute, out var targetUri))
            {
                continue;
            }

            // Only track internal links (same domain)
            if (!targetUri.Host.Equals(baseDomain, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var anchorText = linkNode.InnerText?.Trim() ?? "";
            links.Add(new InternalLink
            {
                TargetUrl = targetUri.ToString(),
                AnchorText = anchorText
            });
        }

        return links;
    }

    private void TrackOutlinks(int projectId, string url, int count)
    {
        var projectOutlinks = OutlinkCountsByProject.GetOrAdd(projectId, _ => new ConcurrentDictionary<string, int>());
        projectOutlinks[url] = count;
    }

    private void TrackInlink(int projectId, string targetUrl)
    {
        var projectInlinks = InlinkCountsByProject.GetOrAdd(projectId, _ => new ConcurrentDictionary<string, int>());
        projectInlinks.AddOrUpdate(targetUrl, 1, (_, count) => count + 1);
    }

    private void TrackAnchorText(int projectId, string targetUrl, string sourceUrl, string anchorText)
    {
        if (string.IsNullOrWhiteSpace(anchorText))
        {
            return;
        }

        var projectAnchors = AnchorTextsByProject.GetOrAdd(projectId, _ => new ConcurrentDictionary<string, List<(string, string)>>());
        projectAnchors.AddOrUpdate(
            targetUrl,
            _ => new List<(string, string)> { (sourceUrl, anchorText) },
            (_, list) => { lock (list) { list.Add((sourceUrl, anchorText)); } return list; });
    }

    private async Task AnalyzeOutlinksAsync(UrlContext ctx, int outlinkCount)
    {
        // Get inlink count for this URL
        var inlinkCount = 0;
        if (InlinkCountsByProject.TryGetValue(ctx.Project.ProjectId, out var projectInlinks))
        {
            inlinkCount = projectInlinks.GetValueOrDefault(ctx.Url.ToString(), 0);
        }
        
        // Check for pages with no outlinks (dead ends)
        if (outlinkCount == 0)
        {
            var row = ReportRow.Create()
                .SetSeverity(Severity.Info)
                .Set("Page", ctx.Url.ToString())
                .Set("IssueType", IssueNoOutlinks)
                .Set("FromURL", ctx.Url.ToString())
                .Set("ToURL", "(none)")
                .Set("AnchorText", "(none)")
                .Set("Inlinks", inlinkCount)
                .Set("Outlinks", outlinkCount)
                .Set("Depth", ctx.Metadata.Depth)
                .Set("Description", DescribeIssue(IssueNoOutlinks, ctx, outlinkCount, inlinkCount));
            
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
        }

        // Check for pages with very few outlinks (might be thin content or poor navigation)
        if (outlinkCount > 0 && outlinkCount < 3 && ctx.Metadata.Depth <= 2)
        {
            var row = ReportRow.Create()
                .SetSeverity(Severity.Info)
                .Set("Page", ctx.Url.ToString())
                .Set("IssueType", IssueFewOutlinks)
                .Set("FromURL", ctx.Url.ToString())
                .Set("ToURL", "(none)")
                .Set("AnchorText", "(none)")
                .Set("Inlinks", inlinkCount)
                .Set("Outlinks", outlinkCount)
                .Set("Depth", ctx.Metadata.Depth)
                .Set("Description", DescribeIssue(IssueFewOutlinks, ctx, outlinkCount, inlinkCount));
            
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
        }
    }

    private async Task AnalyzeOrphanStatusAsync(UrlContext ctx)
    {
        // Get outlink count for this URL
        var outlinkCount = 0;
        if (OutlinkCountsByProject.TryGetValue(ctx.Project.ProjectId, out var projectOutlinks))
        {
            outlinkCount = projectOutlinks.GetValueOrDefault(ctx.Url.ToString(), 0);
        }
        
        // Check if this URL has any inlinks
        if (InlinkCountsByProject.TryGetValue(ctx.Project.ProjectId, out var projectInlinks))
        {
            if (projectInlinks.TryGetValue(ctx.Url.ToString(), out var inlinkCount))
            {
                // Has inlinks, good!
                _logger.LogDebug("URL {Url} has {InlinkCount} inlinks", ctx.Url, inlinkCount);
                return;
            }
        }

        // No inlinks detected (potential orphan)
        // Only report if depth > 1 (not a seed URL) and not the homepage
        if (ctx.Metadata.Depth > 1)
        {
            var isHomepage = ctx.Url.AbsolutePath == "/" || ctx.Url.AbsolutePath == "";
            
            if (!isHomepage)
            {
                var row = ReportRow.Create()
                    .SetSeverity(Severity.Warning)
                    .Set("Page", ctx.Url.ToString())
                    .Set("IssueType", IssueOrphanPage)
                    .Set("FromURL", "(no linking page)")
                    .Set("ToURL", ctx.Url.ToString())
                    .Set("AnchorText", "(none)")
                    .Set("Inlinks", 0)
                    .Set("Outlinks", outlinkCount)
                    .Set("Depth", ctx.Metadata.Depth)
                    .Set("Description", DescribeIssue(IssueOrphanPage, ctx, outlinkCount, 0));
                
                await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
            }
        }
    }

    private async Task AnalyzeAnchorTextAsync(UrlContext ctx)
    {
        // Get outlink and inlink counts
        var outlinkCount = 0;
        if (OutlinkCountsByProject.TryGetValue(ctx.Project.ProjectId, out var projectOutlinks))
        {
            outlinkCount = projectOutlinks.GetValueOrDefault(ctx.Url.ToString(), 0);
        }
        
        var inlinkCount = 0;
        if (InlinkCountsByProject.TryGetValue(ctx.Project.ProjectId, out var projectInlinks))
        {
            inlinkCount = projectInlinks.GetValueOrDefault(ctx.Url.ToString(), 0);
        }
        
        if (!AnchorTextsByProject.TryGetValue(ctx.Project.ProjectId, out var projectAnchors))
        {
            return;
        }

        if (!projectAnchors.TryGetValue(ctx.Url.ToString(), out var anchorTexts))
        {
            return;
        }

        List<(string SourceUrl, string AnchorText)> textsCopy;
        lock (anchorTexts)
        {
            textsCopy = anchorTexts.ToList();
        }

        if (textsCopy.Count == 0)
        {
            return;
        }

        var totalLinks = textsCopy.Count;

        // Check for generic anchor text
        var genericTerms = new[] { "click here", "read more", "learn more", "more", "here", "link" };
        var genericLinks = textsCopy.Where(t => genericTerms.Contains(t.AnchorText.ToLowerInvariant())).ToList();
        var genericCount = genericLinks.Count;

        if (genericCount > totalLinks * 0.5)
        {
            // Report each generic anchor link as a row for easy scanning
            foreach (var genericLink in genericLinks.Take(10)) // Limit to first 10 to avoid spam
            {
                var row = ReportRow.Create()
                    .SetSeverity(Severity.Info)
                    .Set("Page", ctx.Url.ToString())
                    .Set("IssueType", IssueGenericAnchor)
                    .Set("FromURL", genericLink.SourceUrl)
                    .Set("ToURL", ctx.Url.ToString())
                    .Set("AnchorText", genericLink.AnchorText)
                    .Set("Inlinks", inlinkCount)
                    .Set("Outlinks", outlinkCount)
                    .Set("Depth", ctx.Metadata.Depth)
                    .Set("Description", DescribeIssue(IssueGenericAnchor, ctx, outlinkCount, inlinkCount, genericLink.AnchorText));
                
                await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
            }
        }
    }
    
    /// <summary>
    /// Cleanup per-project data when project is closed.
    /// </summary>
    public override void CleanupProject(int projectId)
    {
        OutlinkCountsByProject.TryRemove(projectId, out _);
        InlinkCountsByProject.TryRemove(projectId, out _);
        AnchorTextsByProject.TryRemove(projectId, out _);
        _logger.LogDebug("Cleaned up internal linking data for project {ProjectId}", projectId);
    }

    private static string DescribeIssue(string issueType, UrlContext ctx, int outlinkCount, int inlinkCount, string anchorText = "")
    {
        string NormalizeAnchor(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "(no anchor text)";
            }

            var cleaned = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return cleaned.Length > 120 ? cleaned[..120] + "..." : cleaned;
        }

        var anchorPreview = NormalizeAnchor(anchorText);

        return issueType switch
        {
            IssueNoOutlinks => $"Page {ctx.Url.AbsolutePath} currently links to no other internal destinations; add relevant anchors so visitors and crawlers can keep exploring.",
            IssueFewOutlinks => $"Page {ctx.Url.AbsolutePath} has {inlinkCount} inbound link(s) but only {outlinkCount} internal link(s) at depth {ctx.Metadata.Depth}; strengthen navigation by adding more relevant outbound links.",
            IssueOrphanPage => $"Page {ctx.Url.AbsolutePath} has no inbound links and still shows up as orphaned; add internal links from related pages or navigation to make it discoverable.",
            IssueGenericAnchor => $"Anchor text \"{anchorPreview}\" is too generic; use descriptive wording so users and search engines understand what the link points to.",
            _ => string.Empty
        };
    }

    private class InternalLink
    {
        public string TargetUrl { get; set; } = string.Empty;
        public string AnchorText { get; set; } = string.Empty;
    }
}

