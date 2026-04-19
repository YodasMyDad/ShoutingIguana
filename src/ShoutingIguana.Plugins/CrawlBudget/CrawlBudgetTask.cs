using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;
using ShoutingIguana.PluginSdk.Helpers;

namespace ShoutingIguana.Plugins.CrawlBudget;

/// <summary>
/// Crawl budget optimization: soft 404s, server errors, crawled but not indexed pages.
/// </summary>
public class CrawlBudgetTask(ILogger logger) : UrlTaskBase
{

    // Track server error counts per project
    private static readonly ConcurrentDictionary<int, ConcurrentBag<int>> ServerErrorsByProject = new();
    private static readonly ConcurrentDictionary<int, int> TotalPagesByProject = new();

    // Flag to ensure we only report error rate once per project
    private static readonly ConcurrentDictionary<int, bool> ErrorRateReportedByProject = new();

    private static readonly string[] _softNotFoundPatterns =
    [
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
    ];

    private static readonly Regex[] _softNotFoundRegexes =
        [.. _softNotFoundPatterns.Select(p =>
            new Regex($@"\b{Regex.Escape(p)}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled))];

    private static readonly Regex _titleErrorRegex =
        new(@"\berror\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex _titleNotFoundRegex =
        new(@"\bnot found\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
            if (ctx.Metadata.StatusCode is >= 500 and < 600)
            {
                var errorBag = ServerErrorsByProject.GetOrAdd(ctx.Project.ProjectId, _ => new ConcurrentBag<int>());
                errorBag.Add(ctx.Metadata.StatusCode);
                
                // Report individual 5xx error
                await ReportServerErrorAsync(ctx);
            }
            
            // Check for soft 404s on successful pages
            var reportedSoft404 = false;
            if (ctx.Metadata.StatusCode is >= 200 and < 300)
            {
                reportedSoft404 = await CheckSoft404Async(ctx).ConfigureAwait(false);
            }

            // Flag noindex pages that still consumed crawl budget
            if (!reportedSoft404 && ctx.Metadata.RobotsNoindex == true)
            {
                await ReportNoindexWasteAsync(ctx).ConfigureAwait(false);
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
            logger.LogError(ex, "Error analyzing crawl budget for {Url}", ctx.Url);
        }
    }

    private async Task<bool> CheckSoft404Async(UrlContext ctx)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(ctx.RenderedHtml);

        var titleNode = doc.DocumentNode.SelectSingleNode("//title");
        var title = titleNode?.InnerText?.Trim() ?? "";

        var h1Node = doc.DocumentNode.SelectSingleNode("//h1");
        var h1 = h1Node?.InnerText?.Trim() ?? "";

        var bodyNode = doc.DocumentNode.SelectSingleNode("//body");
        var bodyText = bodyNode?.InnerText?.Trim() ?? "";

        var matchedPatterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchLocations = new List<string>();

        var titleMatched = false;
        var h1Matched = false;
        var bodyMatched = false;

        // Scan first 2000 chars of body to balance recall vs cost on large pages.
        var bodyStart = bodyText.Length > 2000 ? bodyText[..2000] : bodyText;

        for (var i = 0; i < _softNotFoundRegexes.Length; i++)
        {
            var rx = _softNotFoundRegexes[i];
            var pattern = _softNotFoundPatterns[i];

            if (rx.IsMatch(title))
            {
                matchedPatterns.Add(pattern);
                titleMatched = true;
            }

            if (rx.IsMatch(h1))
            {
                matchedPatterns.Add(pattern);
                h1Matched = true;
            }

            if (rx.IsMatch(bodyStart))
            {
                matchedPatterns.Add(pattern);
                bodyMatched = true;
            }
        }

        if (titleMatched) matchLocations.Add("title");
        if (h1Matched) matchLocations.Add("H1");
        if (bodyMatched) matchLocations.Add("body content");

        var contentLength = bodyText.Length;
        var titleErrorSignal = _titleErrorRegex.IsMatch(title) || _titleNotFoundRegex.IsMatch(title);

        // Require two distinct signals from different sources.
        var signals = 0;
        if (titleMatched || titleErrorSignal) signals++;
        if (h1Matched) signals++;
        if (bodyMatched) signals++;
        if (contentLength < 200) signals++;

        if (signals < 2)
        {
            return false;
        }

        var issueText = "Suspected soft 404";
        var detail = $"Suspected soft 404 based on {signals} signals ({string.Join(", ", matchedPatterns)}). Return a proper 4xx status or redirect/remove the URL so crawl budget isn't wasted on a missing page.";

        var row = CreateReportRow(ctx, issueText, Severity.Info, detail);
        await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, CancellationToken.None).ConfigureAwait(false);
        return true;
    }

    private async Task ReportNoindexWasteAsync(UrlContext ctx)
    {
        const string issueText = "CRAWLED_NOINDEX_PAGE";
        const string detail = "Page has noindex but was crawled — consumes crawl budget without indexation benefit.";

        var row = CreateReportRow(ctx, issueText, Severity.Info, detail);
        await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, CancellationToken.None).ConfigureAwait(false);
    }
    
    private async Task ReportServerErrorAsync(UrlContext ctx)
    {
        var issueText = $"Server Error (HTTP {ctx.Metadata.StatusCode})";
        var detail = $"Server returned HTTP {ctx.Metadata.StatusCode}, which wastes crawl budget and frustrates users. Resolve the 5xx response so crawlers and visitors see a healthy page.";

        var row = CreateReportRow(ctx, issueText, Severity.Warning, detail);
        await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, CancellationToken.None);
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

                var row = CreateReportRow(ctx, issueText, Severity.Error, detail, statusCode: 0, depth: 0);
                
                await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, CancellationToken.None);
                
                logger.LogWarning("High server error rate detected for project {ProjectId}: {ErrorRate:P1} ({ErrorCount}/{TotalPages})",
                    ctx.Project.ProjectId, errorRate, errorCount, totalPages);
            }
        }
    }

    public override void CleanupProject(int projectId)
    {
        ServerErrorsByProject.TryRemove(projectId, out _);
        TotalPagesByProject.TryRemove(projectId, out _);
        ErrorRateReportedByProject.TryRemove(projectId, out _);
        logger.LogDebug("Cleaned up crawl budget data for project {ProjectId}", projectId);
    }

    private static ReportRow CreateReportRow(UrlContext ctx, string issue, Severity severity, string details, int? statusCode = null, int? depth = null)
    {
        return ReportRow.Create()
            .SetPage(ctx.Url)
            .Set("Issue", issue)
            .Set("Details", details)
            .SetExplanation(details)
            .Set("StatusCode", statusCode ?? ctx.Metadata.StatusCode)
            .Set("Depth", depth ?? ctx.Metadata.Depth)
            .SetSeverity(severity);
    }
}

