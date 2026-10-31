using System.Diagnostics;
using System.Globalization;
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
/// per URL. Page content is untrusted, so a rule that throws on one page is logged and skipped for that page
/// instead of ending the analysis.
/// </summary>
public sealed class CrawlAnalyzer(ILoggerFactory loggerFactory)
{
    /// <summary>Failures logged in full per rule; the rest are only counted, so a rule that fails everywhere can't flood the log.</summary>
    private const int LOGGED_FAILURES_PER_RULE = 5;

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

        List<string> notes = [];
        HashSet<IssueDefinition> skipped = [];
        Dictionary<string, int> failures = new(StringComparer.Ordinal);
        List<IssueHit> hits = [];

        using var writer = IssueWriter.Open(databasePath, _logger);
        long pages = 0;
        using (var reader = AnalysisReader.Open(databasePath))
        {
            var urls = reader.ReadUrls();
            var options = reader.ReadOptions();
            var sitemaps = reader.ReadSitemaps();
            UrlNormalizer.TryNormalize(options.StartUrl, out var startUrl);
            var scope = new CrawlScope(new Uri(startUrl), options.IncludeSubdomains, options.StayInStartFolder);

            // Orphans and sitemap coverage need every page and sitemap; a stopped crawl can't tell missing from not yet seen.
            var crawlComplete = status == CrawlStatus.Completed && (reader.ReadSitemapsDone() || !options.DiscoverSitemaps);
            var linksComplete = crawlComplete && SeesEveryLinkingPage(reader.ReadLimitSkips(), scope, notes, skipped);
            if (!crawlComplete)
            {
                Skip(notes, skipped, status == CrawlStatus.Completed
                        ? "Not every sitemap was read, so orphan pages and sitemap coverage weren't checked."
                        : "The crawl didn't finish, so orphan pages and sitemap coverage weren't checked.",
                    IssueCatalog.ORPHAN_PAGE, IssueCatalog.NOT_IN_SITEMAP, IssueCatalog.SITEMAP_MISSING);
            }
            else if (SitemapRules.Unreadable(sitemaps) is { } unreadable)
            {
                Skip(notes, skipped, $"Pages missing from the sitemaps weren't checked: {unreadable.Url} couldn't be read ({unreadable.Error}), and it may list them.",
                    IssueCatalog.NOT_IN_SITEMAP);
            }

            // The site lives on the start URL's origin and the home page's (after redirects); robots.txt failing there stops all crawling.
            var mainOrigins = new[] { startUrl, reader.ReadUrl(urls.HomeId) }.OfType<string>()
                .Select(url => new Uri(url).GetLeftPart(UriPartial.Authority)).ToHashSet(StringComparer.Ordinal);

            var hreflang = new HreflangRules(urls);
            var indexability = new IndexabilityRules();
            IPageRule[] pageRules =
            [
                new TitleRules(settings), new DescriptionRules(settings), new HeadingRules(), new ContentRules(settings),
                new ImageRules(settings, urls), new LinkRules(urls), new CanonicalRules(urls), indexability, hreflang,
                new StructuredDataRules(), new SecurityRules(urls), new UrlRules(settings), new PerformanceRules(settings),
                new HtmlRules()
            ];

            // An instant meta refresh makes the page a redirect: nobody sees it, so only the refresh itself is reported.
            IPageRule[] redirectRules = [indexability];
            ISiteRule[] siteRules =
            [
                new InlinkRules(urls, linksComplete), new DuplicateRules(settings), new FragmentRules(urls, reader.ReadFacts, reader.ReadFragmentLinks), hreflang,
                new ResponseCodeRules(urls, reader.ReadBlockReasons()), new RedirectRules(urls),
                new SitemapRules(urls, sitemaps, options, crawlComplete), new RobotsTxtRules(urls, reader.ReadRobots(), scope, mainOrigins)
            ];

            writer.AddIndexability(urls.IndexabilityChanges);
            foreach (var page in reader.ReadPages(urls))
            {
                cancellationToken.ThrowIfCancellationRequested();
                pages++;
                foreach (var rule in page.IsRedirect ? redirectRules : pageRules)
                {
                    // Hits are collected first, so a rule that fails halfway through a page adds nothing for it.
                    hits.Clear();
                    try
                    {
                        hits.AddRange(rule.Check(page));
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        RecordFailure(failures, rule, page.Url, exception);
                        continue;
                    }

                    hits.ForEach(writer.Add);
                }

                foreach (var rule in siteRules)
                {
                    try
                    {
                        rule.Observe(page);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        RecordFailure(failures, rule, page.Url, exception);
                    }
                }
            }

            foreach (var rule in siteRules)
            {
                cancellationToken.ThrowIfCancellationRequested();
                hits.Clear();
                try
                {
                    hits.AddRange(rule.Finish());
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _logger.LogError(exception, "Rule {Rule} failed after the page pass; its cross-page results are left out", RuleName(rule));
                    notes.Add($"{RuleName(rule)} failed after reading every page, so its cross-page results are missing (see the log).");
                    continue;
                }

                hits.ForEach(writer.Add);
            }
        }

        foreach (var (rule, count) in failures)
        {
            _logger.LogWarning("Rule {Rule} failed on {Pages} pages; its results for them are missing", rule, count);
            notes.Add($"{rule} failed on {RuleText.Count(count, "page", "pages")}, so its results for them are missing (see the log).");
        }

        // The reader is closed by now, so the writer can fold the write-ahead log when it closes.
        var info = new AnalysisInfo(DateTimeOffset.UtcNow, EngineInfo.Version, pages, stopwatch.Elapsed);
        var checks = IssueCatalog.All.Where(issue => !skipped.Contains(issue)).ToList();
        var counts = writer.Commit(checks, info, JsonSerializer.Serialize(settings, EngineJsonContext.Default.AnalysisSettings), notes);
        var issues = checks.Select(issue => new IssueCount(issue, counts.GetValueOrDefault(issue.Id))).ToList();

        _logger.LogInformation("Analysed {Pages} pages in {Elapsed}: {Flagged} issues across {Types} checks",
            pages, stopwatch.Elapsed, issues.Sum(issue => issue.Count), issues.Count(issue => issue.Count > 0));
        return new AnalysisSummary(pages, issues, stopwatch.Elapsed, IsPartial: skipped.Count > 0) { Notes = notes };
    }

    /// <summary>
    /// Whether the crawl saw every page that could link to a sitemap page, which the orphan check needs. A depth or
    /// URL limit leaves pages out, and a crawl kept to a folder never reads the links on the rest of the site.
    /// </summary>
    private bool SeesEveryLinkingPage(Dictionary<SkipReason, long> limitSkips, CrawlScope scope, List<string> notes, HashSet<IssueDefinition> skipped)
    {
        var complete = true;
        foreach (var (reason, limit) in new[] { (SkipReason.DepthLimit, "depth limit"), (SkipReason.UrlLimit, "URL limit") })
        {
            if (limitSkips.GetValueOrDefault(reason) is > 0 and var count)
            {
                var left = string.Create(CultureInfo.InvariantCulture, $"{count:N0} internal URL{(count == 1 ? string.Empty : "s")}");
                Skip(notes, skipped, $"Orphan pages weren't checked: the {limit} kept {left} out of the crawl, and pages linked only from them would look orphaned.",
                    IssueCatalog.ORPHAN_PAGE);
                complete = false;
            }
        }

        if (scope.StartFolder != "/")
        {
            Skip(notes, skipped, $"Orphan pages weren't checked: the crawl stayed in {scope.StartFolder}, so links from the rest of the site weren't read.",
                IssueCatalog.ORPHAN_PAGE);
            complete = false;
        }

        return complete;
    }

    private void Skip(List<string> notes, HashSet<IssueDefinition> skipped, string note, params IssueDefinition[] issues)
    {
        _logger.LogWarning("Skipped {Checks}: {Reason}", string.Join(", ", issues.Select(issue => issue.Id)), note);
        notes.Add(note);
        skipped.UnionWith(issues);
    }

    private void RecordFailure(Dictionary<string, int> failures, object rule, string url, Exception exception)
    {
        var name = RuleName(rule);
        var count = failures[name] = failures.GetValueOrDefault(name) + 1;
        if (count <= LOGGED_FAILURES_PER_RULE)
        {
            _logger.LogError(exception, "Rule {Rule} failed on {Url}; its results for this page are left out", name, url);
        }
    }

    private static string RuleName(object rule) => rule.GetType().Name;
}
