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

            var issueText = $"Soft 404 ({confidence} confidence)";
            var detail = $"Identified as a soft 404 ({confidence} confidence). Return a proper 4xx status or redirect/remove the URL so crawl budget isn't wasted on a missing page.";

            var row = CreateReportRow(ctx, issueText, "Warning", detail);
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
        }
    }

    private async Task CheckCrawledButNotIndexedAsync(UrlContext ctx)
    {
        // NOTE: Noindex/canonical checks are duplicates
        // These are already handled by Robots and Canonical plugins
        // Skipping to avoid duplicate reporting
    }

    private async Task ReportServerErrorAsync(UrlContext ctx)
    {
        var issueText = $"Server Error (HTTP {ctx.Metadata.StatusCode})";
        var detail = $"Server returned HTTP {ctx.Metadata.StatusCode}, which wastes crawl budget and frustrates users. Resolve the 5xx response so crawlers and visitors see a healthy page.";

        var row = CreateReportRow(ctx, issueText, "Warning", detail);
        await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
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
                
                var issueText = $"High Server Error Rate ({errorPercentage}%)";
                var detail = $"High server error rate ({errorPercentage}%) means crawlers waste time retrying failing pages. Investigate why {errorCount} of {totalPages} pages returned 5xx and stabilise the backend.";

                var row = CreateReportRow(ctx, issueText, "Error", detail, statusCode: 0, depth: 0);
                
                await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
                
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

    private static ReportRow CreateReportRow(UrlContext ctx, string issue, string severity, string details, int? statusCode = null, int? depth = null)
    {
        return ReportRow.Create()
            .Set("Page", ctx.Url.ToString())
            .Set("Issue", issue)
            .Set("Details", details)
            .Set("StatusCode", statusCode ?? ctx.Metadata.StatusCode)
            .Set("Depth", depth ?? ctx.Metadata.Depth)
            .Set("Severity", severity);
    }
}

