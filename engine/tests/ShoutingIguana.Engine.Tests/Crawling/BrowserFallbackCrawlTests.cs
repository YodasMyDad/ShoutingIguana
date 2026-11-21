using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>
/// A host that answers plain HTTP with a Cloudflare challenge is read in a browser when the crawl asks for it, and
/// that HTML is parsed once. The next page on the host is not fetched by HTTP. A challenge the browser also gets
/// stays a block, and the host stays on HTTP. Off, the default, leaves the challenge as <c>internal-blocked</c>.
/// </summary>
public sealed class BrowserFallbackCrawlTests : IAsyncLifetime
{
    private readonly List<string> _databases = [];
    private SyntheticSiteServer _server = null!;

    public async Task InitializeAsync() =>
        _server = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 2, Sitemaps = false });

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        foreach (var path in _databases.SelectMany(path => new[] { path, path + "-wal", path + "-shm" }))
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ABlockedHostIsReadInTheBrowserAndTheNextPageSkipsHttp()
    {
        var path = NewDatabase();
        var start = new Uri(_server.BaseUri, SyntheticSite.BROWSER_FALLBACK_PATH).AbsoluteUri;
        var next = new Uri(_server.BaseUri, SyntheticSite.BROWSER_FALLBACK_NEXT_PATH).AbsoluteUri;
        var hits = _server.Requests.WatchHits();

        var summary = await RunAsync(path, start, browserFallback: true);
        Assert.Equal(CrawlStatus.Completed, summary.Status);

        Assert.Equal("200", Scalar(path, start, "r.status_code"));
        Assert.Equal(((int)FetchOutcome.Success).ToString(), Scalar(path, start, "r.outcome"));
        Assert.Equal("", Scalar(path, start, "COALESCE(r.title, '')"));
        Assert.Equal("200", Scalar(path, next, "r.status_code"));

        var page = Hits(hits, SyntheticSite.BROWSER_FALLBACK_PATH);
        Assert.Contains(page, hit => !hit.FromBrowser);
        Assert.Contains(page, hit => hit.FromBrowser);
        Assert.DoesNotContain(Hits(hits, SyntheticSite.BROWSER_FALLBACK_NEXT_PATH), hit => !hit.FromBrowser);
        Assert.Contains(Hits(hits, SyntheticSite.BROWSER_FALLBACK_NEXT_PATH), hit => hit.FromBrowser);

        Assert.Equal("browser-fallback", Assert.Single(TestCrawls.Rows(path, "SELECT code FROM notices WHERE code = 'browser-fallback'")));

        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        Assert.Equal(1, IssueReader.ReadCounts(path)[IssueCatalog.TITLE_MISSING.Id]);
        Assert.EndsWith(SyntheticSite.BROWSER_FALLBACK_PATH, Assert.Single(IssueReader.ReadIssues(path, IssueCatalog.TITLE_MISSING.Id, 10)).Url);
        Assert.Equal(0, IssueReader.ReadCounts(path).GetValueOrDefault(IssueCatalog.INTERNAL_BLOCKED.Id));
    }

    [Fact]
    public async Task OffLeavesTheChallengeBlocked()
    {
        var path = NewDatabase();
        var start = new Uri(_server.BaseUri, SyntheticSite.BROWSER_FALLBACK_PATH).AbsoluteUri;
        var hits = _server.Requests.WatchHits();

        var summary = await RunAsync(path, start, browserFallback: false);
        Assert.Equal(CrawlStatus.Completed, summary.Status);

        Assert.Equal(((int)FetchOutcome.Blocked).ToString(), Scalar(path, start, "r.outcome"));
        Assert.Equal("Cloudflare challenge", Scalar(path, start, "r.block_reason"));
        Assert.DoesNotContain(TestCrawls.Rows(path, "SELECT url FROM urls"), url => url.Contains("browser-fallback-next", StringComparison.Ordinal));
        Assert.DoesNotContain(Hits(hits, SyntheticSite.BROWSER_FALLBACK_PATH), hit => hit.FromBrowser);
        Assert.Empty(TestCrawls.Rows(path, "SELECT code FROM notices WHERE code = 'browser-fallback'"));
    }

    [Fact]
    public async Task AChallengeTheBrowserAlsoGetsDoesNotSwitchTheHost()
    {
        var path = NewDatabase();
        var start = new Uri(_server.BaseUri, "/plants/blocked").AbsoluteUri;
        var summary = await RunAsync(path, start, browserFallback: true);
        Assert.Equal(CrawlStatus.Completed, summary.Status);

        Assert.Equal(((int)FetchOutcome.Blocked).ToString(), Scalar(path, start, "r.outcome"));
        Assert.Equal("Cloudflare challenge", Scalar(path, start, "r.block_reason"));
        Assert.Empty(TestCrawls.Rows(path, "SELECT code FROM notices WHERE code = 'browser-fallback'"));
    }

    private async Task<CrawlSummary> RunAsync(string path, string start, bool browserFallback)
    {
        var options = new CrawlOptions
        {
            StartUrl = start,
            Concurrency = 2,
            Rendering = JavaScriptRendering.Off,
            DiscoverSitemaps = false,
            CheckResources = false,
            CheckExternalLinks = false,
            BrowserFallback = browserFallback
        };
        await using var job = new CrawlEngine(NullLoggerFactory.Instance).Start(new CrawlRequest(options, path));
        return await job.Completion.WaitAsync(TimeSpan.FromSeconds(90));
    }

    private static string Scalar(string path, string url, string column) =>
        Assert.Single(TestCrawls.Rows(path, $"SELECT {column} FROM urls u JOIN responses r ON r.url_id = u.id WHERE u.url = '{url}'"));

    private static List<SyntheticHit> Hits(IEnumerable<SyntheticHit> hits, string path) =>
        hits.Where(hit => hit.Path == path).ToList();

    private string NewDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), $"si-fallback-{Guid.NewGuid():N}.sicrawl");
        _databases.Add(path);
        return path;
    }
}
