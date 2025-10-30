using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;
using ShoutingIguana.PluginSdk.Helpers;

namespace ShoutingIguana.Plugins.Robots;

/// <summary>
/// Robots.txt compliance, meta robots, X-Robots-Tag, and indexability analysis.
/// </summary>
public class RobotsTask(ILogger logger) : UrlTaskBase
{
    private readonly ILogger _logger = logger;

    public override string Key => "Robots";
    public override string DisplayName => "Indexability";
    public override string Description => "Checks robots.txt, noindex tags, and other indexability directives";
    public override int Priority => 20; // Run early since other plugins may use IsIndexable

    public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
    {
        // Only analyze internal URLs (external URLs are for BrokenLinks status checking only)
        if (UrlHelper.IsExternal(ctx.Project.BaseUrl, ctx.Url.ToString()))
        {
            return;
        }

        try
        {
            // Check for missing robots.txt on the homepage only (depth 0)
            if (ctx.Metadata.Depth == 0)
            {
                await CheckForRobotsTxtAsync(ctx);
            }
            
            // Compute IsIndexable status
            bool isIndexable = ComputeIsIndexable(ctx);
            
            // Note: Will be stored in Urls.IsIndexable column via migration
            // For now, just use for analysis in this plugin

            // Analyze robots.txt compliance
            await AnalyzeRobotsTxtComplianceAsync(ctx);

            // Analyze meta robots directives
            await AnalyzeMetaRobotsAsync(ctx, isIndexable);

            // Analyze X-Robots-Tag headers
            await AnalyzeXRobotsTagAsync(ctx, isIndexable);

            // Check for conflicts between directives
            await CheckRobotsConflictsAsync(ctx);

            // Check for indexability issues on important pages
            await CheckImportantPageIndexabilityAsync(ctx, isIndexable);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing robots/indexability for {Url}", ctx.Url);
        }
    }

    private bool ComputeIsIndexable(UrlContext ctx)
    {
        // A page is NOT indexable if:
        // 1. It has noindex in meta robots
        // 2. It has noindex in X-Robots-Tag header
        // 3. It's blocked by robots.txt (if crawler respects robots.txt)
        //
        // NOTE: We do NOT check HTTP status codes here - that's the Broken Links plugin's job.
        // This plugin only cares about indexability DIRECTIVES (robots.txt, meta robots, etc.)

        // Check robots directives
        if (ctx.Metadata.RobotsNoindex == true)
        {
            return false;
        }

        // Check robots.txt (crawler already checked this)
        // If the URL was crawled, it means either:
        // - Robots.txt allows it, OR
        // - Robots.txt compliance is disabled
        // We'll report if it's blocked but still linked

        return true;
    }

    private async Task CheckForRobotsTxtAsync(UrlContext ctx)
    {
        try
        {
            // Build robots.txt URL from the base URL
            var baseUri = new Uri(ctx.Project.BaseUrl);
            var robotsTxtUrl = $"{baseUri.Scheme}://{baseUri.Host}/robots.txt";
            
            // Note: HttpClient is created here for a one-time use (only at depth 0)
            // This is acceptable since it's called once per crawl, not per URL
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            
            var response = await httpClient.GetAsync(robotsTxtUrl);
            
            if (!response.IsSuccessStatusCode)
            {
                // No robots.txt found
                var details = FindingDetailsBuilder.Create()
                    .AddItem($"Robots.txt URL: {robotsTxtUrl}")
                    .AddItem($"HTTP Status: {(int)response.StatusCode}")
                    .AddItem("✅ All pages allowed by default")
                    .BeginNested("ℹ️ Note")
                        .AddItem("A robots.txt file is not required")
                        .AddItem("Can be used to control crawler access")
                        .AddItem("Can specify crawl delays")
                        .AddItem("Can declare sitemap locations")
                    .WithTechnicalMetadata("robotsTxtUrl", robotsTxtUrl)
                    .WithTechnicalMetadata("statusCode", (int)response.StatusCode)
                    .Build();
                
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Info,
                    "NO_ROBOTS_TXT",
                    "No robots.txt file found (all pages allowed by default)",
                    details);
            }
            else
            {
                // robots.txt exists - optionally could analyze its content here
                _logger.LogDebug("robots.txt found at {Url}", robotsTxtUrl);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error checking for robots.txt");
        }
    }
    
    private Task AnalyzeRobotsTxtComplianceAsync(UrlContext ctx)
    {
        // Check if page is blocked by robots.txt but still internally linked
        // Note: The crawler already respects robots.txt based on Project.RespectRobotsTxt setting
        
        // If robots.txt compliance is enabled and this URL was crawled,
        // it means it was allowed by robots.txt
        if (ctx.Project.RespectRobotsTxt)
        {
            _logger.LogDebug("URL {Url} complies with robots.txt (RespectRobotsTxt=true)", ctx.Url);
        }
        
        return Task.CompletedTask;
    }

    private async Task AnalyzeMetaRobotsAsync(UrlContext ctx, bool isIndexable)
    {
        // Only analyze HTML pages
        if (ctx.Metadata.ContentType?.Contains("text/html") != true)
        {
            return;
        }

        // Check for noindex
        if (ctx.Metadata.RobotsNoindex == true)
        {
            var severity = ctx.Metadata.Depth <= 2 ? Severity.Warning : Severity.Info;
            var source = !string.IsNullOrEmpty(ctx.Metadata.XRobotsTag) ? "X-Robots-Tag header" : "meta robots tag";
            
            var details = FindingDetailsBuilder.Create()
                .AddItem($"Source: {source}")
                .AddItem($"Page depth: {ctx.Metadata.Depth}")
                .AddItem("⚠️ This page will not be indexed by search engines")
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("source", source)
                .WithTechnicalMetadata("depth", ctx.Metadata.Depth)
                .WithTechnicalMetadata("isIndexable", isIndexable)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                severity,
                "NOINDEX_DETECTED",
                "Page has noindex directive (will not be indexed by search engines)",
                details);
        }

        // Check for nofollow
        if (ctx.Metadata.RobotsNofollow == true)
        {
            var source = !string.IsNullOrEmpty(ctx.Metadata.XRobotsTag) ? "X-Robots-Tag header" : "meta robots tag";
            var details = FindingDetailsBuilder.Create()
                .AddItem($"Source: {source}")
                .AddItem("🔗 Links on this page will not pass link equity")
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("source", source)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "NOFOLLOW_DETECTED",
                "Page has nofollow directive (links will not pass equity)",
                details);
        }

        // Check for other robots directives that might be present
        await CheckAdvancedRobotsDirectivesAsync(ctx);
    }

    private async Task AnalyzeXRobotsTagAsync(UrlContext ctx, bool isIndexable)
    {
        if (string.IsNullOrEmpty(ctx.Metadata.XRobotsTag))
        {
            return;
        }

        // Report that X-Robots-Tag is being used
        var details = FindingDetailsBuilder.Create()
            .AddItem($"X-Robots-Tag: {ctx.Metadata.XRobotsTag}")
            .AddItem($"Indexable: {(isIndexable ? "Yes" : "No")}")
            .AddItem("ℹ️ HTTP headers override meta tags if both are present")
            .WithTechnicalMetadata("url", ctx.Url.ToString())
            .WithTechnicalMetadata("xRobotsTag", ctx.Metadata.XRobotsTag)
            .WithTechnicalMetadata("isIndexable", isIndexable)
            .Build();
        
        await ctx.Findings.ReportAsync(
            Key,
            Severity.Info,
            "X_ROBOTS_TAG_PRESENT",
            $"X-Robots-Tag header present: {ctx.Metadata.XRobotsTag}",
            details);
    }

    private async Task CheckRobotsConflictsAsync(UrlContext ctx)
    {
        if (!ctx.Metadata.HasRobotsConflict)
        {
            return;
        }

        var details = FindingDetailsBuilder.Create()
            .AddItem("Conflicting directives found:")
            .AddItem($"  • X-Robots-Tag: {ctx.Metadata.XRobotsTag}")
            .AddItem($"  • Meta robots noindex: {ctx.Metadata.RobotsNoindex}")
            .AddItem($"  • Meta robots nofollow: {ctx.Metadata.RobotsNofollow}")
            .AddItem("⚠️ Most restrictive directive takes precedence")
            .WithTechnicalMetadata("url", ctx.Url.ToString())
            .WithTechnicalMetadata("xRobotsTag", ctx.Metadata.XRobotsTag)
            .WithTechnicalMetadata("metaRobotsNoindex", ctx.Metadata.RobotsNoindex)
            .WithTechnicalMetadata("metaRobotsNofollow", ctx.Metadata.RobotsNofollow)
            .Build();
        
        await ctx.Findings.ReportAsync(
            Key,
            Severity.Warning,
            "ROBOTS_DIRECTIVE_CONFLICT",
            "Conflicting robots directives detected (meta tag vs X-Robots-Tag header)",
            details);
    }

    private async Task CheckImportantPageIndexabilityAsync(UrlContext ctx, bool isIndexable)
    {
        // Check if an important page (low depth) is blocked by indexability directives
        // NOTE: We only check robots directives here, not HTTP errors (that's Broken Links plugin's job)
        if (!isIndexable && ctx.Metadata.Depth <= 2)
        {
            // Determine the reason (should only be robots-related at this point)
            string reason;
            string recommendation;
            
            if (ctx.Metadata.RobotsNoindex == true)
            {
                reason = !string.IsNullOrEmpty(ctx.Metadata.XRobotsTag) 
                    ? "noindex in X-Robots-Tag header" 
                    : "noindex in meta robots tag";
                recommendation = "Important pages should be indexable. Remove the noindex directive unless this is intentional (e.g., admin pages, thank-you pages).";
            }
            else
            {
                // This page is blocked by robots.txt or other directive
                reason = "blocked by robots.txt or other directive";
                recommendation = "Important pages should be accessible to search engines. Check your robots.txt file.";
            }

            var details = FindingDetailsBuilder.Create()
                .AddItem($"Page depth: {ctx.Metadata.Depth} (important page)")
                .AddItem($"Indexable: No")
                .AddItem($"Reason: {reason}")
                .BeginNested("💡 Recommendations")
                    .AddItem(recommendation)
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("depth", ctx.Metadata.Depth)
                .WithTechnicalMetadata("reason", reason)
                .WithTechnicalMetadata("hasNoindex", ctx.Metadata.RobotsNoindex)
                .WithTechnicalMetadata("xRobotsTag", ctx.Metadata.XRobotsTag)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "IMPORTANT_PAGE_NOT_INDEXABLE",
                $"Important page (depth {ctx.Metadata.Depth}) is not indexable: {reason}",
                details);
        }
    }

    private async Task CheckAdvancedRobotsDirectivesAsync(UrlContext ctx)
    {
        // Check for other robots directives in X-Robots-Tag if present
        if (string.IsNullOrEmpty(ctx.Metadata.XRobotsTag))
        {
            return;
        }

        var tag = ctx.Metadata.XRobotsTag.ToLowerInvariant();

        // Check for noarchive
        if (tag.Contains("noarchive"))
        {
            var details = FindingDetailsBuilder.WithMetadata(
                new Dictionary<string, object?> { ["url"] = ctx.Url.ToString() },
                "noarchive directive detected",
                "ℹ️ Search engines will not show cached versions of this page");
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "NOARCHIVE_DETECTED",
                "Page has 'noarchive' directive (prevents cached copies)",
                details);
        }

        // Check for nosnippet
        if (tag.Contains("nosnippet"))
        {
            var details = FindingDetailsBuilder.WithMetadata(
                new Dictionary<string, object?> { ["url"] = ctx.Url.ToString() },
                "nosnippet directive detected",
                "ℹ️ Search engines will not show description snippets");
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "NOSNIPPET_DETECTED",
                "Page has 'nosnippet' directive (prevents text snippets in search results)",
                details);
        }

        // Check for noimageindex
        if (tag.Contains("noimageindex"))
        {
            var details = FindingDetailsBuilder.WithMetadata(
                new Dictionary<string, object?> { ["url"] = ctx.Url.ToString() },
                "noimageindex directive detected",
                "ℹ️ Images on this page will not appear in image search");
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "NOIMAGEINDEX_DETECTED",
                "Page has 'noimageindex' directive (images will not be indexed)",
                details);
        }

        // Check for unavailable_after
        if (tag.Contains("unavailable_after"))
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem("unavailable_after directive detected")
                .AddItem("⏰ Time-limited indexing")
                .AddItem("ℹ️ Page will be removed from index after specified date")
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("xRobotsTag", ctx.Metadata.XRobotsTag)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "UNAVAILABLE_AFTER_DETECTED",
                "Page has 'unavailable_after' directive (time-limited indexing)",
                details);
        }
        
        // Check for indexifembedded (Google's new directive for iframes)
        if (tag.Contains("indexifembedded"))
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem("indexifembedded directive detected")
                .AddItem("ℹ️ Allows indexing when embedded in iframes")
                .AddItem("Even if page has noindex, can be indexed when embedded")
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("xRobotsTag", ctx.Metadata.XRobotsTag)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "INDEXIFEMBEDDED_DETECTED",
                "Page has 'indexifembedded' directive (allows indexing when embedded in iframes)",
                details);
        }
        
        // Check for max-snippet directive
        var maxSnippetMatch = System.Text.RegularExpressions.Regex.Match(tag, @"max-snippet:(\d+|-1)");
        if (maxSnippetMatch.Success)
        {
            var maxSnippet = maxSnippetMatch.Groups[1].Value;
            var snippetValue = maxSnippet == "-1" ? "unlimited" : $"{maxSnippet} characters";
            var severity = maxSnippet == "0" ? Severity.Warning : Severity.Info;
            
            var builder = FindingDetailsBuilder.Create()
                .AddItem($"max-snippet: {snippetValue}");
            
            if (maxSnippet == "0")
            {
                builder.AddItem("⚠️ Prevents any text snippets - may reduce SERP visibility")
                    .BeginNested("💡 Recommendations")
                        .AddItem("Consider allowing snippets for better SERP appearance");
            }
            else
            {
                builder.AddItem($"ℹ️ Limits text snippet length to {snippetValue}");
            }
            
            builder.WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("maxSnippet", maxSnippet);
            
            await ctx.Findings.ReportAsync(
                Key,
                severity,
                "MAX_SNIPPET_DETECTED",
                $"Page has 'max-snippet' directive: {snippetValue}",
                builder.Build());
        }
        
        // Check for max-image-preview directive
        var maxImageMatch = System.Text.RegularExpressions.Regex.Match(tag, @"max-image-preview:(none|standard|large)");
        if (maxImageMatch.Success)
        {
            var maxImage = maxImageMatch.Groups[1].Value;
            var severity = maxImage == "none" ? Severity.Warning : Severity.Info;
            
            var builder = FindingDetailsBuilder.Create()
                .AddItem($"max-image-preview: {maxImage}");
            
            if (maxImage == "none")
            {
                builder.AddItem("⚠️ Prevents image previews - may reduce visibility")
                    .BeginNested("💡 Recommendations")
                        .AddItem("Consider allowing image previews for better engagement");
            }
            else
            {
                builder.AddItem($"✅ Allows {maxImage} image previews in search results");
            }
            
            builder.WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("maxImagePreview", maxImage);
            
            await ctx.Findings.ReportAsync(
                Key,
                severity,
                "MAX_IMAGE_PREVIEW_DETECTED",
                $"Page has 'max-image-preview' directive: {maxImage}",
                builder.Build());
        }
        
        // Check for max-video-preview directive
        var maxVideoMatch = System.Text.RegularExpressions.Regex.Match(tag, @"max-video-preview:(\d+|-1)");
        if (maxVideoMatch.Success)
        {
            var maxVideo = maxVideoMatch.Groups[1].Value;
            var videoValue = maxVideo == "-1" ? "unlimited" : $"{maxVideo} seconds";
            var severity = maxVideo == "0" ? Severity.Warning : Severity.Info;
            
            var builder = FindingDetailsBuilder.Create()
                .AddItem($"max-video-preview: {videoValue}");
            
            if (maxVideo == "0")
            {
                builder.AddItem("⚠️ Prevents video previews - may reduce click-through")
                    .BeginNested("💡 Recommendations")
                        .AddItem("Consider allowing video previews");
            }
            else
            {
                builder.AddItem($"ℹ️ Limits video preview to {videoValue}");
            }
            
            builder.WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("maxVideoPreview", maxVideo);
            
            await ctx.Findings.ReportAsync(
                Key,
                severity,
                "MAX_VIDEO_PREVIEW_DETECTED",
                $"Page has 'max-video-preview' directive: {videoValue}",
                builder.Build());
        }
        
        // Check for googlebot-news specific directives
        if (tag.Contains("googlebot-news"))
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem("googlebot-news directive detected")
                .AddItem("ℹ️ This specifically targets Google News crawler")
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("xRobotsTag", ctx.Metadata.XRobotsTag)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "GOOGLEBOT_NEWS_DIRECTIVE",
                "Page has googlebot-news specific directive",
                details);
        }
    }
}

