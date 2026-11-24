using System.Text.RegularExpressions;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>
/// The browser fallback is on unless a crawl turns it off, and on does nothing until a host blocks: a site that
/// answers plain HTTP is crawled without a browser, none is started and none is downloaded. Left at its default, a
/// host that does block is read in one.
/// </summary>
public sealed class BrowserFallbackDefaultTests : IAsyncLifetime
{
    private readonly CrawlFiles _files = new();
    private SyntheticSiteServer _server = null!;

    public async Task InitializeAsync() =>
        _server = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 30 });

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        _files.Dispose();
    }

    /// <summary>
    /// The synthetic site without its planted pages, which are where it blocks: products, categories, sitemaps,
    /// resources and links to another site, crawled with every option at its default but rendering, which is off so
    /// that the only thing that could start a browser is the fallback. Nothing asks the site from a browser, the
    /// crawl says nothing about one, and the renderer, the only thing that starts one, logs nothing at all.
    /// </summary>
    [Fact]
    public async Task ACrawlOfASiteThatNeverBlocksStartsNoBrowser()
    {
        var path = _files.NewPath();
        var hits = _server.Requests.WatchHits();
        var observer = new RecordingObserver();
        using var log = new CapturingLoggerFactory();
        var options = new CrawlOptions
        {
            StartUrl = _server.BaseUri.AbsoluteUri,
            Concurrency = 8,
            Rendering = JavaScriptRendering.Off,
            ExcludePatterns = [Regex.Escape(SyntheticSite.PLANTS_PATH)]
        };
        Assert.True(options.BrowserFallback);
        Assert.True(options.UseInstalledBrowser);

        CrawlSummary summary;
        await using (var job = new CrawlEngine(log).Start(new CrawlRequest(options, path), observer))
        {
            summary = await job.Completion.WaitAsync(TestCrawls.TIMEOUT);
        }

        Assert.Equal(CrawlStatus.Completed, summary.Status);
        Assert.True(CrawlDatabase.ReadOptions(path).BrowserFallback);

        // It was a crawl of the site, and the site blocked none of it.
        var pages = TestCrawls.Count(path, $"SELECT COUNT(*) FROM responses WHERE outcome = {(int)FetchOutcome.Success} AND content_type LIKE 'text/html%'");
        Assert.True(pages > 30, $"{pages} pages read");
        Assert.Equal(0, TestCrawls.Count(path, $"SELECT COUNT(*) FROM responses WHERE outcome = {(int)FetchOutcome.Blocked}"));

        // No browser asked the site for anything.
        Assert.True(hits.Count > pages, $"{hits.Count} requests seen");
        Assert.DoesNotContain(hits, hit => hit.FromBrowser);

        // The crawl said nothing of a browser: not that one was downloaded, not that one couldn't start, not that a host was read in one.
        string[] ofABrowser = ["javascript-rendering", "browser-fallback", "blocked", "human-check", "human-check-passed"];
        Assert.DoesNotContain(observer.Notices, notice => ofABrowser.Contains(notice.Code));
        Assert.Empty(TestCrawls.Rows(path, $"SELECT code FROM notices WHERE code IN ({string.Join(", ", ofABrowser.Select(code => $"'{code}'"))})"));

        // Starting a browser, downloading one and failing to are all logged by the renderer. It logged nothing.
        Assert.NotEmpty(log.Messages);
        Assert.DoesNotContain(log.Messages, message => message.Contains(typeof(JavaScriptRenderer).FullName!, StringComparison.Ordinal));
    }

    /// <summary>
    /// The same crawl started on the page that answers plain HTTP with a challenge and a browser with the page: with
    /// the fallback left at its default, the page is read in a browser. The computer's own browser is turned off, so
    /// no window opens here: the headless one reads it.
    /// </summary>
    [Fact]
    public async Task LeftAtItsDefaultAHostThatBlocksIsReadInABrowser()
    {
        var path = _files.NewPath();
        var start = new Uri(_server.BaseUri, SyntheticSite.BROWSER_FALLBACK_PATH).AbsoluteUri;
        var hits = _server.Requests.WatchHits();
        var options = new CrawlOptions
        {
            StartUrl = start,
            Concurrency = 2,
            Rendering = JavaScriptRendering.Off,
            DiscoverSitemaps = false,
            CheckResources = false,
            CheckExternalLinks = false,
            UseInstalledBrowser = false
        };

        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(options, path)).Status);

        Assert.Equal($"200 | {(int)FetchOutcome.Success}",
            Assert.Single(TestCrawls.Rows(path, $"SELECT r.status_code, r.outcome FROM urls u JOIN responses r ON r.url_id = u.id WHERE u.url = '{start}'")));
        Assert.Contains(hits, hit => hit is { Path: SyntheticSite.BROWSER_FALLBACK_PATH, FromBrowser: false });
        Assert.Contains(hits, hit => hit is { Path: SyntheticSite.BROWSER_FALLBACK_PATH, FromBrowser: true });
        Assert.Single(TestCrawls.Rows(path, "SELECT code FROM notices WHERE code = 'browser-fallback'"));
    }
}
