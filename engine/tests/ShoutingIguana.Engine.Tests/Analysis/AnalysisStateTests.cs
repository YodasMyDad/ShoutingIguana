using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Serialization;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>
/// What an analysis stores about itself: whether it's partial, why each skipped check was skipped, and what it ran
/// against, so readers can tell when it no longer stands. Files analysed by earlier engines have none of it, and readers
/// cope: those analyses read as they always did, and as out of date.
/// </summary>
public sealed class AnalysisStateTests : IAsyncLifetime
{
    private readonly CrawlFiles _files = new();
    private SyntheticSiteServer _server = null!;

    public async Task InitializeAsync() => _server = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 60 });

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        _files.Dispose();
    }

    /// <summary>Each check gets the first reason that applies; the notes still give every reason, once each.</summary>
    [Fact]
    public async Task APartialAnalysisStoresWhyEachCheckWasSkipped()
    {
        var path = await CrawlAsync(new CrawlOptions
        {
            StartUrl = _server.BaseUri.AbsoluteUri, Concurrency = 8, MaxDepth = 1, CheckExternalLinks = false, CheckResources = false, DiscoverSitemaps = false
        });

        var analysis = await AnalyseAsync(path);

        var keptOut = TestCrawls.Count(path, "SELECT COUNT(*) FROM urls WHERE is_internal = 1 AND state = 3 AND skip_reason = 5");
        Assert.True(keptOut > 1);
        Dictionary<string, (string Reason, string Text)> why = new(StringComparer.Ordinal)
        {
            [IssueCatalog.BROKEN_EXTERNAL_LINKS.Id] = (SkippedCheckReasons.EXTERNAL_LINKS_OFF, "Links to other sites weren't checked in this crawl."),
            [IssueCatalog.ORPHAN_PAGE.Id] = (SkippedCheckReasons.DEPTH_LIMIT,
                string.Create(CultureInfo.InvariantCulture, $"The depth limit kept {keptOut:N0} internal URLs out of the crawl, and pages linked only from them would look orphaned.")),
            [IssueCatalog.CANONICAL_UNLINKED.Id] = (SkippedCheckReasons.DEPTH_LIMIT,
                string.Create(CultureInfo.InvariantCulture, $"The depth limit kept {keptOut:N0} internal URLs out of the crawl, and pages linked only from them would look orphaned.")),
            [IssueCatalog.NON_INDEXABLE_INLINKS_ONLY.Id] = (SkippedCheckReasons.DEPTH_LIMIT,
                string.Create(CultureInfo.InvariantCulture, $"The depth limit kept {keptOut:N0} internal URLs out of the crawl, and pages linked only from them would look orphaned.")),
            [IssueCatalog.BROKEN_RESOURCES.Id] = (SkippedCheckReasons.RESOURCES_OFF, "Scripts, stylesheets and other files weren't fetched in this crawl."),
            [IssueCatalog.SLOW_EXTERNAL_LINKS.Id] = (SkippedCheckReasons.EXTERNAL_LINKS_OFF, "Links to other sites weren't checked in this crawl."),
            [IssueCatalog.IMAGE_BROKEN.Id] = (SkippedCheckReasons.RESOURCES_OFF, "Images weren't fetched in this crawl."),
            [IssueCatalog.IMAGE_TOO_LARGE.Id] = (SkippedCheckReasons.RESOURCES_OFF, "Images weren't fetched in this crawl.")
        };
        foreach (var check in IssueCatalog.All.Where(check => check.Category == IssueCategory.Sitemaps))
        {
            why[check.Id] = (SkippedCheckReasons.SITEMAPS_OFF, "Sitemaps weren't read in this crawl.");
        }

        List<SkippedCheck> expected = [.. IssueCatalog.All.Where(check => why.ContainsKey(check.Id)).Select(check => new SkippedCheck(check.Id, why[check.Id].Reason, why[check.Id].Text))];
        Assert.Equal(expected, analysis.Skipped);
        Assert.Equal(expected, IssueReader.ReadSkippedChecks(path));
        Assert.True(analysis.IsPartial);
        Assert.True(IssueReader.ReadAnalysis(path)!.IsPartial);
        Assert.Equal(4, analysis.Notes.Count);
        Assert.Equal(analysis.Notes, IssueReader.ReadNotes(path));

        // Skipped is not run: none of these has a count.
        Assert.Empty(IssueReader.ReadCounts(path).Keys.Intersect(why.Keys));
    }

    /// <summary>An engine from before the URL summary left its analysis without the columns and tables added since.</summary>
    [Fact]
    public async Task AFileAnalysedByAnEarlierEngineHasNoSummaryAndNeedsAnalysingAgain()
    {
        var path = await CrawlAsync(new CrawlOptions { StartUrl = _server.BaseUri.AbsoluteUri, MaxUrls = 30 });
        await AnalyseAsync(path);
        var counts = IssueReader.ReadCounts(path);
        Change(path, """
            ALTER TABLE analysis DROP COLUMN is_partial;
            ALTER TABLE analysis DROP COLUMN crawl_updated_at;
            ALTER TABLE analysis DROP COLUMN analysis_version;
            ALTER TABLE analysis DROP COLUMN checks_fingerprint;
            ALTER TABLE analysis DROP COLUMN crawled_urls;
            ALTER TABLE analysis DROP COLUMN score_version;
            ALTER TABLE analysis DROP COLUMN score;
            ALTER TABLE analysis DROP COLUMN score_partial;
            ALTER TABLE analysis DROP COLUMN score_json;
            DROP TABLE url_summary;
            DROP TABLE skipped_checks;
            """);

        // It reads as it always did, with nothing to say about what it doesn't have.
        var earlier = IssueReader.ReadAnalysis(path)!;
        Assert.Equal(((bool?)null, (int?)null, (string?)null, (DateTimeOffset?)null, false),
            (earlier.IsPartial, earlier.AnalysisVersion, earlier.ChecksFingerprint, earlier.CrawlUpdatedAt, earlier.HasUrlSummary));
        Assert.Equal(new AnalysisSettings(), earlier.Settings);
        Assert.Equal(((long?)null, (int?)null, (SiteScoreSummary?)null, (NoScoreReason?)NoScoreReason.NotScored),
            (earlier.CrawledUrlCount, earlier.ScoreVersion, earlier.Score, earlier.NoScoreReason));
        Assert.Equal(counts, IssueReader.ReadCounts(path));
        Assert.Empty(IssueReader.ReadSkippedChecks(path));
        Assert.False(UrlSummaryReader.IsAvailable(path));
        Assert.Throws<InvalidOperationException>(() => UrlSummaryReader.ReadAll(path).ToList());

        var summary = CrawlSummaryReader.Read(path);
        Assert.Equal(earlier.AnalyzedAt, summary.AnalyzedAt);
        Assert.Equal([OutOfDateReason.EngineChanged], summary.OutOfDate(new AnalysisSettings()));

        // It has no score, and says why; what it found still has its order to fix.
        var results = IssueReader.ReadResults(path)!;
        Assert.Equal(((SiteScore?)null, (NoScoreReason?)NoScoreReason.NotScored), (results.Score, results.NoScoreReason));
        Assert.Equal(FixFirstOrder.Of(counts), results.FixFirst);
        Assert.NotEmpty(results.FixFirst);

        // Analysed by this engine, it gets what it lacked, and stands.
        var analysis = await AnalyseAsync(path);
        Assert.NotNull(IssueReader.ReadResults(path)!.Score);
        Assert.True(UrlSummaryReader.IsAvailable(path));
        Assert.Empty(CrawlSummaryReader.Read(path).OutOfDate(new AnalysisSettings()));
        Assert.Equal(analysis.IsPartial, IssueReader.ReadAnalysis(path)!.IsPartial);
        Assert.Equal(
            [
                (IssueCatalog.ORPHAN_PAGE.Id, SkippedCheckReasons.URL_LIMIT), (IssueCatalog.NON_INDEXABLE_INLINKS_ONLY.Id, SkippedCheckReasons.URL_LIMIT),
                (IssueCatalog.CANONICAL_UNLINKED.Id, SkippedCheckReasons.URL_LIMIT)
            ],
            IssueReader.ReadSkippedChecks(path).Select(skip => (skip.CheckId, skip.Reason)));
    }

    /// <summary>
    /// An earlier engine analysing a crawl this one analysed replaces what it knows and leaves the rest: the summary and
    /// the skip reasons are then another analysis's, and aren't read.
    /// </summary>
    [Fact]
    public async Task AnEarlierEngineAnalysingAgainLeavesNothingOfThisOneToRead()
    {
        var path = await CrawlAsync(new CrawlOptions { StartUrl = _server.BaseUri.AbsoluteUri, MaxUrls = 30 });
        await AnalyseAsync(path);
        Assert.NotEmpty(IssueReader.ReadSkippedChecks(path));

        var settings = JsonSerializer.Serialize(new AnalysisSettings(), EngineJsonContext.Default.AnalysisSettings);
        Change(path, $"""
            DELETE FROM analysis;
            INSERT INTO analysis (id, analyzed_at, engine_version, settings_json, pages, duration_ms)
            VALUES (1, {DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}, '2.0.0-alpha.0', '{settings}', 9, 99);
            """);

        Assert.True(TestCrawls.Count(path, "SELECT COUNT(*) FROM url_summary") > 0);
        Assert.False(IssueReader.ReadAnalysis(path)!.HasUrlSummary);
        Assert.False(UrlSummaryReader.IsAvailable(path));
        Assert.Empty(IssueReader.ReadSkippedChecks(path));
        Assert.Equal([OutOfDateReason.EngineChanged], CrawlSummaryReader.Read(path).OutOfDate(new AnalysisSettings()));

        // A threshold the analysing engine didn't have takes its default, so it matches unless it's set otherwise now.
        Change(path, $"UPDATE analysis SET settings_json = '{settings.Replace("\"slowResponseMs\":1000,", string.Empty, StringComparison.Ordinal)}'");
        Assert.Equal([OutOfDateReason.EngineChanged], CrawlSummaryReader.Read(path).OutOfDate(new AnalysisSettings()));
        Assert.Equal([OutOfDateReason.EngineChanged, OutOfDateReason.SettingsChanged],
            CrawlSummaryReader.Read(path).OutOfDate(new AnalysisSettings { SlowResponseMs = 2_000 }));
    }

    [Fact]
    public async Task TheAnalysisIsOutOfDateWhenTheCrawlOrItsSettingsChange()
    {
        var path = await CrawlAsync(new CrawlOptions { StartUrl = _server.BaseUri.AbsoluteUri, MaxUrls = 20 });
        await AnalyseAsync(path);
        Assert.Empty(CrawlSummaryReader.Read(path).OutOfDate(new AnalysisSettings()));
        Assert.Equal(CrawlSummaryReader.Read(path).UpdatedAt, IssueReader.ReadAnalysis(path)!.CrawlUpdatedAt);

        // Other thresholds.
        var longer = new AnalysisSettings { TitleMaxPixels = 658 };
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path, longer);
        Assert.Equal([OutOfDateReason.SettingsChanged], CrawlSummaryReader.Read(path).OutOfDate(new AnalysisSettings()));
        Assert.Empty(CrawlSummaryReader.Read(path).OutOfDate(longer with { }));

        // The crawl ran again: even a continued crawl that finds nothing new stamps the crawl.
        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(CrawlDatabase.ReadOptions(path), path, resume: true)).Status);
        Assert.Equal([OutOfDateReason.CrawlChanged], CrawlSummaryReader.Read(path).OutOfDate(longer));
        Assert.Equal([OutOfDateReason.CrawlChanged, OutOfDateReason.SettingsChanged], CrawlSummaryReader.Read(path).OutOfDate(new AnalysisSettings()));

        await AnalyseAsync(path);
        Assert.Empty(CrawlSummaryReader.Read(path).OutOfDate(new AnalysisSettings()));
    }

    /// <summary>
    /// The crawl's stamp only moves forward, whatever the clock does. Here the analysis ran while the clock was a day
    /// ahead and has since been put right: a stamp taken from the clock would go back, and could meet the very stamp the
    /// analysis kept, which would make the changed crawl look analysed.
    /// </summary>
    [Fact]
    public async Task TheCrawlsStampOnlyMovesForwardWhateverTheClockDoes()
    {
        var path = await CrawlAsync(new CrawlOptions { StartUrl = _server.BaseUri.AbsoluteUri, MaxUrls = 20 });
        await AnalyseAsync(path);
        var ahead = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeMilliseconds();
        Change(path, $"UPDATE crawl SET updated_at = {ahead}; UPDATE analysis SET crawl_updated_at = {ahead};");
        Assert.Empty(CrawlSummaryReader.Read(path).OutOfDate(new AnalysisSettings()));

        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(CrawlDatabase.ReadOptions(path), path, resume: true)).Status);
        var stamped = CrawlSummaryReader.Read(path);
        Assert.True(stamped.UpdatedAt.ToUnixTimeMilliseconds() > ahead, $"stamped {stamped.UpdatedAt:O}, before {DateTimeOffset.FromUnixTimeMilliseconds(ahead):O}");
        Assert.Equal([OutOfDateReason.CrawlChanged], stamped.OutOfDate(new AnalysisSettings()));
    }

    /// <summary>A shared file can say a sitemap was left unread without saying why: the analysis says so in its own words.</summary>
    [Fact]
    public async Task ASitemapLeftUnreadWithoutAReasonDoesNotStopTheAnalysis()
    {
        var path = await CrawlAsync(new CrawlOptions { StartUrl = _server.BaseUri.AbsoluteUri });
        var sitemap = TestCrawls.Rows(path, "SELECT url FROM sitemaps WHERE error IS NULL AND status_code = 200 AND is_probe = 0 ORDER BY id").First();
        Assert.True(TestCrawls.Count(path, "SELECT COUNT(*) FROM sitemaps WHERE error IS NULL AND status_code = 200") > 1);
        Change(path, $"UPDATE sitemaps SET error = 'Not read: ' WHERE url = '{sitemap}'");

        var analysis = await AnalyseAsync(path);

        var skip = Assert.Single(analysis.Skipped);
        Assert.Equal(new SkippedCheck(IssueCatalog.NOT_IN_SITEMAP.Id, SkippedCheckReasons.SITEMAPS_NOT_READ,
            $"The crawler left it unread, so {sitemap} and any more like it weren't read."), skip);
        Assert.Contains(analysis.Notes, note => note.Contains($"the crawler left it unread, so {sitemap}", StringComparison.Ordinal));
    }

    /// <summary>
    /// A site rule that fails once every page is read leaves its checks without results: they're skipped, each saying
    /// why, rather than counted as run and clean, and nothing of theirs is kept. Every other check is as it was.
    /// </summary>
    [Fact]
    public async Task ASiteRuleThatFailsLeavesItsChecksSkippedSayingWhy()
    {
        var path = await CrawlAsync(new CrawlOptions { StartUrl = _server.BaseUri.AbsoluteUri });
        await AnalyseAsync(path);
        var counts = IssueReader.ReadCounts(path);
        string[] duplicates =
        [
            IssueCatalog.TITLE_DUPLICATE.Id, IssueCatalog.DESCRIPTION_DUPLICATE.Id, IssueCatalog.H1_DUPLICATE.Id, IssueCatalog.DUPLICATE_CONTENT.Id,
            IssueCatalog.NEAR_DUPLICATE_CONTENT.Id
        ];
        Assert.True(counts[IssueCatalog.TITLE_DUPLICATE.Id] > 0);
        Assert.Empty(IssueReader.ReadSkippedChecks(path));

        var failing = new CrawlAnalyzer(NullLoggerFactory.Instance)
        {
            Finishing = rule =>
            {
                if (rule is DuplicateRules)
                {
                    throw new InvalidOperationException("Forced to fail");
                }
            }
        };
        var analysis = await failing.AnalyzeAsync(path);

        List<SkippedCheck> expected =
        [
            .. IssueCatalog.All.Where(check => duplicates.Contains(check.Id))
                .Select(check => new SkippedCheck(check.Id, SkippedCheckReasons.RULE_FAILED, "The check failed during the analysis, so it has no results. The log says why."))
        ];
        Assert.Equal(expected, analysis.Skipped);
        Assert.Equal(expected, IssueReader.ReadSkippedChecks(path));
        Assert.True(analysis.IsPartial);
        Assert.Contains(analysis.Notes, note => note.StartsWith("DuplicateRules failed after reading every page", StringComparison.Ordinal));
        Assert.Equal(counts.Where(count => !duplicates.Contains(count.Key)).OrderBy(count => count.Key, StringComparer.Ordinal),
            IssueReader.ReadCounts(path).OrderBy(count => count.Key, StringComparer.Ordinal));
        Assert.DoesNotContain(IssueReader.ReadAll(path), row => duplicates.Contains(row.Issue));
    }

    /// <summary>
    /// A check an analysis neither ran nor skipped is one its engine didn't have: newer than that engine, so analysing
    /// again runs it. (A check can be added without a new analysis version.) Unless that engine is newer than this one:
    /// then it dropped the check, and nothing is known of it, so it isn't called newer than the analysis.
    /// </summary>
    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task ACheckTheAnalysisDidntKnowIsNewerThanItUnlessItsEngineIsNewer(int versionsAway, bool newerThanAnalysis)
    {
        var path = await CrawlAsync(new CrawlOptions { StartUrl = _server.BaseUri.AbsoluteUri });
        await AnalyseAsync(path);
        Assert.Empty(IssueReader.ReadSkippedChecks(path));
        Change(path, $"""
            DELETE FROM issue_types WHERE id = '{IssueCatalog.TITLE_TOO_LONG.Id}';
            UPDATE analysis SET analysis_version = {AnalysisFreshness.VERSION + versionsAway};
            """);

        SkippedCheck[] expected = newerThanAnalysis
            ? [new SkippedCheck(IssueCatalog.TITLE_TOO_LONG.Id, SkippedCheckReasons.NEWER_THAN_ANALYSIS, "This check is newer than the analysis. Analyse the crawl again to run it.")]
            : [];
        Assert.Equal(expected, IssueReader.ReadSkippedChecks(path));
    }

    /// <summary>
    /// A closed crawl is a single file in rollback-journal mode, where someone part-way through reading it keeps writers
    /// out. An analysis that meets such a reader says the file is busy within a couple of seconds, rather than retrying
    /// for 30, and changes nothing; once the reader has finished, it runs.
    /// </summary>
    [Fact]
    public async Task AnAnalysisMeetingAReaderOnAClosedCrawlSaysTheFileIsBusy()
    {
        var path = await CrawlAsync(new CrawlOptions { StartUrl = _server.BaseUri.AbsoluteUri, MaxUrls = 20 });
        var analyzer = new CrawlAnalyzer(NullLoggerFactory.Instance);
        await analyzer.AnalyzeAsync(path);
        var analysed = IssueReader.ReadAnalysis(path)!;
        Assert.Equal(["delete"], TestCrawls.Rows(path, "PRAGMA journal_mode"));

        using (var reading = IssueReader.ReadAll(path).GetEnumerator())
        {
            Assert.True(reading.MoveNext());
            var timer = Stopwatch.StartNew();
            var busy = await Assert.ThrowsAsync<CrawlFileInUseException>(() => analyzer.AnalyzeAsync(path, new AnalysisSettings { TitleMaxPixels = 658 }));
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10), $"The analysis waited {timer.Elapsed} for the reader");
            Assert.Contains("being read", busy.Message, StringComparison.Ordinal);
        }

        Assert.Equal(analysed, IssueReader.ReadAnalysis(path));
        await analyzer.AnalyzeAsync(path, new AnalysisSettings { TitleMaxPixels = 658 });
        Assert.Equal(658, IssueReader.ReadAnalysis(path)!.Settings!.TitleMaxPixels);
    }

    private async Task<string> CrawlAsync(CrawlOptions options)
    {
        var path = _files.NewPath();
        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(options, path)).Status);
        return path;
    }

    private static Task<AnalysisSummary> AnalyseAsync(string path) => new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);

    /// <summary>Changes the file as another engine would have left it.</summary>
    private static void Change(string path, string sql)
    {
        var connection = CrawlDatabase.OpenWriter(path, allowNew: false);
        try
        {
            CrawlDatabase.Execute(connection, sql);
        }
        finally
        {
            CrawlDatabase.CloseWriter(connection, NullLogger.Instance);
        }
    }
}
