using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>
/// A crawl's fetching over time, counted back from its file: from a zero at the start to what it holds now, rising,
/// within its bounds, and, for a crawl continued later, up to where the run being watched began.
/// </summary>
public sealed class CrawlHistoryReaderTests : IAsyncLifetime
{
    private readonly CrawlFiles _files = new();
    private SyntheticSiteServer _site = null!;

    public async Task InitializeAsync() => _site = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 60 });

    public async Task DisposeAsync()
    {
        await _site.DisposeAsync();
        _files.Dispose();
    }

    /// <summary>
    /// It ends where the crawl's last progress did, the site's own URLs found included, so a chart drawn from the file
    /// meets the headline. The synthetic site's sitemap lists pages no link leads to, and its pages links the crawl skips.
    /// </summary>
    [Fact]
    public async Task ACrawlsHistoryRunsFromNothingAtItsStartToWhatItFetched()
    {
        var path = _files.NewPath();
        var summary = await TestCrawls.RunAsync(Options(), path);
        Assert.Equal(CrawlStatus.Completed, summary.Status);

        var history = CrawlHistoryReader.Read(path);

        var stats = CrawlStatsReader.Read(path);
        Assert.Equal(stats.StartedAt, history.StartedAt);
        Assert.Equal(new CrawlHistoryPoint(history.StartedAt, 0, 0, 0, 0, 0, 0), history.Points[0]);
        AssertRises(history.Points);
        var last = history.Points[^1];
        Assert.Equal(stats.Outcomes.Values.Sum(), last.Fetched);
        Assert.Equal(stats.HtmlPageCount, last.HtmlPages);
        Assert.Equal(TestCrawls.Count(path, "SELECT COUNT(*) FROM responses r JOIN urls u ON u.id = r.url_id WHERE u.is_internal = 1 AND u.source <> 3"),
            last.InternalFetched);
        Assert.Equal(TestCrawls.Count(path, "SELECT COUNT(*) FROM responses WHERE outcome IN (2, 3, 6, 7, 8)"), last.Errors);
        Assert.Equal(stats.Outcomes.GetValueOrDefault(FetchOutcome.Blocked), last.Blocked);
        Assert.Equal(TestCrawls.Count(path, "SELECT MAX(fetched_at) FROM responses"), last.At.ToUnixTimeMilliseconds());
        Assert.Equal((summary.Progress.InternalFound, summary.Progress.InternalFetched), (last.InternalFound, last.InternalFetched));
        Assert.True(TestCrawls.Count(path, "SELECT COUNT(*) FROM urls WHERE is_internal = 1 AND state = 3") > 0, "The site has links the crawl skips.");
    }

    /// <summary>
    /// A URL is found as the page that first links it is fetched; the start URL and the URLs only a sitemap lists, which
    /// no page links, as the crawl starts. Here a page is fetched each second, in the order the crawl found them.
    /// </summary>
    [Fact]
    public async Task TheSitesOwnUrlsCountAsFoundWhenThePageLinkingThemIsFetched()
    {
        var path = _files.NewPath();
        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(Options(), path)).Status);
        var started = TestCrawls.Count(path, "SELECT started_at FROM crawl");
        Execute(path, $"UPDATE responses SET fetched_at = {started} + 1000 * url_id");

        var history = CrawlHistoryReader.Read(path);

        // Every URL was fetched, URL n in second n: by then, the URLs it linked first were found, and so was it (an adopted
        // probe can be fetched before the page that made it the site's own). What no page linked was found at the start.
        AssertRises(history.Points);
        const string SITE_URL = "u.is_internal = 1 AND u.source <> 3 AND u.state <> 3";
        Assert.All(history.Points.Skip(1), point =>
        {
            var second = point.At.ToUnixTimeMilliseconds() / 1000 - started / 1000;
            Assert.Equal(TestCrawls.Count(path, $"SELECT COUNT(*) FROM urls u WHERE {SITE_URL} AND (u.found_on_id IS NULL OR MIN(u.found_on_id, u.id) <= {second})"),
                point.InternalFound);
        });
        Assert.Equal(TestCrawls.Count(path, $"SELECT COUNT(*) FROM urls u WHERE {SITE_URL}"), history.Points[^1].InternalFound);
    }

    /// <summary>Ten thousand seconds of fetching, one a second, kept to the bound and spread evenly over them.</summary>
    [Fact]
    public async Task ALongCrawlsHistoryIsDownsampledToItsBound()
    {
        var path = _files.NewPath();
        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(Options(), path)).Status);
        var started = TestCrawls.Count(path, "SELECT started_at FROM crawl");
        var responses = TestCrawls.Count(path, "SELECT COUNT(*) FROM responses");

        // Each response a second apart, as a slow crawl would fetch them.
        Execute(path, $"UPDATE responses SET fetched_at = {started} + 1000 * url_id");
        var history = CrawlHistoryReader.Read(path, maxPoints: 20);

        Assert.InRange(history.Points.Count, 11, 22);
        AssertRises(history.Points);
        Assert.Equal(responses, history.Points[^1].Fetched);
        var gaps = history.Points.Skip(1).Zip(history.Points.Skip(2), (first, second) => second.Fetched - first.Fetched).SkipLast(1).Distinct().ToList();
        Assert.Single(gaps);
    }

    /// <summary>Stopped part-way and continued: what was fetched before the continued run began, and nothing after.</summary>
    [Fact]
    public async Task AContinuedCrawlsEarlierRunsAreCountedUpToTheRunBeingWatched()
    {
        var path = _files.NewPath();
        await using (var job = TestCrawls.Start(Options() with { MaxRequestsPerSecond = 100 }, path))
        {
            while (job.Progress.Fetched < 20)
            {
                await Task.Delay(5);
            }

            Assert.Equal(CrawlStatus.Stopped, (await job.StopAsync()).Status);
        }

        var firstRun = TestCrawls.Count(path, "SELECT COUNT(*) FROM responses");
        await Task.Delay(20);
        var resumedAt = DateTimeOffset.UtcNow;
        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(CrawlDatabase.ReadOptions(path), path, resume: true)).Status);

        // The first run's, but for any the second fetched again: each URL counts from its last fetch.
        var earlier = CrawlHistoryReader.Read(path, resumedAt);
        var last = earlier.Points[^1];
        Assert.Equal(TestCrawls.Count(path, $"SELECT COUNT(*) FROM responses WHERE fetched_at < {resumedAt.ToUnixTimeMilliseconds()}"), last.Fetched);
        Assert.InRange(last.Fetched, 1, firstRun);
        Assert.All(earlier.Points, point => Assert.True(point.At < resumedAt));
        AssertRises(earlier.Points);
        Assert.True(CrawlHistoryReader.Read(path).Points[^1].Fetched > last.Fetched);
    }

    private CrawlOptions Options() => new() { StartUrl = _site.BaseUri.AbsoluteUri, BrowserFallback = false };

    /// <summary>Each point adds something fetched or found, and the site's own URLs are found by the time they're fetched.</summary>
    private static void AssertRises(IReadOnlyList<CrawlHistoryPoint> points)
    {
        Assert.All(points, point => Assert.InRange(point.InternalFetched, 0, point.InternalFound));
        Assert.All(points.Zip(points.Skip(1)), pair =>
        {
            Assert.True(pair.Second.At >= pair.First.At);
            Assert.True(pair.Second.Fetched >= pair.First.Fetched && pair.Second.InternalFound >= pair.First.InternalFound);
            Assert.True(pair.Second.Fetched > pair.First.Fetched || pair.Second.InternalFound > pair.First.InternalFound);
            Assert.True(pair.Second.HtmlPages >= pair.First.HtmlPages && pair.Second.InternalFetched >= pair.First.InternalFetched);
            Assert.True(pair.Second.Errors >= pair.First.Errors && pair.Second.Blocked >= pair.First.Blocked);
        });
    }

    private static void Execute(string path, string sql)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
