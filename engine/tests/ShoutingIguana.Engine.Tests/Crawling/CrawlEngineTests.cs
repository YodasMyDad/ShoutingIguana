using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>End-to-end crawls of a local synthetic site whose planted issues are known exactly.</summary>
public sealed class CrawlEngineTests : IAsyncLifetime
{
    private const int PAGES = 600;
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
    public async Task FindsEveryPlantedIssue()
    {
        var path = NewDatabasePath();
        var summary = await CrawlAsync(Options(), path);

        Assert.Equal(CrawlStatus.Completed, summary.Status);
        var stats = CrawlStatsReader.Read(path);
        var expected = _server.Site.GetExpectations();
        Assert.Equal(expected.HtmlPages, stats.HtmlPageCount);
        Assert.Equal(expected.Orphans, stats.UnlinkedPageCount);
        Assert.Equal(expected.Redirects, stats.Outcomes.GetValueOrDefault(FetchOutcome.Redirect));
        Assert.Equal(expected.RobotsDisallowed, stats.Outcomes.GetValueOrDefault(FetchOutcome.RobotsDisallowed));
        Assert.Equal(expected.BrokenUrls + expected.ExternalMissing, stats.StatusCodes.GetValueOrDefault(404));
        Assert.Equal(expected.ServerErrorUrls, stats.StatusCodes.GetValueOrDefault(500));
        Assert.Equal(expected.NofollowOnly, stats.SkipReasons.GetValueOrDefault(SkipReason.NoFollow));
        Assert.Equal(expected.Noindex, stats.NonIndexableReasons.GetValueOrDefault(IndexabilityReason.Noindex));
        Assert.Equal(expected.Canonicalised, stats.NonIndexableReasons.GetValueOrDefault(IndexabilityReason.Canonicalised));
        Assert.Equal(0, stats.QueuedCount);
        Assert.Equal(0, Count(path, "SELECT COUNT(*) FROM urls WHERE state = 1"));
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
        Assert.Equal(40, Count(path, "SELECT COUNT(*) FROM urls WHERE is_internal = 1 AND state <> 3"));
        Assert.True(CrawlStatsReader.Read(path).SkipReasons.GetValueOrDefault(SkipReason.UrlLimit) > 0);
    }

    [Fact]
    public async Task OptionsChangeWhatIsFollowed()
    {
        var expected = _server.Site.GetExpectations();
        var permissive = NewDatabasePath();
        await CrawlAsync(Options() with { RespectRobotsTxt = false, FollowNofollow = true }, permissive);

        var stats = CrawlStatsReader.Read(permissive);
        Assert.Equal(expected.HtmlPages + expected.RobotsDisallowed + expected.NofollowOnly, stats.HtmlPageCount);
        Assert.False(stats.Outcomes.ContainsKey(FetchOutcome.RobotsDisallowed));
    }

    [Fact]
    public async Task StoppedCrawlResumesToTheSameResult()
    {
        var path = NewDatabasePath();
        var engine = new CrawlEngine(NullLoggerFactory.Instance);

        await using (var job = engine.Start(new CrawlRequest(Options() with { MaxRequestsPerSecond = 200 }, path)))
        {
            var deadline = DateTime.UtcNow + CRAWL_TIMEOUT;
            while (job.Progress.Fetched < 150 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            Assert.Equal(CrawlStatus.Stopped, (await job.StopAsync()).Status);
        }

        var resumedOptions = CrawlDatabase.ReadOptions(path) with { MaxRequestsPerSecond = 0 };
        var summary = await CrawlAsync(resumedOptions, path, resume: true);

        Assert.Equal(CrawlStatus.Completed, summary.Status);
        Assert.Equal(_server.Site.GetExpectations().HtmlPages, CrawlStatsReader.Read(path).HtmlPageCount);
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

    private CrawlOptions Options() => new() { StartUrl = _server.BaseUri.AbsoluteUri, Concurrency = 8 };

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
}
