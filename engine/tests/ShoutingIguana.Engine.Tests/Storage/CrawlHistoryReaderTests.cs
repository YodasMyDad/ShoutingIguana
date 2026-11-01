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

    [Fact]
    public async Task ACrawlsHistoryRunsFromNothingAtItsStartToWhatItFetched()
    {
        var path = _files.NewPath();
        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(Options(), path)).Status);

        var history = CrawlHistoryReader.Read(path);

        var stats = CrawlStatsReader.Read(path);
        Assert.Equal(stats.StartedAt, history.StartedAt);
        Assert.Equal(new CrawlHistoryPoint(history.StartedAt, 0, 0, 0, 0, 0), history.Points[0]);
        AssertRises(history.Points);
        var last = history.Points[^1];
        Assert.Equal(stats.Outcomes.Values.Sum(), last.Fetched);
        Assert.Equal(stats.HtmlPageCount, last.HtmlPages);
        Assert.Equal(TestCrawls.Count(path, "SELECT COUNT(*) FROM responses r JOIN urls u ON u.id = r.url_id WHERE u.is_internal = 1 AND u.source <> 3"),
            last.InternalFetched);
        Assert.Equal(TestCrawls.Count(path, "SELECT COUNT(*) FROM responses WHERE outcome IN (2, 3, 6, 7, 8)"), last.Errors);
        Assert.Equal(stats.Outcomes.GetValueOrDefault(FetchOutcome.Blocked), last.Blocked);
        Assert.Equal(TestCrawls.Count(path, "SELECT MAX(fetched_at) FROM responses"), last.At.ToUnixTimeMilliseconds());
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

    private CrawlOptions Options() => new() { StartUrl = _site.BaseUri.AbsoluteUri };

    private static void AssertRises(IReadOnlyList<CrawlHistoryPoint> points) =>
        Assert.All(points.Zip(points.Skip(1)), pair =>
        {
            Assert.True(pair.Second.At >= pair.First.At);
            Assert.True(pair.Second.Fetched > pair.First.Fetched);
            Assert.True(pair.Second.HtmlPages >= pair.First.HtmlPages && pair.Second.InternalFetched >= pair.First.InternalFetched);
            Assert.True(pair.Second.Errors >= pair.First.Errors && pair.Second.Blocked >= pair.First.Blocked);
        });

    private static void Execute(string path, string sql)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
