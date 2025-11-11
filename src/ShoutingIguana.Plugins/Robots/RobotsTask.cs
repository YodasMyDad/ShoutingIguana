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
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

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
            
            // Use static HttpClient instance (best practice for .NET HttpClient lifetime management)
            var response = await HttpClient.GetAsync(robotsTxtUrl);
            
            if (!response.IsSuccessStatusCode)
            {
                // No robots.txt found
                var row = ReportRow.Create()
                    .Set("Page", ctx.Url.ToString())
                    .Set("Issue", "No robots.txt Found")
                    .Set("RobotsMeta", "")
                    .Set("XRobotsTag", "")
                    .Set("Indexable", "Yes")
                    .Set("Severity", "Info");
                
                await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
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
            
            var severityStr = ctx.Metadata.Depth <= 2 ? "Warning" : "Info";
            var row = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", "Noindex Detected")
                .Set("RobotsMeta", source.Contains("meta") ? "noindex" : "")
                .Set("XRobotsTag", source.Contains("header") ? "noindex" : "")
                .Set("Indexable", "No")
                .Set("Severity", severityStr);
            
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
        }

        // Check for nofollow
        if (ctx.Metadata.RobotsNofollow == true)
        {
            var source = !string.IsNullOrEmpty(ctx.Metadata.XRobotsTag) ? "X-Robots-Tag header" : "meta robots tag";
            var row = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", "Nofollow Detected")
                .Set("RobotsMeta", source.Contains("meta") ? "nofollow" : "")
                .Set("XRobotsTag", source.Contains("header") ? "nofollow" : "")
                .Set("Indexable", "Yes")
                .Set("Severity", "Info");
            
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
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
        var row = ReportRow.Create()
            .Set("Page", ctx.Url.ToString())
            .Set("Issue", "X-Robots-Tag Present")
            .Set("RobotsMeta", "")
            .Set("XRobotsTag", ctx.Metadata.XRobotsTag ?? "")
            .Set("Indexable", isIndexable ? "Yes" : "No")
            .Set("Severity", "Info");
        
        await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
    }

    private async Task CheckRobotsConflictsAsync(UrlContext ctx)
    {
        if (!ctx.Metadata.HasRobotsConflict)
        {
            return;
        }

        var row = ReportRow.Create()
            .Set("Page", ctx.Url.ToString())
            .Set("Issue", "Conflicting Robots Directives")
            .Set("RobotsMeta", $"noindex: {ctx.Metadata.RobotsNoindex}, nofollow: {ctx.Metadata.RobotsNofollow}")
            .Set("XRobotsTag", ctx.Metadata.XRobotsTag ?? "")
            .Set("Indexable", "See Details")
            .Set("Severity", "Warning");
        
        await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
    }

    private async Task CheckImportantPageIndexabilityAsync(UrlContext ctx, bool isIndexable)
    {
        // Check if an important page (low depth) is blocked by indexability directives
        // NOTE: We only check robots directives here, not HTTP errors (that's Broken Links plugin's job)
        if (!isIndexable && ctx.Metadata.Depth <= 2)
        {
            // Determine the reason (should only be robots-related at this point)
            string reason;
            
            if (ctx.Metadata.RobotsNoindex == true)
            {
                reason = !string.IsNullOrEmpty(ctx.Metadata.XRobotsTag) 
                    ? "noindex in X-Robots-Tag header" 
                    : "noindex in meta robots tag";
            }
            else
            {
                // This page is blocked by robots.txt or other directive
                reason = "blocked by robots.txt or other directive";
            }

            var row = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", $"Important Page Not Indexable (Depth {ctx.Metadata.Depth})")
                .Set("RobotsMeta", ctx.Metadata.RobotsNoindex == true ? "noindex" : "")
                .Set("XRobotsTag", ctx.Metadata.XRobotsTag ?? "")
                .Set("Indexable", "No")
                .Set("Severity", "Warning");
            
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
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
            var row1 = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", "Noarchive Directive")
                .Set("RobotsMeta", "")
                .Set("XRobotsTag", "noarchive")
                .Set("Indexable", "Yes")
                .Set("Severity", "Info");
            
            await ctx.Reports.ReportAsync(Key, row1, ctx.Metadata.UrlId, default);
        }

        // Check for nosnippet
        if (tag.Contains("nosnippet"))
        {
            var row = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", "Nosnippet Directive")
                .Set("RobotsMeta", "")
                .Set("XRobotsTag", "nosnippet")
                .Set("Indexable", "Yes")
                .Set("Severity", "Info");
            
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
        }

        // Check for noimageindex
        if (tag.Contains("noimageindex"))
        {
            var row = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", "Noimageindex Directive")
                .Set("RobotsMeta", "")
                .Set("XRobotsTag", "noimageindex")
                .Set("Indexable", "Yes")
                .Set("Severity", "Info");
            
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
        }

        // Check for unavailable_after
        if (tag.Contains("unavailable_after"))
        {
            var row = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", "Unavailable After Directive (Time-Limited)")
                .Set("RobotsMeta", "")
                .Set("XRobotsTag", "unavailable_after")
                .Set("Indexable", "Yes")
                .Set("Severity", "Info");
            
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
        }
        
        // Check for indexifembedded (Google's new directive for iframes)
        if (tag.Contains("indexifembedded"))
        {
            var row = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", "Indexifembedded Directive")
                .Set("RobotsMeta", "")
                .Set("XRobotsTag", "indexifembedded")
                .Set("Indexable", "Conditional")
                .Set("Severity", "Info");
            
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
        }
        
        // Check for max-snippet directive
        var maxSnippetMatch = System.Text.RegularExpressions.Regex.Match(tag, @"max-snippet:(\d+|-1)");
        if (maxSnippetMatch.Success)
        {
            var maxSnippet = maxSnippetMatch.Groups[1].Value;
            var snippetValue = maxSnippet == "-1" ? "unlimited" : $"{maxSnippet} characters";
            
            var snippetSeverity = maxSnippet == "0" ? "Warning" : "Info";
            var row2 = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", $"Max-Snippet: {snippetValue}")
                .Set("RobotsMeta", "")
                .Set("XRobotsTag", $"max-snippet:{maxSnippet}")
                .Set("Indexable", "Yes")
                .Set("Severity", snippetSeverity);
            
            await ctx.Reports.ReportAsync(Key, row2, ctx.Metadata.UrlId, default);
        }
        
        // Check for max-image-preview directive
        var maxImageMatch = System.Text.RegularExpressions.Regex.Match(tag, @"max-image-preview:(none|standard|large)");
        if (maxImageMatch.Success)
        {
            var maxImage = maxImageMatch.Groups[1].Value;
            var imageSeverity = maxImage == "none" ? "Warning" : "Info";
            
            var row3 = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", $"Max-Image-Preview: {maxImage}")
                .Set("RobotsMeta", "")
                .Set("XRobotsTag", $"max-image-preview:{maxImage}")
                .Set("Indexable", "Yes")
                .Set("Severity", imageSeverity);
            
            await ctx.Reports.ReportAsync(Key, row3, ctx.Metadata.UrlId, default);
        }
        
        // Check for max-video-preview directive
        var maxVideoMatch = System.Text.RegularExpressions.Regex.Match(tag, @"max-video-preview:(\d+|-1)");
        if (maxVideoMatch.Success)
        {
            var maxVideo = maxVideoMatch.Groups[1].Value;
            var videoValue = maxVideo == "-1" ? "unlimited" : $"{maxVideo} seconds";
            var videoSeverity = maxVideo == "0" ? "Warning" : "Info";
            
            var row4 = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", $"Max-Video-Preview: {videoValue}")
                .Set("RobotsMeta", "")
                .Set("XRobotsTag", $"max-video-preview:{maxVideo}")
                .Set("Indexable", "Yes")
                .Set("Severity", videoSeverity);
            
            await ctx.Reports.ReportAsync(Key, row4, ctx.Metadata.UrlId, default);
        }
        
        // Check for googlebot-news specific directives
        if (tag.Contains("googlebot-news"))
        {
            var row5 = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", "Googlebot-News Directive")
                .Set("RobotsMeta", "")
                .Set("XRobotsTag", "googlebot-news")
                .Set("Indexable", "Yes")
                .Set("Severity", "Info");
            
            await ctx.Reports.ReportAsync(Key, row5, ctx.Metadata.UrlId, default);
        }
    }
}

