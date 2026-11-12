using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>
/// The score as an analysis stores it: worked out from that analysis's own counts, the pages it read and the URLs crawled
/// as it read them, and read back whole, so no reader ever sets a count from one moment against a total from another.
/// </summary>
public sealed class ScoreStorageTests : IAsyncLifetime
{
    private static readonly TimeSpan CRAWL_TIMEOUT = TimeSpan.FromMinutes(2);

    private readonly CrawlFiles _files = new();
    private SyntheticSiteServer _server = null!;

    public async Task InitializeAsync() => _server = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 200 });

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        _files.Dispose();
    }

    [Fact]
    public async Task TheAnalysisStoresTheScoreItWorkedOutFromItsOwnCounts()
    {
        var path = await CrawlAsync(Options());

        var analysis = await AnalyseAsync(path);

        // The site is served over plain HTTP, a fault of the whole site: 60 points gone before anything else.
        var score = analysis.Score!;
        Assert.True(score.Value <= 40, $"scored {score.Value}");
        Assert.Contains(score.Lines, line => line is { CheckId: "no-https", Unit: ScoreUnit.Site, Extent: ScoreExtent.Site, Points: 60 });
        Assert.Equal((ScoreCalculator.VERSION, false, true, (ScoreIncompleteReason?)null), (score.Version, score.Partial, score.Covers.Complete, score.Covers.Reason));

        // Read back whole, in one snapshot with the counts it was worked out from.
        var results = IssueReader.ReadResults(path)!;
        Assert.Equal(Json(score), Json(results.Score!));
        Assert.Null(results.NoScoreReason);

        // Its totals are the analysis's own: the pages it read, and the URLs crawled as the library counts them.
        var listed = CrawlSummaryReader.Read(path);
        var crawled = listed.CrawledUrlCount!.Value;
        Assert.Equal((analysis.PagesAnalyzed, crawled), (score.Covers.Pages, score.Covers.Urls));
        Assert.Equal((crawled, (long?)crawled), (analysis.CrawledUrlCount, results.Analysis.CrawledUrlCount));
        Assert.Equal((results.Counts.Count, results.Skipped.Count), (score.Covers.ChecksRun, score.Covers.ChecksSkipped));
        Assert.Equal(IssueCatalog.All.Count, score.Covers.ChecksRun + score.Covers.ChecksSkipped);
        Assert.Equal(results.Counts[IssueCatalog.BLOCKED_BY_ROBOTS.Id], score.Covers.BlockedByRobots);
        Assert.True(score.Covers.BlockedByRobots > 0);

        // A line for every check that found something, in the order to fix them, each with the count the analysis stored.
        Assert.Equal(results.FixFirst, score.Lines.Select(line => line.CheckId));
        Assert.Equal(results.Counts.Where(count => count.Value > 0).Select(count => count.Key).Order(StringComparer.Ordinal),
            score.Lines.Select(line => line.CheckId).Order(StringComparer.Ordinal));
        Assert.All(score.Lines, line =>
        {
            Assert.Equal(results.Counts[line.CheckId], line.Count);
            Assert.Equal(line.Unit == ScoreUnit.Pages ? analysis.PagesAnalyzed : crawled, line.Of);
        });

        // Working it out again from what was stored gives the score that was stored.
        List<IssueCount> ran = [.. IssueCatalog.All.Where(check => results.Counts.ContainsKey(check.Id)).Select(check => new IssueCount(check, results.Counts[check.Id]))];
        Assert.Equal(Json(score), Json(ScoreCalculator.Calculate(ran, results.Skipped.Count, analysis.PagesAnalyzed, crawled, null, score.Covers.BlockedByRobots)!));

        // The headline is on the analysis, where a list of crawls reads it, and the analysis stands.
        var headline = new SiteScoreSummary(score.Value, score.Band, score.Partial);
        Assert.Equal((headline, (int?)ScoreCalculator.VERSION), (results.Analysis.Score, results.Analysis.ScoreVersion));
        Assert.Equal(headline, listed.Analysis!.Score);
        Assert.Empty(listed.OutOfDate(new AnalysisSettings()));
    }

    /// <summary>
    /// A crawl stopped part-way gives a partial score, of what it read. When the crawl goes on, the analysis keeps the
    /// totals it was worked out from: the URLs crawled then, not now.
    /// </summary>
    [Fact]
    public async Task AStoppedCrawlsScoreIsPartialAndKeepsItsOwnTotalsWhenTheCrawlGoesOn()
    {
        var path = _files.NewPath();
        await StopAfterAsync(Options() with { MaxRequestsPerSecond = 200 }, path, fetched: 60);

        var analysis = await AnalyseAsync(path);
        var then = CrawlSummaryReader.Read(path);
        var crawledThen = then.CrawledUrlCount!.Value;
        var stored = IssueReader.ReadResults(path)!;
        Assert.Equal((true, false, (ScoreIncompleteReason?)ScoreIncompleteReason.Stopped), (analysis.Score!.Partial, analysis.Score.Covers.Complete, analysis.Score.Covers.Reason));
        Assert.True(stored.Analysis.Score!.Partial);
        Assert.Equal(crawledThen, stored.Analysis.CrawledUrlCount);
        Assert.Equal((analysis.PagesAnalyzed, crawledThen), (stored.Score!.Covers.Pages, stored.Score.Covers.Urls));

        // The crawl goes on to its end: more URLs and more pages than the analysis saw.
        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(CrawlDatabase.ReadOptions(path) with { MaxRequestsPerSecond = 0 }, path, resume: true)).Status);
        var now = CrawlSummaryReader.Read(path);
        var crawledNow = now.CrawledUrlCount!.Value;
        Assert.True(crawledNow > crawledThen && now.PageCount > then.PageCount, $"{crawledThen} URLs then, {crawledNow} now");

        // The analysis is out of date, and still says what it was worked out from: nothing in it is the crawl's as it is now.
        var after = IssueReader.ReadResults(path)!;
        Assert.Equal([OutOfDateReason.CrawlChanged], after.OutOfDate(new AnalysisSettings()));
        Assert.Equal(((long?)crawledThen, analysis.PagesAnalyzed), (after.Analysis.CrawledUrlCount, after.Analysis.PagesAnalyzed));
        Assert.Equal(crawledThen, now.Analysis!.CrawledUrlCount);
        Assert.Equal(Json(stored.Score), Json(after.Score!));
        Assert.Equal((analysis.PagesAnalyzed, crawledThen), (after.Score!.Covers.Pages, after.Score.Covers.Urls));
        Assert.All(after.Score.Lines, line => Assert.Equal(line.Unit == ScoreUnit.Pages ? analysis.PagesAnalyzed : crawledThen, line.Of));

        // Analysed again, the score is of the whole crawl.
        var whole = await AnalyseAsync(path);
        Assert.Equal((false, crawledNow, (long?)crawledNow), (whole.Score!.Partial, whole.Score.Covers.Urls, IssueReader.ReadAnalysis(path)!.CrawledUrlCount));
        Assert.True(whole.PagesAnalyzed > analysis.PagesAnalyzed);
    }

    /// <summary>A crawl that finished, but whose limit kept part of the site out, read part of the site: its score says so, and which limit.</summary>
    [Fact]
    public async Task ALimitThatKeptPartOfTheSiteOutMakesTheScorePartial()
    {
        var limited = await CrawlAsync(Options() with { MaxUrls = 40 });
        var byUrls = (await AnalyseAsync(limited)).Score!;
        Assert.Equal((true, (ScoreIncompleteReason?)ScoreIncompleteReason.UrlLimit), (byUrls.Partial, byUrls.Covers.Reason));
        Assert.True(IssueReader.ReadAnalysis(limited)!.Score!.Partial);

        var shallow = await CrawlAsync(Options() with { MaxDepth = 1, DiscoverSitemaps = false });
        var byDepth = (await AnalyseAsync(shallow)).Score!;
        Assert.Equal((true, (ScoreIncompleteReason?)ScoreIncompleteReason.DepthLimit), (byDepth.Partial, byDepth.Covers.Reason));

        // A crawl kept to its start folder read all it set out to: that is its scope.
        var folder = await CrawlAsync(Options() with { StartUrl = new Uri(_server.BaseUri, SyntheticSite.PLANTS_PATH).AbsoluteUri });
        var inFolder = (await AnalyseAsync(folder)).Score!;
        Assert.Equal((false, true), (inFolder.Partial, inFolder.Covers.Complete));
    }

    /// <summary>A crawl that read no page has no score, and says why. Its analysis stands: there is nothing to score again.</summary>
    [Fact]
    public async Task ACrawlThatReadNoPageHasNoScoreAndSaysWhy()
    {
        var path = await CrawlAsync(Options() with { StartUrl = new Uri(_server.BaseUri, "/broken/1").AbsoluteUri, DiscoverSitemaps = false });

        var analysis = await AnalyseAsync(path);

        Assert.Equal((0L, (SiteScore?)null), (analysis.PagesAnalyzed, analysis.Score));
        Assert.True(analysis.CrawledUrlCount > 0);
        var results = IssueReader.ReadResults(path)!;
        Assert.Equal(((SiteScore?)null, (NoScoreReason?)NoScoreReason.NoPages), (results.Score, results.NoScoreReason));
        Assert.Equal(((SiteScoreSummary?)null, (int?)ScoreCalculator.VERSION, (NoScoreReason?)NoScoreReason.NoPages, (long?)analysis.CrawledUrlCount),
            (results.Analysis.Score, results.Analysis.ScoreVersion, results.Analysis.NoScoreReason, results.Analysis.CrawledUrlCount));
        Assert.Null(CrawlSummaryReader.Read(path).Analysis!.Score);
        Assert.Empty(CrawlSummaryReader.Read(path).OutOfDate(new AnalysisSettings()));

        // What it found is still in order: the start URL answers 404.
        Assert.Equal([IssueCatalog.INTERNAL_CLIENT_ERROR.Id], results.FixFirst.Take(1));
    }

    /// <summary>A score worked out by another version of the formula is still read, with its number; the analysis is out of date.</summary>
    [Fact]
    public async Task AScoreByAnotherVersionOfTheFormulaMakesTheAnalysisOutOfDate()
    {
        var path = await CrawlAsync(Options() with { MaxUrls = 30 });
        var analysis = await AnalyseAsync(path);
        Assert.Empty(CrawlSummaryReader.Read(path).OutOfDate(new AnalysisSettings()));

        Change(path, $"UPDATE analysis SET score_version = {ScoreCalculator.VERSION + 1}");

        var listed = CrawlSummaryReader.Read(path);
        Assert.Equal([OutOfDateReason.EngineChanged], listed.OutOfDate(new AnalysisSettings()));
        Assert.Equal(((int?)(ScoreCalculator.VERSION + 1), analysis.Score!.Value), (listed.Analysis!.ScoreVersion, listed.Analysis.Score!.Value));
        Assert.Equal(analysis.Score.Value, IssueReader.ReadResults(path)!.Score!.Value);

        // Analysing again scores it by this engine's formula, and it stands.
        await AnalyseAsync(path);
        Assert.Empty(CrawlSummaryReader.Read(path).OutOfDate(new AnalysisSettings()));
    }

    /// <summary>
    /// Crawl files are shared, and a later engine may store what this one doesn't know. A score that can't be read is no
    /// score, with the reason every unscored analysis gives, never an error; the rest of the analysis reads as it did.
    /// </summary>
    [Theory]
    [InlineData("score = 5000")]
    [InlineData("score = -1")]
    [InlineData("score = NULL")]
    [InlineData("score_json = NULL")]
    [InlineData("score_json = 'not json'")]
    [InlineData("score_json = '[]'")]
    [InlineData("score_json = 'null'")]
    [InlineData("score_json = '{\"value\":85}'")]
    [InlineData("score_json = replace(score_json, '\"band\":\"', '\"band\":\"Better')")]
    [InlineData("score_json = replace(score_json, '\"lines\":[', '\"lines\":[null,')")]
    [InlineData("score_json = replace(score_json, '\"checkIds\":[', '\"checkIds\":[null,')")]
    [InlineData("score_json = 85")]
    [InlineData("score = 100 - score")]
    [InlineData("score_partial = 1 - score_partial")]
    public async Task AScoreThatCantBeReadIsNoScoreNeverAnError(string change)
    {
        var path = await CrawlAsync(Options() with { MaxUrls = 30 });
        await AnalyseAsync(path);
        var counts = IssueReader.ReadCounts(path);
        Assert.NotNull(IssueReader.ReadResults(path)!.Score);

        Change(path, $"UPDATE analysis SET {change}");

        var results = IssueReader.ReadResults(path)!;
        Assert.Equal(((SiteScore?)null, (NoScoreReason?)NoScoreReason.NotScored), (results.Score, results.NoScoreReason));
        Assert.Equal(counts, results.Counts);
        Assert.Equal(FixFirstOrder.Of(counts), results.FixFirst);
        _ = CrawlSummaryReader.Read(path);
    }

    /// <summary>A file whose analysis has the score's number but not the column for the rest of it reads as having no score.</summary>
    [Fact]
    public async Task AScoreWithoutItsBreakdownIsNoScore()
    {
        var path = await CrawlAsync(Options() with { MaxUrls = 30 });
        await AnalyseAsync(path);

        Change(path, "ALTER TABLE analysis DROP COLUMN score_json");

        var results = IssueReader.ReadResults(path)!;
        Assert.NotNull(results.Analysis.Score);
        Assert.Equal(((SiteScore?)null, (NoScoreReason?)NoScoreReason.NotScored), (results.Score, results.NoScoreReason));
    }

    private CrawlOptions Options() => new() { StartUrl = _server.BaseUri.AbsoluteUri, Concurrency = 8 };

    private async Task<string> CrawlAsync(CrawlOptions options)
    {
        var path = _files.NewPath();
        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(options, path)).Status);
        return path;
    }

    private static Task<AnalysisSummary> AnalyseAsync(string path) => new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);

    /// <summary>A score as text, to compare two of them whole: every number, every line and every id.</summary>
    private static string Json(SiteScore score) => JsonSerializer.Serialize(score);

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

    /// <summary>Changes the file as another engine, or another program, would have left it.</summary>
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
