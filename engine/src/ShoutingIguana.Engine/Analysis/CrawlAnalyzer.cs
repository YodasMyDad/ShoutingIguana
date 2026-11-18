using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Configuration;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Parsing.Models;
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

    private const string RULE_FAILED_TEXT = "The check failed during the analysis, so it has no results. The log says why.";

    /// <summary>Checks besides the orphans' that need every page that could link to a page, sitemaps aside: skipped as orphans are when the crawl didn't read them all.</summary>
    private static readonly IssueDefinition[] LinkingPageChecks = [IssueCatalog.CANONICAL_UNLINKED, IssueCatalog.NON_INDEXABLE_INLINKS_ONLY, IssueCatalog.HREFLANG_UNLINKED];

    /// <summary>The orphan check is skipped once 1 in this many of the pages the crawl tried to read couldn't be read (<see cref="SkippedCheckReasons.PAGES_UNREAD"/>).</summary>
    private const int UNREAD_PAGES_ONE_IN = 20;

    private readonly ILogger _logger = loggerFactory.CreateLogger<CrawlAnalyzer>();

    /// <summary>Tests only: called with each site rule just before it reports, where its failure is caught, so a test can make one fail.</summary>
    internal Action<object>? Finishing { get; set; }

    /// <summary>Analyses a crawl that is not being written to. A crawl still running (or interrupted) is refused.</summary>
    public Task<AnalysisSummary> AnalyzeAsync(string databasePath, AnalysisSettings? settings = null, CancellationToken cancellationToken = default) =>
        AnalyzeAsync(databasePath, settings, progress: null, cancellationToken);

    /// <summary>Analyses a crawl that is not being written to, saying how far it has got as it goes.</summary>
    /// <param name="progress">
    /// Hears of each phase as it starts and as it ends, and a few times a second between, on the analysis's own thread and so
    /// in order. Report straight from it: <see cref="Progress{T}"/> hands each report to the thread pool, which can reorder them.
    /// </param>
    public Task<AnalysisSummary> AnalyzeAsync(string databasePath, AnalysisSettings? settings, IProgress<AnalysisProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        settings ??= new AnalysisSettings();
        var errors = settings.Validate();
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(settings));
        }

        return Task.Run(() => Analyze(databasePath, settings, new AnalysisProgressReporter(progress, TimeProvider.System), cancellationToken),
            cancellationToken);
    }

    private AnalysisSummary Analyze(string databasePath, AnalysisSettings settings, AnalysisProgressReporter progress, CancellationToken cancellationToken)
    {
        AnalysisSummary summary;

        // Opening the writer locks the file against crawls first, then refuses a crawl that is running or was interrupted.
        using (var writer = IssueWriter.Open(databasePath, _logger))
        {
            summary = AnalyzeWith(writer, databasePath, settings, progress, cancellationToken);
        }

        // Committed, and the file folded back into one: there's nothing left to do.
        progress.Finish(1);
        return summary;
    }

    private AnalysisSummary AnalyzeWith(IssueWriter writer, string databasePath, AnalysisSettings settings, AnalysisProgressReporter progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        List<string> notes = [];
        Dictionary<IssueDefinition, SkippedCheck> skipped = [];
        Dictionary<string, int> failures = new(StringComparer.Ordinal);
        List<IssueHit> hits = [];
        var finishFailed = false;

        long pages = 0;
        UrlTable urls;
        ScoreIncompleteReason? incomplete;
        using (var reader = AnalysisReader.Open(databasePath))
        {
            var status = reader.ReadStatus();
            progress.Start(AnalysisPhase.Reading, reader.CountUrls());
            urls = reader.ReadUrls(progress);
            var options = reader.ReadOptions();
            var sitemaps = reader.ReadSitemaps();
            UrlNormalizer.TryNormalize(options.StartUrl, out var startUrl);
            var scope = new CrawlScope(new Uri(startUrl), options.IncludeSubdomains, options.StayInStartFolder);

            // Orphans and sitemap coverage need every page and sitemap; a stopped crawl can't tell missing from not yet seen.
            // An unlinked canonical needs every page that could link to it, sitemaps or not.
            var finished = status == CrawlStatus.Completed;
            var crawlComplete = finished && (reader.ReadSitemapsDone() || !options.DiscoverSitemaps);
            var limitSkips = reader.ReadLimitSkips();
            var everyLinkingPage = finished && SeesEveryLinkingPage(limitSkips, scope, urls, notes, skipped, orphansJudged: crawlComplete);
            var linksComplete = crawlComplete && everyLinkingPage;
            if (!finished)
            {
                Skip(notes, skipped, note: null, SkippedCheckReasons.CRAWL_UNFINISHED, "The crawl didn't finish.", LinkingPageChecks);
            }
            incomplete = WhyIncomplete(status, limitSkips);
            if (options.DiscoverSitemaps && !reader.HasSitemapLastmods())
            {
                Skip(notes, skipped,
                    $"{IssueCatalog.SITEMAP_LASTMOD_INVALID.Title} wasn't checked: this crawl was made by an earlier version, which didn't record what the check needs. Crawl again to check it.",
                    SkippedCheckReasons.EARLIER_VERSION, "This crawl was made by an earlier version, which didn't record what this check needs. Crawl again to check it.",
                    IssueCatalog.SITEMAP_LASTMOD_INVALID);
            }
            if (!options.DiscoverSitemaps)
            {
                Skip(notes, skipped, "Sitemaps weren't read in this crawl, so orphan pages and the sitemap checks weren't run.",
                    SkippedCheckReasons.SITEMAPS_OFF, "Sitemaps weren't read in this crawl.",
                    [IssueCatalog.ORPHAN_PAGE, .. IssueCatalog.All.Where(issue => issue.Category == IssueCategory.Sitemaps)]);
            }
            else if (!crawlComplete)
            {
                Skip(notes, skipped, finished
                        ? "Not every sitemap was read, so orphan pages and sitemap coverage weren't checked."
                        : "The crawl didn't finish, so orphan pages and sitemap coverage weren't checked.",
                    finished ? SkippedCheckReasons.SITEMAPS_UNFINISHED : SkippedCheckReasons.CRAWL_UNFINISHED,
                    finished ? "Not every sitemap was read." : "The crawl didn't finish.",
                    IssueCatalog.ORPHAN_PAGE, IssueCatalog.NOT_IN_SITEMAP, IssueCatalog.SITEMAP_MISSING);
            }
            else if (!sitemaps.Any(sitemap => sitemap is { Error: null, StatusCode: >= 200 and < 300 }))
            {
                // Orphans are sitemap pages nothing links to, and coverage compares with what the sitemaps list.
                Skip(notes, skipped, "No sitemap could be read, so orphan pages and pages missing from the sitemaps weren't checked.",
                    SkippedCheckReasons.NO_SITEMAP, "No sitemap could be read.",
                    IssueCatalog.ORPHAN_PAGE, IssueCatalog.NOT_IN_SITEMAP);
            }
            else if (SitemapRules.Unreadable(sitemaps) is { } unreadable)
            {
                if (unreadable.NotReadReason is { } notRead)
                {
                    // Crawl files are shared, and one can hold the "Not read" prefix with no reason after it.
                    var reason = string.IsNullOrWhiteSpace(notRead) ? "the crawler left it unread" : notRead;
                    Skip(notes, skipped,
                        $"Pages missing from the sitemaps weren't checked: {reason}, so {unreadable.Url} and any more like it weren't read, and they may list them.",
                        SkippedCheckReasons.SITEMAPS_NOT_READ, $"{char.ToUpperInvariant(reason[0])}{reason[1..]}, so {unreadable.Url} and any more like it weren't read.",
                        IssueCatalog.NOT_IN_SITEMAP);
                }
                else
                {
                    Skip(notes, skipped,
                        $"Pages missing from the sitemaps weren't checked: {unreadable.Url} couldn't be read ({unreadable.Error}), and it may list them.",
                        SkippedCheckReasons.SITEMAP_UNREADABLE, $"{unreadable.Url} couldn't be read ({unreadable.Error}).",
                        IssueCatalog.NOT_IN_SITEMAP);
                }
            }

            // What the crawl was told not to fetch can't be found broken, which isn't the same as finding it fine.
            if (!options.CheckExternalLinks)
            {
                Skip(notes, skipped, "Links to other sites weren't checked in this crawl, so broken external links weren't looked for.",
                    SkippedCheckReasons.EXTERNAL_LINKS_OFF, "Links to other sites weren't checked in this crawl.",
                    IssueCatalog.BROKEN_EXTERNAL_LINKS, IssueCatalog.SLOW_EXTERNAL_LINKS);
            }

            if (!options.CheckResources)
            {
                Skip(notes, skipped, "Images weren't fetched in this crawl, so broken and oversized images weren't looked for.",
                    SkippedCheckReasons.RESOURCES_OFF, "Images weren't fetched in this crawl.",
                    IssueCatalog.IMAGE_BROKEN, IssueCatalog.IMAGE_TOO_LARGE, IssueCatalog.IMAGE_NO_SRCSET, IssueCatalog.IMAGE_LEGACY_FORMAT,
                    IssueCatalog.SOCIAL_IMAGE_BROKEN, IssueCatalog.SOCIAL_IMAGE_TOO_LARGE, IssueCatalog.SOCIAL_IMAGE_TOO_SMALL);
                Skip(notes, skipped, note: null,
                    SkippedCheckReasons.RESOURCES_OFF, "Scripts, stylesheets and other files weren't fetched in this crawl.",
                    IssueCatalog.BROKEN_RESOURCES, IssueCatalog.CONTENT_TYPE_MISMATCH, IssueCatalog.RESOURCE_PAST_CRAWL_LIMIT);
            }


            // The crawler probes the site's other versions first thing, but a crawl stopped before then, or one from before
            // it did, doesn't know how they answer; nor is there a main version without a home page.
            var siteVersions = new SiteVersionRules(urls, new Uri(startUrl), reader.ReadUrlId, reader.ReadUrl);

            // Whether a site hides itself is judged by its home page, and a crawl started anywhere else has none to judge
            // by: its first page is a section's, or some page of the site. A query on the root is still the root, and so
            // is a root that redirects (to /en/, to the www host): the home page is the page it ends on.
            if (new Uri(startUrl).AbsolutePath is not "/" and var startPath)
            {
                Skip(notes, skipped, $"A site hidden from search wasn't looked for: the crawl started at {startPath}, not at the site's root, so its first page isn't known to be the site's home page.",
                    SkippedCheckReasons.START_NOT_ROOT, $"The crawl started at {startPath}, not at the site's root, so its first page isn't known to be the site's home page.",
                    IssueCatalog.SITE_HIDDEN_FROM_SEARCH);
            }
            if (!siteVersions.CanCheck)
            {
                Skip(notes, skipped, "The site's other versions (www or not, http or https) weren't all checked, so duplicate versions of the site weren't looked for.",
                    SkippedCheckReasons.SITE_VERSIONS_UNCHECKED, "The site's other versions (www or not, http or https) weren't all checked.",
                    IssueCatalog.DUPLICATE_SITE_VERSION);
            }

            var rateLimited = reader.CountNotCheckedFor(RetryingFetcher.RATE_LIMITED_ERROR);
            var privateAddresses = reader.CountNotCheckedFor(PrivateNetworkGuard.ERROR);
            if (reader.CountNotChecked() - rateLimited - privateAddresses is > 0 and var notChecked)
            {
                notes.Add(notChecked == 1
                    ? "1 URL wasn't checked because its site had stopped answering, so links to it aren't judged."
                    : $"{notChecked:N0} URLs weren't checked because their sites had stopped answering, so links to them aren't judged.");
            }

            if (rateLimited > 0)
            {
                notes.Add(rateLimited == 1
                    ? "1 URL wasn't checked because its site kept asking the crawler to slow down (429 Too Many Requests), so links to it aren't judged. Check the site again later, or at the Polite speed."
                    : $"{rateLimited:N0} URLs weren't checked because their sites kept asking the crawler to slow down (429 Too Many Requests), so links to them aren't judged. Check the site again later, or at the Polite speed.");
            }

            if (privateAddresses > 0)
            {
                notes.Add(privateAddresses == 1
                    ? "1 URL wasn't checked because it is on a private network address (such as localhost or 192.168.x.x), which a crawl of a public site doesn't ask, so links to it aren't judged."
                    : $"{privateAddresses:N0} URLs weren't checked because they are on private network addresses (such as localhost or 192.168.x.x), which a crawl of a public site doesn't ask, so links to them aren't judged.");
            }

            // The site lives on the start URL's origin and the home page's (after redirects); robots.txt failing there stops all crawling.
            var mainUrls = new[] { startUrl, reader.ReadUrl(urls.HomeId) }.OfType<string>().Select(url => new Uri(url)).ToList();
            var mainOrigins = mainUrls.Select(uri => uri.GetLeftPart(UriPartial.Authority)).ToHashSet(StringComparer.Ordinal);

            // A crawl told to ignore robots.txt asks for every URL, so none is refused, and it reads robots.txt only where
            // something else needs it: sitemap discovery asks the start URL's origin for it. What it didn't read it can't
            // judge, which isn't finding it fine: nothing blocked, no other origin's file read, and the site's own judged
            // only when it was read.
            var robots = reader.ReadRobots();
            if (!options.RespectRobotsTxt)
            {
                var ownWasRead = robots.Any(file => mainOrigins.Contains(file.Origin));
                Skip(notes, skipped, ownWasRead
                        ? "robots.txt was ignored in this crawl, so the URLs it blocks weren't looked for, nor whether it is unavailable on the site's other origins."
                        : "robots.txt was ignored in this crawl and never read, so the URLs it blocks, and whether it is missing or unavailable, weren't looked for.",
                    SkippedCheckReasons.ROBOTS_TXT_IGNORED, "robots.txt was ignored in this crawl.",
                    IssueCatalog.BLOCKED_BY_ROBOTS);
                Skip(notes, skipped, note: null,
                    SkippedCheckReasons.ROBOTS_TXT_IGNORED, "robots.txt was ignored in this crawl, so it wasn't read on the site's other origins.",
                    IssueCatalog.ROBOTS_TXT_UNREACHABLE_OTHER_ORIGIN);
                if (!ownWasRead)
                {
                    Skip(notes, skipped, note: null,
                        SkippedCheckReasons.ROBOTS_TXT_IGNORED, "robots.txt was ignored in this crawl, and nothing else asked for it, so it was never read.",
                        IssueCatalog.ROBOTS_TXT_UNREACHABLE, IssueCatalog.ROBOTS_TXT_MISSING);
                }
            }

            var hreflang = new HreflangRules(urls, reader.ReadUrl, reader.ReadNotFollowedUrls());
            var indexability = new IndexabilityRules();
            var robotsToken = UserAgents.Resolve(options.UserAgent, options.CustomUserAgent).RobotsToken;
            IPageRule[] pageRules =
            [
                new TitleRules(settings), new DescriptionRules(settings), new HeadingRules(), new ContentRules(settings),
                new ImageRules(settings, urls, reader.ReadUrlId, scope, PrivateNetworkGuard.IsPrivateLiteral(new Uri(startUrl)), reader.ReadUrl),
                new TextStructureRules(settings), new SocialRules(urls), new LinkRules(urls), new CanonicalRules(urls, reader.ReadUrl, reader.ReadFacts), indexability,
                new DirectiveRules(urls, robotsToken), new LinkStructureRules(settings, urls, reader.ReadUrl, new Uri(startUrl)), hreflang, new StructuredDataRules(),
                new SecurityRules(urls), new UrlRules(settings), new PerformanceRules(settings), new HtmlRules()
            ];

            // An instant meta refresh makes the page a redirect: nobody sees it, so only the refresh itself is reported.
            IPageRule[] redirectRules = [indexability];
            var duplicates = new DuplicateRules(settings);
            ISiteRule[] siteRules =
            [
                new InlinkRules(urls, linksComplete, everyLinkingPage), duplicates, new FragmentRules(urls, reader.ReadFacts, reader.ReadFragmentLinks, reader.ReadUrl), hreflang,
                new ResponseCodeRules(urls, reader.ReadBlockReasons()), new RedirectRules(urls, reader.ReadUrl),
                new SitemapRules(urls, sitemaps, options, crawlComplete, reader.ReadOffHostSitemapEntries(mainUrls.Select(uri => uri.Authority).Distinct().ToList()),
                    new SitemapEntryFacts
                    {
                        Repeated = reader.ReadRepeatedSitemapEntries(),
                        // Not one on the site's own scheme: a sitemap declared on http:// that redirects to https:// lists
                        // https:// URLs, which is right.
                        OtherScheme = reader.ReadOtherSchemeSitemapEntries().Where(entry => mainUrls.All(uri => uri.Scheme != entry.Value.Url))
                            .ToDictionary(),
                        Lastmods = reader.ReadSitemapLastmodFaults(LatestLastmod(reader))
                    }),
                new RobotsTxtRules(urls, robots, scope, mainOrigins, reader.ReadSiteVersionOnlyOrigins(), sitemaps), siteVersions,
                new SiteVisibilityRules(urls), new RedirectHeaderRules(urls, reader.ReadRedirectHeaders, reader.ReadUrl),
                new PaginationRules(urls, reader.ReadUrlId, reader.ReadUrl), new ResourceRules(urls, reader.ReadContentType),
                new UrlTextRules(urls, reader.ReadInternalUrls())
            ];

            // Pages parsed before each collection of facts that a check needs (CollectedFacts), counted as they're read.
            var collections = CollectedFacts.Needs.Values.Distinct().Order().ToArray();
            var parsedBefore = new long[collections.Length];

            writer.AddIndexability(urls.IndexabilityChanges);
            progress.Finish();
            progress.Start(AnalysisPhase.PageRules, urls.PagesToAudit());
            foreach (var page in reader.ReadPages(urls))
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress.Advance(++pages);
                for (var collection = 0; collection < collections.Length; collection++)
                {
                    if (page.Facts.CollectedVersion < collections[collection])
                    {
                        parsedBefore[collection]++;
                    }
                }

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

            progress.Finish(pages);

            // Pages an earlier version crawled have only the first lane of their content fingerprint, and any page could be
            // the near copy of one of them, so with any the check can't be made.
            if (duplicates.PagesWithoutFingerprint is > 0 and var unfingerprinted)
            {
                var why = duplicates.PagesWithFingerprint == 0
                    ? "this crawl was made by an earlier version"
                    : $"{RuleText.Count(unfingerprinted, "page", "pages")} in this crawl {(unfingerprinted == 1 ? "was" : "were")} fetched by an earlier version";
                Skip(notes, skipped, $"Near-duplicate content wasn't checked: {why}, which didn't record what the check needs. Crawl again to check it.",
                    SkippedCheckReasons.EARLIER_VERSION, $"{char.ToUpperInvariant(why[0])}{why[1..]}, which didn't record what this check needs. Crawl again to check it.",
                    IssueCatalog.NEAR_DUPLICATE_CONTENT);
            }

            SkipChecksNeedingNewerFacts(writer, notes, skipped, collections, parsedBefore, pages);

            progress.Start(AnalysisPhase.SiteRules, siteRules.Length);
            var rulesDone = 0;
            foreach (var rule in siteRules)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress.Advance(rulesDone++);
                hits.Clear();
                var declared = rule.Checks;
                try
                {
                    Finishing?.Invoke(rule);
                    hits.AddRange(rule.Finish());

                    // What a rule reports must be what its failure would skip, or a failure would leave a check looking clean.
                    if (hits.FindIndex(hit => !declared.Contains(hit.Issue)) is >= 0 and var undeclared)
                    {
                        throw new InvalidOperationException($"{RuleName(rule)} reported {hits[undeclared].Issue.Id}, which isn't one of the checks it declares.");
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _logger.LogError(exception, "Rule {Rule} failed after the page pass; its cross-page results are left out", RuleName(rule));
                    notes.Add($"{RuleName(rule)} failed after reading every page, so its cross-page results are missing (see the log).");
                    finishFailed = true;

                    // Its checks have no results, which isn't finding nothing: they are skipped, each saying why.
                    foreach (var check in declared)
                    {
                        skipped.TryAdd(check, new SkippedCheck(check.Id, SkippedCheckReasons.RULE_FAILED, RULE_FAILED_TEXT));
                    }

                    continue;
                }

                Store(hits);
            }

            progress.Finish(siteRules.Length);
        }

        // A skipped check stores nothing, whatever its rule found on the way: it isn't in the results, so its rows
        // would turn up only in exports.
        void Store(List<IssueHit> found)
        {
            foreach (var hit in found.Where(hit => !skipped.ContainsKey(hit.Issue)))
            {
                writer.Add(hit);
            }
        }

        foreach (var (rule, count) in failures)
        {
            _logger.LogWarning("Rule {Rule} failed on {Pages} pages; its results for them are missing", rule, count);
            notes.Add($"{rule} failed on {RuleText.Count(count, "page", "pages")}, so its results there are missing (see the log).");
        }

        // Partial when a check was skipped, or a rule failed and left results out.
        var isPartial = skipped.Count > 0 || failures.Count > 0 || finishFailed;
        List<SkippedCheck> skips = [.. IssueCatalog.All.Where(skipped.ContainsKey).Select(issue => skipped[issue])];

        // The reader is closed by now, so the writer can fold the write-ahead log when it closes.
        var info = new AnalysisInfo(DateTimeOffset.UtcNow, EngineInfo.Version, pages, stopwatch.Elapsed)
        {
            IsPartial = isPartial,
            AnalysisVersion = AnalysisFreshness.VERSION,
            ChecksFingerprint = AnalysisFreshness.ChecksFingerprint
        };
        var checks = IssueCatalog.All.Where(issue => !skipped.ContainsKey(issue)).ToList();
        // The not-in-sitemap check runs only when every sitemap was read: then the summary can say a URL isn't in one.
        var committed = writer.Commit(checks, info, JsonSerializer.Serialize(settings, EngineJsonContext.Default.AnalysisSettings), notes, skips, urls,
            sitemapsCovered: !skipped.ContainsKey(IssueCatalog.NOT_IN_SITEMAP), incomplete, progress);
        var issues = checks.Select(issue => new IssueCount(issue, committed.Counts.GetValueOrDefault(issue.Id))).ToList();

        _logger.LogInformation("Analysed {Pages} pages in {Elapsed}: {Flagged} issues across {Types} checks, score {Score}",
            pages, stopwatch.Elapsed, issues.Sum(issue => issue.Count), issues.Count(issue => issue.Count > 0), committed.Score?.Value);
        return new AnalysisSummary(pages, issues, stopwatch.Elapsed, isPartial)
        {
            Notes = notes, Skipped = skips, CrawledUrlCount = committed.CrawledUrlCount, Score = committed.Score
        };
    }

    /// <summary>
    /// Skips each check that needs a fact some pages were parsed without (<see cref="CollectedFacts"/>), as near duplicates
    /// are skipped on pages without every lane of their fingerprint, and takes back what its rule found on the other pages:
    /// a result for some pages only would read as the whole crawl's.
    /// </summary>
    /// <param name="collections">Each collection a check needs, in order.</param>
    /// <param name="parsedBefore">For each, how many of the pages read were parsed before it.</param>
    /// <summary>
    /// The latest a sitemap's lastmod may be, in Unix seconds: a day after the crawl last ran, so a time zone or a clock a
    /// little ahead isn't "the future".
    /// </summary>
    private static long LatestLastmod(AnalysisReader reader) =>
        (reader.ReadCrawlUpdatedAt() ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) / 1000 + 86_400;

    private void SkipChecksNeedingNewerFacts(IssueWriter writer, List<string> notes, Dictionary<IssueDefinition, SkippedCheck> skipped, int[] collections,
        long[] parsedBefore, long pages)
    {
        for (var collection = 0; collection < collections.Length; collection++)
        {
            if (parsedBefore[collection] == 0)
            {
                continue;
            }

            var checks = CollectedFacts.Needs.Where(need => need.Value == collections[collection] && !skipped.ContainsKey(need.Key))
                .Select(need => need.Key).OrderBy(check => check.Id, StringComparer.Ordinal).ToArray();
            if (checks.Length == 0)
            {
                continue;
            }

            var older = (int)Math.Min(parsedBefore[collection], int.MaxValue);
            var why = older == pages
                ? "this crawl was made by an earlier version"
                : $"{RuleText.Count(older, "page", "pages")} in this crawl {(older == 1 ? "was" : "were")} fetched by an earlier version";
            var note = checks.Length == 1
                ? $"{checks[0].Title} wasn't checked: {why}, which didn't record what the check needs. Crawl again to check it."
                : $"{checks.Length} checks weren't run: {why}, which didn't record what they need. Crawl again to run them.";
            Skip(notes, skipped, note, SkippedCheckReasons.EARLIER_VERSION,
                $"{char.ToUpperInvariant(why[0])}{why[1..]}, which didn't record what this check needs. Crawl again to check it.", checks);
            writer.Discard(checks);
        }
    }

    /// <summary>
    /// Why the crawl didn't read all of the site, for the score to say it is of what was read so far: it was stopped or
    /// failed, or it finished with a limit keeping internal URLs out. Null when it read all it set out to. A crawl kept to
    /// its start folder did: that is its scope, not a part of it.
    /// </summary>
    private static ScoreIncompleteReason? WhyIncomplete(CrawlStatus status, Dictionary<SkipReason, long> limitSkips) => status switch
    {
        CrawlStatus.Failed => ScoreIncompleteReason.Failed,
        not CrawlStatus.Completed => ScoreIncompleteReason.Stopped,
        _ when limitSkips.GetValueOrDefault(SkipReason.UrlLimit) > 0 => ScoreIncompleteReason.UrlLimit,
        _ when limitSkips.GetValueOrDefault(SkipReason.DepthLimit) > 0 => ScoreIncompleteReason.DepthLimit,
        _ => null
    };

    /// <summary>
    /// Whether the crawl saw every page that could link to a sitemap page, which the orphan check needs. A depth or
    /// URL limit leaves pages out, and a crawl kept to a folder never reads the links on the rest of the site. Nor are
    /// the links known on pages that refused the crawler or didn't answer: once they are 1 in
    /// <see cref="UNREAD_PAGES_ONE_IN"/> of the pages it tried to read, as when a shop rate-limits every product page, the
    /// check is skipped. A few stray failures don't stop it, or it would hardly ever run on a large site.
    /// </summary>
    /// <param name="orphansJudged">
    /// Orphans would be checked, every sitemap having been read: they're skipped with a note for what keeps pages out. When
    /// they aren't, they already have their reason, and only unlinked canonicals are skipped here.
    /// </param>
    private bool SeesEveryLinkingPage(Dictionary<SkipReason, long> limitSkips, CrawlScope scope, UrlTable urls, List<string> notes,
        Dictionary<IssueDefinition, SkippedCheck> skipped, bool orphansJudged)
    {
        var complete = true;
        IssueDefinition[] checks = orphansJudged ? [IssueCatalog.ORPHAN_PAGE, .. LinkingPageChecks] : LinkingPageChecks;
        string? Note(string note) => orphansJudged ? note : null;
        foreach (var (reason, limit, code) in new[]
                 {
                     (SkipReason.DepthLimit, "depth limit", SkippedCheckReasons.DEPTH_LIMIT), (SkipReason.UrlLimit, "URL limit", SkippedCheckReasons.URL_LIMIT)
                 })
        {
            if (limitSkips.GetValueOrDefault(reason) is > 0 and var count)
            {
                var left = string.Create(CultureInfo.InvariantCulture, $"{count:N0} internal URL{(count == 1 ? string.Empty : "s")}");
                Skip(notes, skipped, Note($"Orphan pages weren't checked: the {limit} kept {left} out of the crawl, and pages linked only from them would look orphaned."),
                    code, $"The {limit} kept {left} out of the crawl, and pages linked only from them would look orphaned.",
                    checks);
                complete = false;
            }
        }

        if (scope.StartFolder != "/")
        {
            Skip(notes, skipped, Note($"Orphan pages weren't checked: the crawl stayed in {scope.StartFolder}, so links from the rest of the site weren't read."),
                SkippedCheckReasons.START_FOLDER, $"The crawl stayed in {scope.StartFolder}, so links from the rest of the site weren't read.",
                checks);
            complete = false;
        }

        var (read, unread) = urls.CountPagesRead();
        if (unread > 0 && (long)unread * UNREAD_PAGES_ONE_IN >= read + unread)
        {
            var why = string.Create(CultureInfo.InvariantCulture,
                $"{unread:N0} of the {read + unread:N0} pages the crawl tried to read refused it or didn't answer, and pages linked only from them would look orphaned.");
            Skip(notes, skipped, Note($"Orphan pages weren't checked: {why}"), SkippedCheckReasons.PAGES_UNREAD, why, checks);
            complete = false;
        }

        return complete;
    }

    /// <summary>Leaves checks out of the results, with a note for the analysis and a reason for each check.</summary>
    /// <param name="note">One sentence for the analysis notes, naming the checks. Null when another skip's note already covers these.</param>
    /// <param name="reason">The stable code (<see cref="SkippedCheckReasons"/>).</param>
    /// <param name="text">Why, to show beside each check. A check skipped for more than one reason keeps the first.</param>
    private void Skip(List<string> notes, Dictionary<IssueDefinition, SkippedCheck> skipped, string? note, string reason, string text,
        params IssueDefinition[] issues)
    {
        _logger.LogWarning("Skipped {Checks}: {Reason}", string.Join(", ", issues.Select(issue => issue.Id)), note ?? text);
        if (note is not null)
        {
            notes.Add(note);
        }

        foreach (var issue in issues)
        {
            skipped.TryAdd(issue, new SkippedCheck(issue.Id, reason, text));
        }
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
