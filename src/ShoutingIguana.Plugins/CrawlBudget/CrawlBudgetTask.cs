using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;
using ShoutingIguana.PluginSdk.Helpers;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace ShoutingIguana.Plugins.CrawlBudget;

/// <summary>
/// Crawl budget optimization: soft 404s, server errors, crawled but not indexed pages.
/// </summary>
public class CrawlBudgetTask(ILogger logger, IRepositoryAccessor repositoryAccessor) : UrlTaskBase
{
    private readonly ILogger _logger = logger;
    private readonly IRepositoryAccessor _repositoryAccessor = repositoryAccessor;
    
    // Track server error counts per project
    private static readonly ConcurrentDictionary<int, ConcurrentBag<int>> ServerErrorsByProject = new();
    private static readonly ConcurrentDictionary<int, int> TotalPagesByProject = new();
    
    // Flag to ensure we only report error rate once per project
    private static readonly ConcurrentDictionary<int, bool> ErrorRateReportedByProject = new();

    public override string Key => "CrawlBudget";
    public override string DisplayName => "Crawl Budget";
    public override string Description => "Identifies crawl budget waste: soft 404s, server errors, and indexation issues";
    public override int Priority => 55; // Run after content analysis

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

        // Only analyze internal URLs
        if (UrlHelper.IsExternal(ctx.Project.BaseUrl, ctx.Url.ToString()))
        {
            return;
        }

        try
        {
            // Track total pages
            TotalPagesByProject.AddOrUpdate(ctx.Project.ProjectId, 1, (_, count) => count + 1);
            
            // Track server errors (5xx)
            if (ctx.Metadata.StatusCode >= 500 && ctx.Metadata.StatusCode < 600)
            {
                var errorBag = ServerErrorsByProject.GetOrAdd(ctx.Project.ProjectId, _ => new ConcurrentBag<int>());
                errorBag.Add(ctx.Metadata.StatusCode);
                
                // Report individual 5xx error
                await ReportServerErrorAsync(ctx);
            }
            
            // Check for soft 404s on successful pages
            if (ctx.Metadata.StatusCode >= 200 && ctx.Metadata.StatusCode < 300)
            {
                await CheckSoft404Async(ctx);
                await CheckCrawledButNotIndexedAsync(ctx);
            }
            
            // Periodically check if we should report error rate (every 100 pages)
            var totalPages = TotalPagesByProject.GetValueOrDefault(ctx.Project.ProjectId, 0);
            if (totalPages % 100 == 0 && !ErrorRateReportedByProject.ContainsKey(ctx.Project.ProjectId))
            {
                await CheckServerErrorRateAsync(ctx);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing crawl budget for {Url}", ctx.Url);
        }
    }

    private async Task CheckSoft404Async(UrlContext ctx)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(ctx.RenderedHtml);

        // Extract title and main content
        var titleNode = doc.DocumentNode.SelectSingleNode("//title");
        var title = titleNode?.InnerText?.Trim().ToLowerInvariant() ?? "";
        
        var h1Node = doc.DocumentNode.SelectSingleNode("//h1");
        var h1 = h1Node?.InnerText?.Trim().ToLowerInvariant() ?? "";
        
        var bodyNode = doc.DocumentNode.SelectSingleNode("//body");
        var bodyText = bodyNode?.InnerText?.Trim().ToLowerInvariant() ?? "";
        
        // Pattern matching for soft 404 indicators
        var soft404Patterns = new[]
        {
            "404",
            "not found",
            "page not found",
            "page could not be found",
            "page does not exist",
            "page doesn't exist",
            "no longer exists",
            "has been removed",
            "has been deleted",
            "coming soon",
            "under construction",
            "no results found",
            "no items found",
            "nothing found",
            "sorry, but nothing matched",
            "oops! that page can",
            "the page you requested",
            "the page you are looking for"
        };

        var matchedPatterns = new List<string>();
        var matchLocations = new List<string>();

        foreach (var pattern in soft404Patterns)
        {
            if (title.Contains(pattern))
            {
                matchedPatterns.Add(pattern);
                if (!matchLocations.Contains("title"))
                    matchLocations.Add("title");
            }
            
            if (h1.Contains(pattern))
            {
                matchedPatterns.Add(pattern);
                if (!matchLocations.Contains("H1"))
                    matchLocations.Add("H1");
            }
            
            // For body text, only check first 500 chars to avoid false positives
            var bodyStart = bodyText.Length > 500 ? bodyText.Substring(0, 500) : bodyText;
            if (bodyStart.Contains(pattern))
            {
                matchedPatterns.Add(pattern);
                if (!matchLocations.Contains("body content"))
                    matchLocations.Add("body content");
            }
        }

        // Also check for very low content length with error-like messaging
        var contentLength = bodyText.Length;
        var hasErrorWords = matchedPatterns.Any();
        
        if (hasErrorWords || (contentLength < 200 && (title.Contains("error") || title.Contains("not found"))))
        {
            var confidence = "high";
            if (matchedPatterns.Count == 1 && contentLength > 500)
            {
                confidence = "medium";
            }
            
            var builder = FindingDetailsBuilder.Create()
                .AddItem($"HTTP Status: 200 OK (but appears to be an error page)")
                .AddItem($"Content length: {contentLength} characters")
                .AddItem($"Detection confidence: {confidence}");
            
            if (matchedPatterns.Any())
            {
                builder.BeginNested($"🔍 Soft 404 indicators found in {string.Join(", ", matchLocations)}");
                foreach (var pattern in matchedPatterns.Distinct().Take(5))
                {
                    builder.AddItem($"\"{pattern}\"");
                }
            }
            
            builder.BeginNested("⚠️ Impact")
                .AddItem("Wastes Googlebot crawl budget on non-existent pages")
                .AddItem("These pages should return 404 status, not 200 OK")
                .AddItem("Confuses search engines about site quality");
            
            builder.BeginNested("💡 Recommendations")
                .AddItem("Return proper 404 HTTP status code for missing pages")
                .AddItem("Return 410 (Gone) for permanently deleted content")
                .AddItem("Use 301 redirects if content moved to new URL")
                .AddItem("Soft 404s waste crawl budget and reduce indexation efficiency");
            
            builder.WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("statusCode", ctx.Metadata.StatusCode)
                .WithTechnicalMetadata("contentLength", contentLength)
                .WithTechnicalMetadata("patterns", matchedPatterns.Distinct().ToArray())
                .WithTechnicalMetadata("locations", matchLocations.ToArray())
                .WithTechnicalMetadata("confidence", confidence);
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "SOFT_404",
                $"Soft 404 detected: Page returns 200 OK but appears to show error content",
                builder.Build());
        }
    }

    private async Task CheckCrawledButNotIndexedAsync(UrlContext ctx)
    {
        var reasons = new List<string>();
        
        // Check if page has noindex
        if (ctx.Metadata.RobotsNoindex == true)
        {
            reasons.Add("noindex directive");
        }
        
        // Check if canonical points elsewhere
        var canonical = ctx.Metadata.CanonicalHtml ?? ctx.Metadata.CanonicalHttp;
        if (!string.IsNullOrEmpty(canonical))
        {
            var normalizedCurrent = UrlHelper.Normalize(ctx.Url.ToString());
            var normalizedCanonical = UrlHelper.Normalize(canonical);
            
            if (normalizedCurrent != normalizedCanonical)
            {
                reasons.Add("canonical points to different URL");
            }
        }
        
        // Check if robots.txt blocks it (project setting)
        // Note: If we got here, robots.txt allowed crawling, but we can mention indexation concerns
        
        // Only report if there are reasons and this is an important page
        if (reasons.Any() && ctx.Metadata.Depth <= 3)
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem($"Page will be crawled but NOT indexed")
                .AddItem($"Page depth: {ctx.Metadata.Depth}")
                .BeginNested("🚫 Reasons page won't be indexed");
            
            foreach (var reason in reasons)
            {
                details.AddItem(reason);
            }
            
            details.BeginNested("⚠️ Crawl Budget Impact")
                .AddItem("Googlebot wastes time crawling pages that won't be indexed")
                .AddItem("This is OK for truly non-indexable pages (filters, search results)")
                .AddItem("But if this is important content, it wastes crawl budget");
            
            details.BeginNested("💡 Recommendations")
                .AddItem("If page should be indexed: Remove noindex/canonical")
                .AddItem("If page shouldn't exist: Return 404 or 410")
                .AddItem("If page is low-value: Consider blocking in robots.txt to save budget");
            
            details.WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("depth", ctx.Metadata.Depth)
                .WithTechnicalMetadata("reasons", reasons.ToArray());
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "CRAWLED_NOT_INDEXED",
                $"Page crawled but won't be indexed: {string.Join(", ", reasons)}",
                details.Build());
        }
    }

    private async Task ReportServerErrorAsync(UrlContext ctx)
    {
        var details = FindingDetailsBuilder.Create()
            .AddItem($"HTTP Status: {ctx.Metadata.StatusCode}")
            .AddItem($"Page URL: {ctx.Url}")
            .AddItem($"Page depth: {ctx.Metadata.Depth}")
            .BeginNested("⚠️ Server Error Impact")
                .AddItem("5xx errors waste crawl budget")
                .AddItem("Search engines cannot index error pages")
                .AddItem("High error rate triggers quality concerns")
                .AddItem("May cause site-wide crawl rate reduction");
        
        details.BeginNested("💡 Recommendations")
            .AddItem("Investigate and fix server/application errors immediately")
            .AddItem("Check server logs for error details")
            .AddItem("High 5xx rate = serious crawl budget waste");
        
        details.WithTechnicalMetadata("url", ctx.Url.ToString())
            .WithTechnicalMetadata("statusCode", ctx.Metadata.StatusCode)
            .WithTechnicalMetadata("depth", ctx.Metadata.Depth);
        
        await ctx.Findings.ReportAsync(
            Key,
            Severity.Warning,
            $"SERVER_ERROR_{ctx.Metadata.StatusCode}",
            $"Server error {ctx.Metadata.StatusCode} wastes crawl budget",
            details.Build());
    }

    private async Task CheckServerErrorRateAsync(UrlContext ctx)
    {
        var totalPages = TotalPagesByProject.GetValueOrDefault(ctx.Project.ProjectId, 0);
        
        if (totalPages < 50) // Need reasonable sample size
        {
            return;
        }
        
        if (ServerErrorsByProject.TryGetValue(ctx.Project.ProjectId, out var errorBag))
        {
            var errorCount = errorBag.Count;
            var errorRate = (double)errorCount / totalPages;
            
            // Report if error rate > 5%
            if (errorRate > 0.05 && ErrorRateReportedByProject.TryAdd(ctx.Project.ProjectId, true))
            {
                var errorPercentage = (int)(errorRate * 100);
                
                var builder = FindingDetailsBuilder.Create()
                    .AddItem($"Total pages analyzed: {totalPages}")
                    .AddItem($"Server errors (5xx): {errorCount}")
                    .AddItem($"Error rate: {errorPercentage}%")
                    .AddItem("❌ HIGH ERROR RATE DETECTED");
                
                builder.BeginNested("⚠️ Critical Crawl Budget Impact")
                    .AddItem($"{errorPercentage}% of your pages return server errors")
                    .AddItem("This wastes massive amounts of crawl budget")
                    .AddItem("Search engines may reduce crawl rate site-wide")
                    .AddItem("Indicates serious server/application problems");
                
                builder.BeginNested("💡 Urgent Actions")
                    .AddItem("Investigate server errors immediately - check logs")
                    .AddItem("Fix application bugs causing 500 errors")
                    .AddItem("Check server resources (CPU, memory, disk)")
                    .AddItem("Verify database connections and queries")
                    .AddItem("High error rate = poor user experience + SEO damage");
                
                builder.WithTechnicalMetadata("totalPages", totalPages)
                    .WithTechnicalMetadata("errorCount", errorCount)
                    .WithTechnicalMetadata("errorRate", errorRate)
                    .WithTechnicalMetadata("errorPercentage", errorPercentage);
                
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Error,
                    "HIGH_SERVER_ERROR_RATE",
                    $"High server error rate detected: {errorPercentage}% of pages return 5xx errors",
                    builder.Build());
                
                _logger.LogWarning("High server error rate detected for project {ProjectId}: {ErrorRate:P1} ({ErrorCount}/{TotalPages})",
                    ctx.Project.ProjectId, errorRate, errorCount, totalPages);
            }
        }
    }

    public override void CleanupProject(int projectId)
    {
        ServerErrorsByProject.TryRemove(projectId, out _);
        TotalPagesByProject.TryRemove(projectId, out _);
        ErrorRateReportedByProject.TryRemove(projectId, out _);
        _logger.LogDebug("Cleaned up crawl budget data for project {ProjectId}", projectId);
    }
}

