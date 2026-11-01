using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
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
            DROP TABLE url_summary;
            DROP TABLE skipped_checks;
            """);

        // It reads as it always did, with nothing to say about what it doesn't have.
        var earlier = IssueReader.ReadAnalysis(path)!;
        Assert.Equal(((bool?)null, (int?)null, (string?)null, (DateTimeOffset?)null, false),
            (earlier.IsPartial, earlier.AnalysisVersion, earlier.ChecksFingerprint, earlier.CrawlUpdatedAt, earlier.HasUrlSummary));
        Assert.Equal(new AnalysisSettings(), earlier.Settings);
        Assert.Equal(counts, IssueReader.ReadCounts(path));
        Assert.Empty(IssueReader.ReadSkippedChecks(path));
        Assert.False(UrlSummaryReader.IsAvailable(path));
        Assert.Throws<InvalidOperationException>(() => UrlSummaryReader.ReadAll(path).ToList());

        var summary = CrawlSummaryReader.Read(path);
        Assert.Equal(earlier.AnalyzedAt, summary.AnalyzedAt);
        Assert.Equal([OutOfDateReason.EngineChanged], summary.OutOfDate(new AnalysisSettings()));

        // Analysed by this engine, it gets what it lacked, and stands.
        var analysis = await AnalyseAsync(path);
        Assert.True(UrlSummaryReader.IsAvailable(path));
        Assert.Empty(CrawlSummaryReader.Read(path).OutOfDate(new AnalysisSettings()));
        Assert.Equal(analysis.IsPartial, IssueReader.ReadAnalysis(path)!.IsPartial);
        Assert.Equal(SkippedCheckReasons.URL_LIMIT, Assert.Single(IssueReader.ReadSkippedChecks(path)).Reason);
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

        // A threshold the analysing engine didn't store can't be known to match: when in doubt, out of date.
        Change(path, $"UPDATE analysis SET settings_json = '{settings.Replace("\"slowResponseMs\":1000,", string.Empty, StringComparison.Ordinal)}'");
        Assert.Equal([OutOfDateReason.EngineChanged, OutOfDateReason.SettingsChanged], CrawlSummaryReader.Read(path).OutOfDate(new AnalysisSettings()));
    }

    [Fact]
    public async Task TheAnalysisIsOutOfDateWhenTheCrawlOrItsSettingsChange()
    {
        var path = await CrawlAsync(new CrawlOptions { StartUrl = _server.BaseUri.AbsoluteUri, MaxUrls = 20 });
        await AnalyseAsync(path);
        Assert.Empty(CrawlSummaryReader.Read(path).OutOfDate(new AnalysisSettings()));
        Assert.Equal(CrawlSummaryReader.Read(path).UpdatedAt, IssueReader.ReadAnalysis(path)!.CrawlUpdatedAt);

        // Other thresholds.
        var longer = new AnalysisSettings { TitleMaxLength = 70 };
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
