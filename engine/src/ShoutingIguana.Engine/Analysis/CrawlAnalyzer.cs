using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Serialization;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Analysis;

/// <summary>
/// Runs every rule over a stored crawl and replaces its issues. Works from the stored page facts, links and URL
/// statuses, so it never refetches or reparses anything, and any crawl can be re-analysed with new rules or
/// thresholds. One streaming pass over the pages, then the cross-page checks; memory stays at a few dozen bytes
/// per URL.
/// </summary>
public sealed class CrawlAnalyzer(ILoggerFactory loggerFactory)
{
    private readonly ILogger _logger = loggerFactory.CreateLogger<CrawlAnalyzer>();

    /// <summary>Analyses a crawl that is not being written to. A crawl still running (or interrupted) is refused.</summary>
    public Task<AnalysisSummary> AnalyzeAsync(string databasePath, AnalysisSettings? settings = null, CancellationToken cancellationToken = default)
    {
        settings ??= new AnalysisSettings();
        var errors = settings.Validate();
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(settings));
        }

        return Task.Run(() => Analyze(databasePath, settings, cancellationToken), cancellationToken);
    }

    private AnalysisSummary Analyze(string databasePath, AnalysisSettings settings, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        CrawlStatus status;
        using (var probe = AnalysisReader.Open(databasePath))
        {
            status = probe.ReadStatus();
            if (status is CrawlStatus.Running or CrawlStatus.Paused)
            {
                throw new InvalidOperationException(
                    "This crawl is still running, or was interrupted. Finish it with --resume (or stop it) before analysing.");
            }
        }

        var crawlComplete = false;

        using var writer = IssueWriter.Open(databasePath, _logger);
        long pages = 0;
        using (var reader = AnalysisReader.Open(databasePath))
        {
            var urls = reader.ReadUrls();
            var options = reader.ReadOptions();
            UrlNormalizer.TryNormalize(options.StartUrl, out var startUrl);
            var scope = new CrawlScope(new Uri(startUrl), options.IncludeSubdomains, options.StayInStartFolder);

            // Orphans and sitemap coverage need every page and sitemap; a stopped crawl can't tell missing from not yet seen.
            crawlComplete = status == CrawlStatus.Completed && (reader.ReadSitemapsDone() || !options.DiscoverSitemaps);
            var hreflang = new HreflangRules(urls);
            IPageRule[] pageRules =
            [
                new TitleRules(settings), new DescriptionRules(settings), new HeadingRules(), new ContentRules(settings),
                new ImageRules(settings, urls), new LinkRules(urls), new CanonicalRules(urls), new IndexabilityRules(), hreflang,
                new StructuredDataRules(), new SecurityRules(urls), new UrlRules(settings), new PerformanceRules(settings),
                new HtmlRules()
            ];
            ISiteRule[] siteRules =
            [
                new InlinkRules(urls, crawlComplete), new DuplicateRules(settings), new FragmentRules(urls, reader), hreflang,
                new ResponseCodeRules(urls, reader.ReadBlockReasons()), new RedirectRules(urls),
                new SitemapRules(urls, reader.ReadSitemaps(), options, crawlComplete), new RobotsTxtRules(urls, reader.ReadRobots(), scope)
            ];

            foreach (var page in reader.ReadPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                pages++;
                foreach (var rule in pageRules)
                {
                    foreach (var hit in rule.Check(page))
                    {
                        writer.Add(hit);
                    }
                }

                foreach (var rule in siteRules)
                {
                    rule.Observe(page);
                }
            }

            foreach (var rule in siteRules)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var hit in rule.Finish())
                {
                    writer.Add(hit);
                }
            }
        }

        // The reader is closed by now, so the writer can fold the write-ahead log when it closes.
        var info = new AnalysisInfo(DateTimeOffset.UtcNow, EngineInfo.Version, pages, stopwatch.Elapsed);
        var counts = writer.Commit(IssueCatalog.All, info, JsonSerializer.Serialize(settings, EngineJsonContext.Default.AnalysisSettings));
        var issues = IssueCatalog.All.Select(issue => new IssueCount(issue, counts.GetValueOrDefault(issue.Id))).ToList();

        _logger.LogInformation("Analysed {Pages} pages in {Elapsed}: {Flagged} issues across {Types} checks",
            pages, stopwatch.Elapsed, issues.Sum(issue => issue.Count), issues.Count(issue => issue.Count > 0));
        return new AnalysisSummary(pages, issues, stopwatch.Elapsed, IsPartial: !crawlComplete);
    }
}
