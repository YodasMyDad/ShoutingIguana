using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Plugins.Robots;

/// <summary>
/// Robots.txt compliance, meta robots, X-Robots-Tag, and indexability analysis.
/// </summary>
public class RobotsTask(ILogger logger) : UrlTaskBase
{
    private readonly ILogger _logger = logger;

    public override string Key => "Robots";
    public override string DisplayName => "Robots & Indexability";
    public override int Priority => 20; // Run early since other plugins may use IsIndexable

    public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
    {
        try
        {
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
        // 4. It returns 4xx or 5xx status code

        // Check status code
        if (ctx.Metadata.StatusCode is >= 400)
        {
            return false;
        }

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
            
            await ctx.Findings.ReportAsync(
                Key,
                severity,
                "NOINDEX_DETECTED",
                "Page has noindex directive (will not be indexed by search engines)",
                new
                {
                    url = ctx.Url.ToString(),
                    source = !string.IsNullOrEmpty(ctx.Metadata.XRobotsTag) ? "X-Robots-Tag header" : "meta robots tag",
                    depth = ctx.Metadata.Depth,
                    isIndexable
                });
        }

        // Check for nofollow
        if (ctx.Metadata.RobotsNofollow == true)
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "NOFOLLOW_DETECTED",
                "Page has nofollow directive (links will not pass equity)",
                new
                {
                    url = ctx.Url.ToString(),
                    source = !string.IsNullOrEmpty(ctx.Metadata.XRobotsTag) ? "X-Robots-Tag header" : "meta robots tag"
                });
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
        await ctx.Findings.ReportAsync(
            Key,
            Severity.Info,
            "X_ROBOTS_TAG_PRESENT",
            $"X-Robots-Tag header present: {ctx.Metadata.XRobotsTag}",
            new
            {
                url = ctx.Url.ToString(),
                xRobotsTag = ctx.Metadata.XRobotsTag,
                isIndexable,
                note = "HTTP headers override meta tags if both are present"
            });
    }

    private async Task CheckRobotsConflictsAsync(UrlContext ctx)
    {
        if (!ctx.Metadata.HasRobotsConflict)
        {
            return;
        }

        await ctx.Findings.ReportAsync(
            Key,
            Severity.Warning,
            "ROBOTS_DIRECTIVE_CONFLICT",
            "Conflicting robots directives detected (meta tag vs X-Robots-Tag header)",
            new
            {
                url = ctx.Url.ToString(),
                xRobotsTag = ctx.Metadata.XRobotsTag,
                metaRobotsNoindex = ctx.Metadata.RobotsNoindex,
                metaRobotsNofollow = ctx.Metadata.RobotsNofollow,
                resolution = "Most restrictive directive takes precedence"
            });
    }

    private async Task CheckImportantPageIndexabilityAsync(UrlContext ctx, bool isIndexable)
    {
        // Check if an important page (low depth) is not indexable
        if (!isIndexable && ctx.Metadata.Depth <= 2)
        {
            var reason = ctx.Metadata.RobotsNoindex == true
                ? "noindex directive"
                : ctx.Metadata.StatusCode >= 400
                    ? $"HTTP {ctx.Metadata.StatusCode} status"
                    : "unknown reason";

            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "IMPORTANT_PAGE_NOT_INDEXABLE",
                $"Important page (depth {ctx.Metadata.Depth}) is not indexable: {reason}",
                new
                {
                    url = ctx.Url.ToString(),
                    depth = ctx.Metadata.Depth,
                    reason,
                    statusCode = ctx.Metadata.StatusCode,
                    hasNoindex = ctx.Metadata.RobotsNoindex,
                    recommendation = "Important pages should be indexable unless intentionally blocked"
                });
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
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "NOARCHIVE_DETECTED",
                "Page has 'noarchive' directive (prevents cached copies)",
                new
                {
                    url = ctx.Url.ToString(),
                    note = "Search engines will not show cached versions of this page"
                });
        }

        // Check for nosnippet
        if (tag.Contains("nosnippet"))
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "NOSNIPPET_DETECTED",
                "Page has 'nosnippet' directive (prevents text snippets in search results)",
                new
                {
                    url = ctx.Url.ToString(),
                    note = "Search engines will not show description snippets for this page"
                });
        }

        // Check for noimageindex
        if (tag.Contains("noimageindex"))
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "NOIMAGEINDEX_DETECTED",
                "Page has 'noimageindex' directive (images will not be indexed)",
                new
                {
                    url = ctx.Url.ToString(),
                    note = "Images on this page will not appear in image search results"
                });
        }

        // Check for unavailable_after
        if (tag.Contains("unavailable_after"))
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "UNAVAILABLE_AFTER_DETECTED",
                "Page has 'unavailable_after' directive (time-limited indexing)",
                new
                {
                    url = ctx.Url.ToString(),
                    xRobotsTag = ctx.Metadata.XRobotsTag,
                    note = "Page will be removed from index after specified date"
                });
        }
    }
}

