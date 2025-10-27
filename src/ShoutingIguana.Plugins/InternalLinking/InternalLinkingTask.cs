using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;
using System.Collections.Concurrent;

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
    
    // Track anchor text per URL
    private static readonly ConcurrentDictionary<int, ConcurrentDictionary<string, List<string>>> AnchorTextsByProject = new();

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

        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(ctx.RenderedHtml);

            // Extract internal links
            var internalLinks = ExtractInternalLinks(doc, ctx.Url, ctx.Project.BaseUrl);

            // Track outlinks
            TrackOutlinks(ctx.Project.ProjectId, ctx.Url.ToString(), internalLinks.Count);

            // Track inlinks for discovered URLs
            foreach (var link in internalLinks)
            {
                TrackInlink(ctx.Project.ProjectId, link.TargetUrl);
                TrackAnchorText(ctx.Project.ProjectId, link.TargetUrl, link.AnchorText);
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

    private List<InternalLink> ExtractInternalLinks(HtmlDocument doc, Uri currentUrl, string baseUrl)
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

            // Try to resolve relative URLs
            if (!Uri.TryCreate(currentUrl, href, out var targetUri))
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

    private void TrackAnchorText(int projectId, string targetUrl, string anchorText)
    {
        if (string.IsNullOrWhiteSpace(anchorText))
        {
            return;
        }

        var projectAnchors = AnchorTextsByProject.GetOrAdd(projectId, _ => new ConcurrentDictionary<string, List<string>>());
        projectAnchors.AddOrUpdate(
            targetUrl,
            _ => new List<string> { anchorText },
            (_, list) => { lock (list) { list.Add(anchorText); } return list; });
    }

    private async Task AnalyzeOutlinksAsync(UrlContext ctx, int outlinkCount)
    {
        // Check for pages with no outlinks (dead ends)
        if (outlinkCount == 0)
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "NO_OUTLINKS",
                "Page has no internal links (dead end)",
                new
                {
                    url = ctx.Url.ToString(),
                    depth = ctx.Metadata.Depth,
                    recommendation = "Add internal links to improve site navigation and link equity flow"
                });
        }

        // Check for pages with very few outlinks (might be thin content or poor navigation)
        if (outlinkCount > 0 && outlinkCount < 3 && ctx.Metadata.Depth <= 2)
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "FEW_OUTLINKS",
                $"Important page has few internal links ({outlinkCount})",
                new
                {
                    url = ctx.Url.ToString(),
                    outlinkCount,
                    depth = ctx.Metadata.Depth,
                    recommendation = "Consider adding more contextual internal links"
                });
        }
    }

    private async Task AnalyzeOrphanStatusAsync(UrlContext ctx)
    {
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
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Warning,
                    "POTENTIAL_ORPHAN",
                    "Page has no detected internal links pointing to it (potential orphan)",
                    new
                    {
                        url = ctx.Url.ToString(),
                        depth = ctx.Metadata.Depth,
                        note = "This page was discovered but may not be linked from other pages",
                        recommendation = "Ensure important pages are linked from navigation or content"
                    });
            }
        }
    }

    private async Task AnalyzeAnchorTextAsync(UrlContext ctx)
    {
        if (!AnchorTextsByProject.TryGetValue(ctx.Project.ProjectId, out var projectAnchors))
        {
            return;
        }

        if (!projectAnchors.TryGetValue(ctx.Url.ToString(), out var anchorTexts))
        {
            return;
        }

        List<string> textsCopy;
        lock (anchorTexts)
        {
            textsCopy = anchorTexts.ToList();
        }

        if (textsCopy.Count == 0)
        {
            return;
        }

        // Analyze anchor text diversity
        var uniqueTexts = textsCopy.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var totalLinks = textsCopy.Count;
        var uniqueCount = uniqueTexts.Count;

        // Check for over-optimization (same anchor text used for all links)
        if (uniqueCount == 1 && totalLinks >= 5)
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "ANCHOR_TEXT_OVER_OPTIMIZATION",
                $"All links to this page use identical anchor text: \"{uniqueTexts[0]}\"",
                new
                {
                    url = ctx.Url.ToString(),
                    anchorText = uniqueTexts[0],
                    linkCount = totalLinks,
                    recommendation = "Vary anchor text for natural link profile"
                });
        }

        // Check for generic anchor text
        var genericTerms = new[] { "click here", "read more", "learn more", "more", "here", "link" };
        var genericCount = textsCopy.Count(t => genericTerms.Contains(t.ToLowerInvariant()));

        if (genericCount > totalLinks * 0.5)
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "GENERIC_ANCHOR_TEXT",
                $"Many links use generic anchor text ({genericCount}/{totalLinks})",
                new
                {
                    url = ctx.Url.ToString(),
                    genericCount,
                    totalLinks,
                    recommendation = "Use descriptive anchor text that explains the destination page"
                });
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

    private class InternalLink
    {
        public string TargetUrl { get; set; } = string.Empty;
        public string AnchorText { get; set; } = string.Empty;
    }
}

