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
        List<string> notes = [];
        HashSet<IssueDefinition> skipped = [];
        Dictionary<string, int> failures = new(StringComparer.Ordinal);
        List<IssueHit> hits = [];
        var finishFailed = false;

        // Opening the writer locks the file against crawls first, then refuses a crawl that is running or was interrupted.
        using var writer = IssueWriter.Open(databasePath, _logger);
        long pages = 0;
        using (var reader = AnalysisReader.Open(databasePath))
        {
            var status = reader.ReadStatus();
            var urls = reader.ReadUrls();
            var options = reader.ReadOptions();
            var sitemaps = reader.ReadSitemaps();
            UrlNormalizer.TryNormalize(options.StartUrl, out var startUrl);
            var scope = new CrawlScope(new Uri(startUrl), options.IncludeSubdomains, options.StayInStartFolder);

            // Orphans and sitemap coverage need every page and sitemap; a stopped crawl can't tell missing from not yet seen.
            var crawlComplete = status == CrawlStatus.Completed && (reader.ReadSitemapsDone() || !options.DiscoverSitemaps);
            var linksComplete = crawlComplete && SeesEveryLinkingPage(reader.ReadLimitSkips(), scope, notes, skipped);
            if (!options.DiscoverSitemaps)
            {
                Skip(notes, skipped, "Sitemaps weren't read in this crawl, so orphan pages and the sitemap checks weren't run.",
                    [IssueCatalog.ORPHAN_PAGE, .. IssueCatalog.All.Where(issue => issue.Category == IssueCategory.Sitemaps)]);
            }
            else if (!crawlComplete)
            {
                Skip(notes, skipped, status == CrawlStatus.Completed
                        ? "Not every sitemap was read, so orphan pages and sitemap coverage weren't checked."
                        : "The crawl didn't finish, so orphan pages and sitemap coverage weren't checked.",
                    IssueCatalog.ORPHAN_PAGE, IssueCatalog.NOT_IN_SITEMAP, IssueCatalog.SITEMAP_MISSING);
            }
            else if (!sitemaps.Any(sitemap => sitemap is { Error: null, StatusCode: >= 200 and < 300 }))
            {
                // Orphans are sitemap pages nothing links to, and coverage compares with what the sitemaps list.
                Skip(notes, skipped, "No sitemap could be read, so orphan pages and pages missing from the sitemaps weren't checked.",
                    IssueCatalog.ORPHAN_PAGE, IssueCatalog.NOT_IN_SITEMAP);
            }
            else if (SitemapRules.Unreadable(sitemaps) is { } unreadable)
            {
                Skip(notes, skipped, unreadable.NotReadReason is { } reason
                        ? $"Pages missing from the sitemaps weren't checked: {reason}, so {unreadable.Url} and any more like it weren't read, and they may list them."
                        : $"Pages missing from the sitemaps weren't checked: {unreadable.Url} couldn't be read ({unreadable.Error}), and it may list them.",
                    IssueCatalog.NOT_IN_SITEMAP);
            }

            // What the crawl was told not to fetch can't be found broken, which isn't the same as finding it fine.
            if (!options.CheckExternalLinks)
            {
                Skip(notes, skipped, "Links to other sites weren't checked in this crawl, so broken external links weren't looked for.",
                    IssueCatalog.BROKEN_EXTERNAL_LINKS);
            }

            if (!options.CheckResources)
            {
                Skip(notes, skipped, "Images weren't fetched in this crawl, so broken and oversized images weren't looked for.",
                    IssueCatalog.IMAGE_BROKEN, IssueCatalog.IMAGE_TOO_LARGE);
            }

            if (reader.CountNotChecked() is > 0 and var notChecked)
            {
                notes.Add(notChecked == 1
                    ? "1 URL wasn't checked because its site had stopped answering, so links to it aren't judged."
                    : $"{notChecked:N0} URLs weren't checked because their sites had stopped answering, so links to them aren't judged.");
            }

            // The site lives on the start URL's origin and the home page's (after redirects); robots.txt failing there stops all crawling.
            var mainUrls = new[] { startUrl, reader.ReadUrl(urls.HomeId) }.OfType<string>().Select(url => new Uri(url)).ToList();
            var mainOrigins = mainUrls.Select(uri => uri.GetLeftPart(UriPartial.Authority)).ToHashSet(StringComparer.Ordinal);

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
                new SitemapRules(urls, sitemaps, options, crawlComplete, reader.ReadOffHostSitemapEntries(mainUrls.Select(uri => uri.Authority).Distinct().ToList())),
                new RobotsTxtRules(urls, reader.ReadRobots(), scope, mainOrigins)
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

                    Store(hits);
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
                    finishFailed = true;
                    continue;
                }

                Store(hits);
            }
        }

        // A skipped check stores nothing, whatever its rule found on the way: it isn't in the results, so its rows
        // would turn up only in exports.
        void Store(List<IssueHit> found)
        {
            foreach (var hit in found.Where(hit => !skipped.Contains(hit.Issue)))
            {
                writer.Add(hit);
            }
        }

        foreach (var (rule, count) in failures)
        {
            _logger.LogWarning("Rule {Rule} failed on {Pages} pages; its results for them are missing", rule, count);
            notes.Add($"{rule} failed on {RuleText.Count(count, "page", "pages")}, so its results there are missing (see the log).");
        }

        // The reader is closed by now, so the writer can fold the write-ahead log when it closes.
        var info = new AnalysisInfo(DateTimeOffset.UtcNow, EngineInfo.Version, pages, stopwatch.Elapsed);
        var checks = IssueCatalog.All.Where(issue => !skipped.Contains(issue)).ToList();
        var counts = writer.Commit(checks, info, JsonSerializer.Serialize(settings, EngineJsonContext.Default.AnalysisSettings), notes);
        var issues = checks.Select(issue => new IssueCount(issue, counts.GetValueOrDefault(issue.Id))).ToList();

        _logger.LogInformation("Analysed {Pages} pages in {Elapsed}: {Flagged} issues across {Types} checks",
            pages, stopwatch.Elapsed, issues.Sum(issue => issue.Count), issues.Count(issue => issue.Count > 0));
        // Partial when a check was skipped, or a rule failed and left results out.
        return new AnalysisSummary(pages, issues, stopwatch.Elapsed, IsPartial: skipped.Count > 0 || failures.Count > 0 || finishFailed) { Notes = notes };
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
