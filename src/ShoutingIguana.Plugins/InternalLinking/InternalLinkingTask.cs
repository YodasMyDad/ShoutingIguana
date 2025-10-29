using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;
using ShoutingIguana.PluginSdk.Helpers;
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
    
    // Track anchor text per URL with source page information
    // Key: ProjectId -> TargetUrl -> List<(SourceUrl, AnchorText)>
    private static readonly ConcurrentDictionary<int, ConcurrentDictionary<string, List<(string SourceUrl, string AnchorText)>>> AnchorTextsByProject = new();

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
        // Check for pages with no outlinks (dead ends)
        if (outlinkCount == 0)
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem("Page has no internal links")
                .AddItem($"Page depth: {ctx.Metadata.Depth}")
                .AddItem("⚠️ Dead end - visitors have nowhere to go")
                .BeginNested("💡 Recommendations")
                    .AddItem("Add internal links to improve site navigation")
                    .AddItem("Links help distribute link equity")
                    .AddItem("Improve user experience with related content links")
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("depth", ctx.Metadata.Depth)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "NO_OUTLINKS",
                "Page has no internal links (dead end)",
                details);
        }

        // Check for pages with very few outlinks (might be thin content or poor navigation)
        if (outlinkCount > 0 && outlinkCount < 3 && ctx.Metadata.Depth <= 2)
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem($"Internal links: {outlinkCount}")
                .AddItem($"Page depth: {ctx.Metadata.Depth} (important page)")
                .AddItem("ℹ️ Few outlinks may indicate poor navigation")
                .BeginNested("💡 Recommendations")
                    .AddItem("Add more contextual internal links")
                    .AddItem("Link to related content")
                    .AddItem("Aim for 3-5 contextual links per page")
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("outlinkCount", outlinkCount)
                .WithTechnicalMetadata("depth", ctx.Metadata.Depth)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "FEW_OUTLINKS",
                $"Important page has few internal links ({outlinkCount})",
                details);
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
                var details = FindingDetailsBuilder.Create()
                    .AddItem("No internal links pointing to this page")
                    .AddItem($"Page depth: {ctx.Metadata.Depth}")
                    .AddItem("⚠️ Potential orphan page")
                    .BeginNested("ℹ️ Impact")
                        .AddItem("Orphan pages are harder for users to find")
                        .AddItem("May not rank well without internal link equity")
                        .AddItem("Search engines may not discover or prioritize them")
                        .AddItem("Misses opportunity to build topical authority through content clusters")
                    .BeginNested("💡 Recommendations")
                        .AddItem("Add links from navigation or relevant content")
                        .AddItem("Ensure important pages are discoverable")
                        .AddItem("Build internal linking structure with topic clusters")
                        .AddItem("Connect related content to establish topical authority")
                        .AddItem("Link from pillar pages to supporting content")
                    .WithTechnicalMetadata("url", ctx.Url.ToString())
                    .WithTechnicalMetadata("depth", ctx.Metadata.Depth)
                    .Build();
                
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Warning,
                    "POTENTIAL_ORPHAN",
                    "Page has no detected internal links pointing to it (potential orphan)",
                    details);
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
            // Get source pages with generic anchor text
            var genericSourcePages = genericLinks
                .Select(t => t.SourceUrl)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList();
            
            var builder = FindingDetailsBuilder.Create()
                .AddItem($"Generic anchor text: {genericCount} of {totalLinks} links")
                .AddItem($"Percentage: {(genericCount * 100 / totalLinks)}%");
            
            builder.BeginNested("📄 Pages with generic anchor text");
            foreach (var sourcePage in genericSourcePages.Take(5))
            {
                var genericAnchor = genericLinks.FirstOrDefault(l => l.SourceUrl.Equals(sourcePage, StringComparison.OrdinalIgnoreCase)).AnchorText;
                builder.AddItem($"{sourcePage} (\"{genericAnchor}\")");
            }
            if (genericSourcePages.Count > 5)
            {
                builder.AddItem($"... and {genericSourcePages.Count - 5} more");
            }
            
            builder.BeginNested("⚠️ Impact")
                    .AddItem("Generic anchors provide little SEO value")
                    .AddItem("Miss opportunity to use relevant keywords")
                .BeginNested("💡 Recommendations")
                    .AddItem("Use descriptive anchor text that explains the destination")
                    .AddItem("Instead of \"click here\", use \"view our pricing plans\"")
                    .AddItem("Include relevant keywords naturally")
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("genericCount", genericCount)
                .WithTechnicalMetadata("totalLinks", totalLinks)
                .WithTechnicalMetadata("sourcePages", genericSourcePages.ToArray());
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "GENERIC_ANCHOR_TEXT",
                $"Many links use generic anchor text ({genericCount}/{totalLinks})",
                builder.Build());
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

