using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>The cheap summary a list of crawls shows: the same numbers the full stats give, without counting links.</summary>
public sealed class CrawlSummaryReaderTests : IAsyncLifetime
{
    private readonly CrawlFiles _files = new();
    private SyntheticSiteServer _server = null!;

    public async Task InitializeAsync() => _server = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 60 });

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        _files.Dispose();
    }

    [Fact]
    public async Task MatchesTheStatsOfACompletedCrawl()
    {
        var path = _files.NewPath();
        var options = new CrawlOptions { StartUrl = _server.BaseUri.AbsoluteUri, Concurrency = 8 };
        var crawl = await TestCrawls.RunAsync(options, path);
        Assert.Equal(CrawlStatus.Completed, crawl.Status);

        var crawled = CrawlSummaryReader.Read(path);
        var stats = CrawlStatsReader.Read(path);

        Assert.Equal(options.StartUrl, crawled.StartUrl);
        Assert.Equal(CrawlStatus.Completed, crawled.Status);
        Assert.Equal(stats.StartedAt, crawled.StartedAt);
        Assert.Equal(stats.FinishedAt, crawled.FinishedAt);
        Assert.Equal(crawled.FinishedAt, crawled.UpdatedAt);
        Assert.Equal(EngineInfo.Version, crawled.EngineVersion);
        Assert.Equal(stats.HtmlPageCount, crawled.PageCount);

        // The URLs requested, as the crawl's progress counted them: not those it only found (a nofollow link).
        Assert.Equal(crawl.Progress.Fetched, crawled.FetchedUrlCount);
        Assert.Equal(stats.Outcomes.Values.Sum(), crawled.FetchedUrlCount);
        Assert.True(crawled.PageCount is > 60 && crawled.FetchedUrlCount > crawled.PageCount && crawled.FetchedUrlCount < stats.UrlCount,
            $"{crawled.FetchedUrlCount} of {stats.UrlCount} URLs fetched, {crawled.PageCount} pages");
        Assert.Null(crawled.AnalyzedAt);
        Assert.Null(crawled.FlaggedChecks);

        var analysis = await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        var analysed = CrawlSummaryReader.Read(path);

        Assert.Equal(IssueReader.ReadAnalysis(path)!.AnalyzedAt, analysed.AnalyzedAt);
        Assert.True(analysed.AnalyzedAt >= analysed.UpdatedAt);

        // One per check that found something, by severity: the rows of the issues screen, not the flagged URLs.
        var flagged = analysis.Issues.Where(issue => issue.Count > 0).GroupBy(issue => issue.Issue.Severity)
            .ToDictionary(group => group.Key, group => (long)group.Count());
        Assert.Equal(flagged, analysed.FlaggedChecks);
        Assert.True(flagged.Values.Sum() < analysis.Issues.Sum(issue => issue.Count));
    }

    [Fact]
    public async Task AFirstAnalysisThatStoredNothingLeavesTheCrawlUnanalysed()
    {
        var path = _files.NewPath();
        await TestCrawls.RunAsync(new CrawlOptions { StartUrl = _server.BaseUri.AbsoluteUri, MaxUrls = 10 }, path);

        // A cancelled or failed first analysis leaves its empty tables behind.
        using (var connection = CrawlDatabase.OpenWriter(path))
        {
            CrawlDatabase.Execute(connection, CrawlSchema.ANALYSIS);
        }

        var summary = CrawlSummaryReader.Read(path);
        Assert.Null(summary.AnalyzedAt);
        Assert.Null(summary.FlaggedChecks);
        Assert.Equal(CrawlStatsReader.Read(path).Outcomes.Values.Sum(), summary.FetchedUrlCount);
    }

    [Fact]
    public async Task RefusesAFileThatIsNotACrawl()
    {
        var text = _files.NewPath();
        await File.WriteAllTextAsync(text, "not a crawl");
        Assert.Equal(26, Assert.Throws<SqliteException>(() => CrawlSummaryReader.Read(text)).SqliteErrorCode);

        var database = _files.NewPath();
        using (var connection = new SqliteConnection($"Data Source={database};Pooling=False"))
        {
            connection.Open();
            CrawlDatabase.Execute(connection, "CREATE TABLE notes (text TEXT)");
        }

        Assert.Contains("not a crawl database", Assert.Throws<InvalidOperationException>(() => CrawlSummaryReader.Read(database)).Message);
        Assert.Throws<FileNotFoundException>(() => CrawlSummaryReader.Read(_files.NewPath()));
    }
}
