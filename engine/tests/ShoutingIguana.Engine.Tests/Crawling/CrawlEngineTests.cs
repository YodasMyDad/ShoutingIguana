using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Analysis;
using ShoutingIguana.Engine.Tests.Storage;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>End-to-end crawls of a local synthetic site whose planted issues are known exactly.</summary>
public sealed class CrawlEngineTests : IAsyncLifetime
{
    private const int PAGES = 600;
    private const int VARIANT_PAGES = 250;

    /// <summary>A URL the site led to, as opposed to the crawler's probe of another version of the site.</summary>
    private static readonly string NOT_A_PROBE = $"source <> {(int)DiscoverySource.SiteVersion}";
    private static readonly TimeSpan CRAWL_TIMEOUT = TimeSpan.FromMinutes(2);

    private readonly List<string> _databases = [];
    private SyntheticSiteServer _server = null!;

    public async Task InitializeAsync() =>
        _server = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = PAGES });

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        foreach (var path in _databases.SelectMany(path => new[] { path, path + "-wal", path + "-shm" }))
        {
            File.Delete(path);
        }
    }

    [Fact]
    public Task FindsEveryPlantedIssue() => AssertFindsExactlyThePlantsAsync(_server, Options());

    [Fact]
    public async Task FindsEveryPlantedIssueOverHttps()
    {
        await using var server = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = VARIANT_PAGES, Https = true });
        var path = await AssertFindsExactlyThePlantsAsync(server, Options(server) with { TrustedCertificate = server.Certificate });

        // Most real sites are served over HTTP/2, so prove that path works. Kestrel doesn't offer it over TLS on every macOS.
        if (!OperatingSystem.IsMacOS())
        {
            Assert.True(Count(path, "SELECT COUNT(*) FROM responses WHERE http_version = '2.0'") > 0);
        }
    }

    /// <summary>
    /// The site links its https:// version, which doesn't answer: the URL asked for is unreachable, and those the crawler
    /// came to only once it had written the twin off weren't checked, so nothing is said about them, in the issues, the
    /// stats or the URL summary.
    /// </summary>
    [Fact]
    public async Task FindsEveryPlantedIssueWithADeadTwin()
    {
        await using var server = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = VARIANT_PAGES, DeadTwin = true });
        var path = await AssertFindsExactlyThePlantsAsync(server, Options(server));
        Assert.Equal(SyntheticSite.DEAD_TWIN_UNCHECKED, CrawlStatsReader.Read(path).NotCheckedCount);

        // They have an outcome, but weren't crawled: the library's count of URLs crawled leaves them out, as the URL grid does.
        var listed = CrawlSummaryReader.Read(path);
        Assert.Equal(listed.FetchedUrlCount - SyntheticSite.DEAD_TWIN_UNCHECKED, listed.CrawledUrlCount);
        Assert.Equal(listed.CrawledUrlCount, UrlGridReader.Query(path, new UrlQuery { Filters = [GridQueries.Boolean("fetched", true)], Limit = 1 }).Matching);
    }

    [Fact]
    public async Task ReportsASiteWithoutSitemaps()
    {
        await using var server = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = VARIANT_PAGES, Sitemaps = false });
        await AssertFindsExactlyThePlantsAsync(server, Options(server));
    }

    /// <summary>
    /// Every product is noindex, as on a site launched with its staging setting left on: more than half of the pages keep
    /// themselves out of search, which is one fault of the whole site, reported once, on the home page, with the numbers.
    /// The site without it (every other test here, and the benchmark) is never flagged: the whole issues table is compared.
    /// </summary>
    [Fact]
    public async Task FindsASiteHiddenFromSearch()
    {
        await using var server = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = VARIANT_PAGES, HiddenFromSearch = true });
        var path = await AssertFindsExactlyThePlantsAsync(server, Options(server));

        var expected = server.Site.GetExpectations();
        Assert.True(expected.Noindex >= VARIANT_PAGES - 1 && (expected.Noindex + expected.Canonicalised) * 2 > expected.AnalysedPages);
        var hidden = Assert.Single(IssueReader.ReadIssues(path, IssueCatalog.SITE_HIDDEN_FROM_SEARCH.Id, 10));
        Assert.Equal(
            (server.BaseUri.AbsoluteUri, (string?)null, string.Create(CultureInfo.InvariantCulture,
                $"{expected.Noindex + expected.Canonicalised:N0} of {expected.AnalysedPages:N0} pages: {expected.Noindex:N0} noindex, {expected.Canonicalised:N0} canonicalised")),
            (hidden.Url, hidden.RelatedUrl, hidden.Detail));

        // A noindex page is a notice and costs nothing; the site being hidden is serious and site-wide. With the site on
        // plain HTTP as well, nothing is left of the score: a site search engines can't list never reads as fine.
        var score = IssueReader.ReadResults(path)!.Score!;
        Assert.Contains(score.Lines, line => line is { CheckId: "site-hidden-from-search", Severity: IssueSeverity.Error, Extent: ScoreExtent.Site, Points: 60 });
        Assert.Contains(score.Lines, line => line is { CheckId: "noindex-page", Severity: IssueSeverity.Notice, Points: 0 } && line.Count == expected.Noindex);
        Assert.Equal((0, ScoreBand.Poor), (score.Value, score.Band));
    }

    /// <summary>Crawls the site and checks the crawl and every issue against what the site planted; returns the crawl file.</summary>
    private async Task<string> AssertFindsExactlyThePlantsAsync(SyntheticSiteServer server, CrawlOptions options)
    {
        var path = NewDatabasePath();
        var summary = await CrawlAsync(options, path);
        Assert.Equal(CrawlStatus.Completed, summary.Status);
        AssertDownloadedIsWhatWasRead(summary, path);
        ProgressAgreement.AssertMatchesTheFile(summary.Progress, path);
        await AssertMatchesThePlantsAsync(server, path);
        return path;
    }

    /// <summary>Checks a completed crawl of the site, and an analysis of it, against everything the site planted.</summary>
    private static async Task AssertMatchesThePlantsAsync(SyntheticSiteServer server, string path)
    {
        // The stats report indexability as analysed, which knows where canonicals and meta refreshes lead.
        var analysis = await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        var stats = CrawlStatsReader.Read(path);
        var expected = server.Site.GetExpectations();
        Assert.Equal(expected.HtmlPages, stats.HtmlPageCount);
        Assert.Equal(expected.Orphans, stats.UnlinkedPageCount);
        Assert.Equal(expected.Redirects, stats.Outcomes.GetValueOrDefault(FetchOutcome.Redirect));
        Assert.Equal(expected.RobotsDisallowed, stats.Outcomes.GetValueOrDefault(FetchOutcome.RobotsDisallowed));
        Assert.Equal(expected.BrokenUrls + expected.ExternalMissing, stats.StatusCodes.GetValueOrDefault(404));
        Assert.Equal(expected.ServerErrorUrls, stats.StatusCodes.GetValueOrDefault(500));
        Assert.Equal(expected.NofollowOnly, stats.SkipReasons.GetValueOrDefault(SkipReason.NoFollow));
        Assert.Equal(expected.Noindex, stats.NonIndexableReasons.GetValueOrDefault(IndexabilityReason.Noindex));
        Assert.Equal(expected.Canonicalised, stats.NonIndexableReasons.GetValueOrDefault(IndexabilityReason.Canonicalised));
        Assert.Equal(expected.Unreachable, stats.NonIndexableReasons.GetValueOrDefault(IndexabilityReason.Unreachable));
        Assert.Equal(expected.NotChecked, stats.NotCheckedCount);
        Assert.Equal(0, stats.QueuedCount);
        Assert.Equal(0, Count(path, "SELECT COUNT(*) FROM urls WHERE state = 1"));

        // Response times are the server's: the planted slow page takes 1.2 s, a typical page far less than the 1 s
        // threshold. The slow-response comparison below allows for a busy machine, so it can't prove this itself. The
        // server's delay can end a timer tick early (15.6 ms on Windows), hence the margin.
        var slowPage = $"FROM responses r JOIN urls u ON u.id = r.url_id WHERE u.url = '{server.BaseUri}plants/slow'";
        Assert.True(Count(path, $"SELECT r.response_ms {slowPage}") >= 1_150, string.Join(", ", Strings(path,
            $"SELECT 'outcome ' || r.outcome || ', HTTP ' || COALESCE(r.status_code, '-') || ', ' || COALESCE(r.response_ms, '-') || ' ms, ' || COALESCE(r.error, 'no error') {slowPage}")));
        Assert.True(Count(path, """
            SELECT response_ms FROM responses WHERE is_html = 1 AND outcome = 0 ORDER BY response_ms
            LIMIT 1 OFFSET (SELECT COUNT(*) / 2 FROM responses WHERE is_html = 1 AND outcome = 0)
            """) < new AnalysisSettings().SlowResponseMs / 2);

        // Every planted issue is reported on exactly the planted URLs, and nothing else is. Every check ran that the site
        // supports; the rest are skipped with one note saying why.
        Assert.Equal(expected.AnalysedPages, analysis.PagesAnalyzed);
        Assert.Equal(expected.SkippedChecks.Count > 0, analysis.IsPartial);
        Assert.Equal(expected.NoteCount, analysis.Notes.Count);
        var rows = IssueReader.ReadAll(path).ToList();
        var mismatches = expected.CompareIssues(
            rows.Select(row => (row.Issue, row.Url)),
            IssueCatalog.All.Select(issue => issue.Id),
            CrawlStatsReader.ReadSlowPages(path, new AnalysisSettings().SlowResponseMs));
        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));

        // The per-issue totals the summary shows agree with the flagged rows.
        var flaggedPerIssue = rows.GroupBy(row => row.Issue).ToDictionary(group => group.Key, group => (long)group.Count());
        var counts = IssueReader.ReadCounts(path);
        var ran = IssueCatalog.All.Where(issue => !expected.SkippedChecks.Contains(issue.Id)).ToList();
        Assert.Equal(ran.Select(issue => issue.Id).Order(StringComparer.Ordinal), counts.Keys.Order(StringComparer.Ordinal));
        Assert.All(ran, issue => Assert.Equal((issue.Id, flaggedPerIssue.GetValueOrDefault(issue.Id)), (issue.Id, counts.GetValueOrDefault(issue.Id))));

        // A check that says it is reported once has one issue at most, whatever the site: one for each other version of
        // the site, for the duplicate site version.
        Assert.All(ran.Where(issue => issue.ReportedOnce), issue =>
            Assert.True(counts.GetValueOrDefault(issue.Id) <= (issue == IssueCatalog.DUPLICATE_SITE_VERSION ? 3 : 1), $"{issue.Id} was reported {counts.GetValueOrDefault(issue.Id)} times"));

        // The analysis's own state is stored with it: partial or not, and why each skipped check was skipped.
        var stored = IssueReader.ReadAnalysis(path)!;
        Assert.Equal(analysis.IsPartial, stored.IsPartial);
        Assert.Equal(analysis.Skipped, IssueReader.ReadSkippedChecks(path));
        Assert.Equal(expected.SkippedChecks.Order(StringComparer.Ordinal), analysis.Skipped.Select(skip => skip.CheckId).Order(StringComparer.Ordinal));
        var listed = CrawlSummaryReader.Read(path);
        Assert.Empty(listed.OutOfDate(new AnalysisSettings()));

        // The score is stored with it, worked out from these counts over the pages analysed and the URLs crawled, as the
        // library counts them: the URLs that weren't checked (a dead twin's) are no part of either.
        var score = IssueReader.ReadResults(path)!.Score!;
        Assert.Equal((listed.CrawledUrlCount, listed.CrawledUrlCount), ((long?)analysis.CrawledUrlCount, stored.CrawledUrlCount));
        Assert.Equal(((long)expected.AnalysedPages, listed.CrawledUrlCount), (score.Covers.Pages, (long?)score.Covers.Urls));
        Assert.Equal((ran.Count, expected.SkippedChecks.Count, true), (score.Covers.ChecksRun, score.Covers.ChecksSkipped, score.Covers.Complete));
        Assert.Equal(counts.Where(count => count.Value > 0).Select(count => (count.Key, count.Value)).Order(),
            score.Lines.Select(line => (line.CheckId, line.Count)).Order());
        Assert.Equal(expected.RobotsDisallowed, score.Covers.BlockedByRobots);

        // The URLs grid shows what the checks judged.
        UrlSummaryAgreement.AssertAgreesWithTheChecks(path, expected, stats);

        // A check that says it flags pages flagged nothing else, on this variant of the site.
        IssueSubjectAgreement.AssertPageChecksFlagOnlyPages(path);
    }

    [Fact]
    public async Task StoresEveryLinkWithItsPosition()
    {
        var path = NewDatabasePath();
        await CrawlAsync(Options(), path);

        const int product = 5;
        var url = new Uri(_server.BaseUri, $"/p/{product}").AbsoluteUri;
        var contentLinks = Count(path, $"""
            SELECT COUNT(*) FROM links l JOIN urls u ON u.id = l.from_id
            WHERE u.url = '{url}' AND l.kind = {(int)LinkKind.Hyperlink} AND l.position = {(int)LinkPosition.Content}
            """);
        var navigationLinks = Count(path, $"""
            SELECT COUNT(*) FROM links l JOIN urls u ON u.id = l.from_id
            WHERE u.url = '{url}' AND l.position = {(int)LinkPosition.Navigation}
            """);

        Assert.Equal(_server.Site.ContentLinkCount(product), contentLinks);
        Assert.Equal(1 + SyntheticSite.CATEGORY_COUNT, navigationLinks);
        Assert.Equal(0, Depth(path, _server.BaseUri.AbsoluteUri));
        Assert.Equal(1, Depth(path, new Uri(_server.BaseUri, "/c/0").AbsoluteUri));
    }

    [Fact]
    public async Task RespectsMaxDepth()
    {
        var path = NewDatabasePath();
        await CrawlAsync(Options() with { MaxDepth = 1, DiscoverSitemaps = false }, path);

        var stats = CrawlStatsReader.Read(path);

        // Home, its 20 categories, the 50 products it lists, and the 3 footer pages.
        Assert.Equal(1 + SyntheticSite.CATEGORY_COUNT + 50 + 3, stats.HtmlPageCount);
        Assert.True(stats.SkipReasons.GetValueOrDefault(SkipReason.DepthLimit) > 0);
    }

    [Fact]
    public async Task RespectsMaxUrls()
    {
        var path = NewDatabasePath();
        var summary = await CrawlAsync(Options() with { MaxUrls = 40 }, path);

        Assert.Equal(CrawlStatus.Completed, summary.Status);
        Assert.Equal(40, Count(path, $"SELECT COUNT(*) FROM urls WHERE is_internal = 1 AND state <> 3 AND {NOT_A_PROBE}"));

        // What a UI's estimate goes by: the site's URLs the crawl meant to fetch, which the limit made 40, and fetched.
        Assert.Equal((40, 40), (summary.Progress.InternalFound, summary.Progress.InternalFetched));
        ProgressAgreement.AssertMatchesTheFile(summary.Progress, path);
        Assert.True(CrawlStatsReader.Read(path).SkipReasons.GetValueOrDefault(SkipReason.UrlLimit) > 0);
        AssertTheVersionWasProbedOutsideTheLimit(path);
    }

    /// <summary>The probe of the site's https:// version doesn't count towards the URL limit, and is fetched all the same.</summary>
    private static void AssertTheVersionWasProbedOutsideTheLimit(string path) =>
        Assert.Equal(1, Count(path, $"SELECT COUNT(*) FROM urls WHERE source = {(int)DiscoverySource.SiteVersion} AND state = {(int)UrlState.Done}"));

    [Theory]
    [InlineData(1, 0, "depth limit")]
    [InlineData(0, 300, "URL limit")]
    public async Task LimitedCrawlsSkipTheOrphanCheckAndSayWhy(int maxDepth, int maxUrls, string reason)
    {
        var path = NewDatabasePath();
        await CrawlAsync(Options() with { MaxDepth = maxDepth, MaxUrls = maxUrls }, path);

        var analysis = await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);

        // /plants/trailing/ is in the sitemap and only linked through a redirect the limit kept out of the crawl.
        Assert.True(analysis.IsPartial);
        Assert.DoesNotContain(analysis.Issues, issue => issue.Issue == IssueCatalog.ORPHAN_PAGE);
        Assert.False(IssueReader.ReadCounts(path).ContainsKey(IssueCatalog.ORPHAN_PAGE.Id));
        var note = Assert.Single(analysis.Notes);
        Assert.Contains(reason, note);
        Assert.Equal([note], IssueReader.ReadNotes(path));

        // Sitemap coverage doesn't depend on which pages link where, so it still runs.
        Assert.Contains(analysis.Issues, issue => issue.Issue == IssueCatalog.NOT_IN_SITEMAP);
    }

    [Fact]
    public async Task AFolderScopedCrawlSkipsTheOrphanCheck()
    {
        var path = NewDatabasePath();
        await CrawlAsync(Options() with { StartUrl = new Uri(_server.BaseUri, SyntheticSite.PLANTS_PATH).AbsoluteUri }, path);

        var analysis = await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);

        Assert.True(analysis.IsPartial);
        Assert.DoesNotContain(analysis.Issues, issue => issue.Issue == IssueCatalog.ORPHAN_PAGE);
        Assert.Contains(SyntheticSite.PLANTS_PATH, Assert.Single(analysis.Notes));
    }

    [Fact]
    public async Task OptionsChangeWhatIsFollowed()
    {
        var expected = _server.Site.GetExpectations();
        var permissive = NewDatabasePath();
        await CrawlAsync(Options() with { RespectRobotsTxt = false, FollowNofollow = true }, permissive);

        var stats = CrawlStatsReader.Read(permissive);
        Assert.Equal(expected.HtmlPages + expected.RobotsDisallowed + expected.NofollowOnly - expected.NofollowOnlyFiles, stats.HtmlPageCount);
        Assert.False(stats.Outcomes.ContainsKey(FetchOutcome.RobotsDisallowed));
    }

    /// <summary>Stops early (while sitemaps are still being read) and part-way, then checks the resumed crawl exactly.</summary>
    [Theory]
    [InlineData(5)]
    [InlineData(150)]
    public async Task StoppedCrawlResumesToTheSameResult(int stopAfter)
    {
        var path = NewDatabasePath();
        await StopAfterAsync(Options() with { MaxRequestsPerSecond = 200 }, path, stopAfter);

        var resumedOptions = CrawlDatabase.ReadOptions(path) with { MaxRequestsPerSecond = 0 };
        var resumed = await CrawlAsync(resumedOptions, path, resume: true);
        Assert.Equal(CrawlStatus.Completed, resumed.Status);
        AssertDownloadedIsWhatWasRead(resumed, path);
        ProgressAgreement.AssertMatchesTheFile(resumed.Progress, path);
        await AssertMatchesThePlantsAsync(_server, path);

        // The same URLs, in the same states, and the same links as a crawl that was never interrupted.
        var uninterrupted = NewDatabasePath();
        await CrawlAsync(Options(), uninterrupted);
        const string URL_STATES = "SELECT url || ' ' || is_internal || ' ' || state || ' ' || skip_reason || ' ' || fetch_mode FROM urls ORDER BY url";
        Assert.Equal(Strings(uninterrupted, URL_STATES), Strings(path, URL_STATES));
        Assert.Equal(Count(uninterrupted, "SELECT COUNT(*) FROM links"), Count(path, "SELECT COUNT(*) FROM links"));
    }

    [Fact]
    public async Task ResumedCrawlKeepsItsUrlLimit()
    {
        var path = NewDatabasePath();
        await StopAfterAsync(Options() with { MaxUrls = 120, MaxRequestsPerSecond = 200 }, path, 40);

        var resumed = await CrawlAsync(CrawlDatabase.ReadOptions(path) with { MaxRequestsPerSecond = 0 }, path, resume: true);

        Assert.Equal(120, Count(path, $"SELECT COUNT(*) FROM urls WHERE is_internal = 1 AND state <> 3 AND {NOT_A_PROBE}"));
        Assert.Equal((120, 120), (resumed.Progress.InternalFound, resumed.Progress.InternalFetched));
        ProgressAgreement.AssertMatchesTheFile(resumed.Progress, path);
        Assert.True(CrawlStatsReader.Read(path).SkipReasons.GetValueOrDefault(SkipReason.UrlLimit) > 0);
        AssertTheVersionWasProbedOutsideTheLimit(path);
    }

    [Fact]
    public async Task ReanalysisReplacesIssuesAndRunningCrawlsAreRefused()
    {
        var path = NewDatabasePath();
        await CrawlAsync(Options() with { MaxUrls = 60 }, path);
        var analyzer = new CrawlAnalyzer(NullLoggerFactory.Instance);

        // Analysing again gives the same rows, replacing the old ones rather than adding to them.
        var first = await analyzer.AnalyzeAsync(path);
        var firstRows = IssueReader.ReadAll(path).ToList();
        var second = await analyzer.AnalyzeAsync(path);
        Assert.NotEmpty(firstRows);
        Assert.Equal(firstRows, IssueReader.ReadAll(path).ToList());
        Assert.Equal(second.Issues.Sum(issue => issue.Count), Count(path, "SELECT COUNT(*) FROM issues"));
        Assert.Equal(first.Issues.Select(issue => issue.Count), second.Issues.Select(issue => issue.Count));
        Assert.Equal(second.Issues.Count, Count(path, "SELECT COUNT(*) FROM issue_types"));

        // Stricter thresholds flag more, from the same stored crawl, and the defaults again flag what they did at first:
        // the stricter run's extra rows are gone, not kept alongside.
        var strict = await analyzer.AnalyzeAsync(path, new AnalysisSettings { TitleMinLength = 10, TitleMaxLength = 20 });
        Assert.True(strict.Issues.Single(issue => issue.Issue == IssueCatalog.TITLE_TOO_LONG).Count >
                    second.Issues.Single(issue => issue.Issue == IssueCatalog.TITLE_TOO_LONG).Count);
        await analyzer.AnalyzeAsync(path);
        Assert.Equal(firstRows, IssueReader.ReadAll(path).ToList());

        using (var connection = CrawlDatabase.OpenWriter(path))
        {
            CrawlDatabase.Execute(connection, $"UPDATE crawl SET status = {(int)CrawlStatus.Running}");
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => analyzer.AnalyzeAsync(path));
        Assert.Equal(firstRows, IssueReader.ReadAll(path).ToList());
    }

    /// <summary>
    /// A snapshot read of the analysis sees one analysis whole, even if another finishes between its parts: the analysis
    /// row, its counts and its notes come from one consistent view. Forced by changing a count from another connection
    /// just after the read has begun; the read must still return the counts as they were when it started.
    /// </summary>
    [Fact]
    public async Task AnAnalysisIsReadAsOneConsistentSnapshot()
    {
        var path = NewDatabasePath();
        await CrawlAsync(Options() with { MaxUrls = 60 }, path);
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);

        var before = IssueReader.ReadCounts(path);
        var bumped = before.First(count => count.Value > 0).Key;
        var skippedBefore = IssueReader.ReadSkippedChecks(path);
        var stampBefore = CrawlSummaryReader.Read(path).UpdatedAt;
        Assert.NotEmpty(skippedBefore);

        // Keep the file in WAL mode so a writer and the snapshot read can run at once (the analysis folds it back to a
        // rollback journal when it closes, and there a reader and writer can't coexist).
        using var keepWal = CrawlDatabase.OpenWriter(path);

        var acted = 0;
        IssueReader.SnapshotTaken = _ =>
        {
            // Once the read has pinned its snapshot (it has read the analysis row), another connection changes a count.
            if (Interlocked.Exchange(ref acted, 1) == 1)
            {
                return;
            }

            // And the crawl is stamped, as a resume stamps it, and the skip reasons go, as another analysis would replace them.
            using var writer = CrawlDatabase.OpenWriter(path);
            CrawlDatabase.Execute(writer, $"""
                UPDATE issue_types SET url_count = url_count + 1000 WHERE id = '{bumped}';
                UPDATE crawl SET updated_at = updated_at + 1;
                DELETE FROM skipped_checks;
                """);
        };

        try
        {
            var results = IssueReader.ReadResults(path)!;

            // The snapshot read returns the counts, the skip reasons and the crawl's stamp as they were when it began, not
            // the changes made part-way through, so whether the analysis stands is judged on the same snapshot.
            Assert.Equal(before[bumped], results.Counts[bumped]);
            Assert.Equal(skippedBefore, results.Skipped);
            Assert.Equal(stampBefore, results.CrawlUpdatedAt);
            Assert.Empty(results.OutOfDate(new AnalysisSettings()));
        }
        finally
        {
            IssueReader.SnapshotTaken = null;
        }

        // The changes did land, so a fresh read sees them: the snapshot isolated the read, it didn't lose the writes.
        Assert.Equal(before[bumped] + 1000, IssueReader.ReadCounts(path)[bumped]);
        Assert.NotEqual(skippedBefore, IssueReader.ReadSkippedChecks(path));
        Assert.Equal([OutOfDateReason.CrawlChanged], IssueReader.ReadResults(path)!.OutOfDate(new AnalysisSettings()));
    }

    /// <summary>
    /// Cancels a re-analysis with stricter settings at a range of moments. Whenever it's cancelled, the file must still
    /// hold the complete previous results: never a mix, never nothing.
    /// </summary>
    [Fact]
    public async Task CancelledAnalysisKeepsThePreviousResults()
    {
        var path = NewDatabasePath();
        await CrawlAsync(Options(), path);
        var analyzer = new CrawlAnalyzer(NullLoggerFactory.Instance);
        var strict = new AnalysisSettings { TitleMinLength = 10, TitleMaxLength = 20 };
        await analyzer.AnalyzeAsync(path);
        var previous = IssueReader.ReadAll(path).ToList();
        Assert.NotEqual(previous.Count, (await analyzer.AnalyzeAsync(path, strict)).Issues.Sum(issue => issue.Count));
        await analyzer.AnalyzeAsync(path);

        var cancellations = 0;
        foreach (var delayMs in new[] { 0, 1, 2, 5, 10, 20, 40, 80, 160 })
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(delayMs));
            try
            {
                await analyzer.AnalyzeAsync(path, strict, cancel.Token);
                await analyzer.AnalyzeAsync(path);
            }
            catch (OperationCanceledException)
            {
                cancellations++;
                Assert.Equal(previous, IssueReader.ReadAll(path).ToList());
                Assert.Equal(previous.Count, IssueReader.ReadCounts(path).Values.Sum());
            }
        }

        Assert.True(cancellations > 0);
    }

    /// <summary>
    /// A cancel part-way, at an exact point. One link of a page is damaged so a rule fails on it, and the analysis is
    /// cancelled as the failure is logged. On the first page read, the cancel lands in the page pass; on the last, in
    /// the cross-page pass. Either way the analysis stops, and none of it is stored.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnAnalysisCancelledPartWayStoresNothing(bool onTheFirstPage)
    {
        var path = NewDatabasePath();
        await CrawlAsync(Options() with { MaxUrls = 60 }, path);
        int pageId;
        using (var reader = AnalysisReader.Open(path))
        {
            var pages = reader.ReadPages(reader.ReadUrls()).Select(page => page.UrlId).ToList();
            pageId = onTheFirstPage ? pages[0] : pages[^1];
        }

        Assert.True(Count(path, $"SELECT COUNT(*) FROM links WHERE from_id = {pageId} AND kind = {(int)LinkKind.Hyperlink}") > 0);
        using (var connection = CrawlDatabase.OpenWriter(path))
        {
            CrawlDatabase.Execute(connection,
                $"UPDATE links SET to_id = 2147483647 WHERE rowid = (SELECT MIN(rowid) FROM links WHERE from_id = {pageId} AND kind = {(int)LinkKind.Hyperlink})");
        }

        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        var previous = IssueReader.ReadAll(path).ToList();
        var previousNotes = IssueReader.ReadNotes(path);

        // Stricter titles, so a run that stored anything would show.
        using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CrawlAnalyzer(new CancelOnLoggedFailure(cancel))
            .AnalyzeAsync(path, new AnalysisSettings { TitleMinLength = 10, TitleMaxLength = 20 }, cancel.Token));

        Assert.True(cancel.IsCancellationRequested);
        Assert.Equal(previous, IssueReader.ReadAll(path).ToList());
        Assert.Equal(previousNotes, IssueReader.ReadNotes(path));
    }

    [Fact]
    public async Task ARuleThatFailsOnOnePageDoesNotStopTheAnalysis()
    {
        var path = NewDatabasePath();
        await CrawlAsync(Options() with { MaxUrls = 60 }, path);
        var analyzer = new CrawlAnalyzer(NullLoggerFactory.Instance);
        var clean = await analyzer.AnalyzeAsync(path);

        // A link to a URL id the file doesn't have: link checks on the home page can't look its target up.
        using (var connection = CrawlDatabase.OpenWriter(path))
        {
            CrawlDatabase.Execute(connection, $"""
                UPDATE links SET to_id = 2147483647 WHERE rowid = (
                    SELECT MIN(l.rowid) FROM links l JOIN urls u ON u.id = l.from_id
                    WHERE u.url = '{_server.BaseUri.AbsoluteUri}' AND l.kind = {(int)LinkKind.Hyperlink} AND (l.flags & {(int)LinkFlags.SamePage}) = 0)
                """);
        }

        var damaged = await analyzer.AnalyzeAsync(path);

        Assert.Equal(clean.PagesAnalyzed, damaged.PagesAnalyzed);
        Assert.Contains(damaged.Notes, note => note.StartsWith("LinkRules failed on 1 page", StringComparison.Ordinal));
        Assert.Contains(damaged.Notes, note => note.StartsWith("InlinkRules failed on 1 page", StringComparison.Ordinal));
        Assert.DoesNotContain(clean.Notes, note => note.Contains("failed", StringComparison.Ordinal));
        Assert.Equal(clean.Issues.Single(issue => issue.Issue == IssueCatalog.TITLE_TOO_LONG).Count,
            damaged.Issues.Single(issue => issue.Issue == IssueCatalog.TITLE_TOO_LONG).Count);
    }

    [Fact]
    public async Task RefusesToOverwriteAnExistingCrawl()
    {
        var path = NewDatabasePath();
        await CrawlAsync(Options() with { MaxUrls = 5 }, path);

        var engine = new CrawlEngine(NullLoggerFactory.Instance);
        Assert.Throws<InvalidOperationException>(() => engine.Start(new CrawlRequest(Options(), path)));
    }

    [Fact]
    public void RejectsInvalidOptions()
    {
        var engine = new CrawlEngine(NullLoggerFactory.Instance);

        Assert.Throws<ArgumentException>(() => engine.Start(new CrawlRequest(new CrawlOptions { StartUrl = "ftp://example.com" }, NewDatabasePath())));
    }

    private CrawlOptions Options() => Options(_server);

    private static CrawlOptions Options(SyntheticSiteServer server) => new() { StartUrl = server.BaseUri.AbsoluteUri, Concurrency = 8 };

    private static async Task<CrawlSummary> CrawlAsync(CrawlOptions options, string path, bool resume = false)
    {
        await using var job = new CrawlEngine(NullLoggerFactory.Instance).Start(new CrawlRequest(options, path, resume));
        return await job.Completion.WaitAsync(CRAWL_TIMEOUT);
    }

    private string NewDatabasePath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"si-test-{Guid.NewGuid():N}.sicrawl");
        _databases.Add(path);
        return path;
    }

    private static long Count(string path, string sql)
    {
        using var connection = CrawlDatabase.OpenReader(path);
        return Convert.ToInt64(CrawlDatabase.Scalar(connection, sql), CultureInfo.InvariantCulture);
    }

    private static long Depth(string path, string url) => Count(path, $"SELECT depth FROM urls WHERE url = '{url}'");

    /// <summary>
    /// Downloaded means the bodies the crawler read. The images it only checked don't count, whatever size their
    /// Content-Length gives, like the planted 300 KB one.
    /// </summary>
    private static void AssertDownloadedIsWhatWasRead(CrawlSummary summary, string path)
    {
        Assert.Equal(Count(path, "SELECT COALESCE(SUM(size_bytes), 0) FROM responses WHERE body_bytes IS NOT NULL"), summary.Progress.BytesDownloaded);
        Assert.Equal(1, Count(path, "SELECT COUNT(*) FROM responses r JOIN urls u ON u.id = r.url_id WHERE u.url LIKE '%/img/large.jpg' AND r.body_bytes IS NULL"));
    }

    private static List<string> Strings(string path, string sql)
    {
        using var connection = CrawlDatabase.OpenReader(path);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        List<string> values = [];
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    /// <summary>Starts a crawl and stops it once it has fetched the given number of URLs.</summary>
    private static async Task StopAfterAsync(CrawlOptions options, string path, int fetched)
    {
        await using var job = new CrawlEngine(NullLoggerFactory.Instance).Start(new CrawlRequest(options, path));
        var deadline = DateTime.UtcNow + CRAWL_TIMEOUT;
        while (job.Progress.Fetched < fetched && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5);
        }

        Assert.Equal(CrawlStatus.Stopped, (await job.StopAsync()).Status);
    }
}
