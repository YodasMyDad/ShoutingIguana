using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>
/// A list crawl fetches the addresses it was given and does not follow links. The planted page has no title, so the
/// analysis finds title-missing once, and the page it links to is recorded and not fetched.
/// </summary>
public sealed class ListModeCrawlTests : IAsyncLifetime
{
    private readonly List<string> _databases = [];
    private SyntheticSiteServer _server = null!;

    public async Task InitializeAsync() =>
        _server = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 2 });

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        foreach (var path in _databases.SelectMany(path => new[] { path, path + "-wal", path + "-shm" }))
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task TheListIsFetchedAndItsLinksAreNot()
    {
        var path = NewDatabase();
        var start = new Uri(_server.BaseUri, SyntheticSite.LIST_MODE_PATH).AbsoluteUri;
        var options = new CrawlOptions
        {
            StartUrl = start,
            Concurrency = 2,
            Rendering = JavaScriptRendering.Off,
            // Left on, to show a list crawl neither follows them nor reads the sitemap.
            DiscoverSitemaps = true,
            CheckResources = true,
            CheckExternalLinks = true,
            ListUrls = [start],
            BrowserFallback = false
        };

        await using var job = new CrawlEngine(NullLoggerFactory.Instance).Start(new CrawlRequest(options, path));
        var summary = await job.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(CrawlStatus.Completed, summary.Status);

        var pages = TestCrawls.Rows(path, $"""
            SELECT u.url FROM urls u JOIN responses r ON r.url_id = u.id
            WHERE r.outcome = {(int)FetchOutcome.Success} AND r.is_html = 1 AND u.source <> {(int)DiscoverySource.SiteVersion}
            ORDER BY u.url
            """);
        Assert.Equal([start], pages);

        var left = TestCrawls.Rows(path, $"""
            SELECT url FROM urls
            WHERE state = {(int)UrlState.Skipped} AND skip_reason = {(int)SkipReason.ListMode}
            """);
        Assert.Contains(left, url => url.EndsWith(SyntheticSite.LIST_MODE_TARGET_PATH, StringComparison.Ordinal));
        Assert.Contains(left, url => url.EndsWith("/static/site.css", StringComparison.Ordinal));
        Assert.DoesNotContain(TestCrawls.Rows(path, "SELECT url FROM urls"), url => url.Contains("/p/1", StringComparison.Ordinal));

        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        Assert.Equal(1, IssueReader.ReadCounts(path)[IssueCatalog.TITLE_MISSING.Id]);
        Assert.EndsWith(SyntheticSite.LIST_MODE_PATH, Assert.Single(IssueReader.ReadIssues(path, IssueCatalog.TITLE_MISSING.Id, 10)).Url);
    }

    /// <summary>
    /// The fourth pass's case (wisepilgrim.com, three of its addresses crawled as a list): a list crawl follows no link, so
    /// it can't say that nothing links to a page. One page of the list names the other as a language version and has no
    /// link to it, as the rest of the site may have: the checks of what links to a page aren't run, and say why.
    /// </summary>
    [Fact]
    public async Task AListCrawlDoesNotJudgeWhatLinksToAPage()
    {
        var path = NewDatabase();
        var naming = new Uri(_server.BaseUri, SyntheticSite.PLANTS_PATH + "hreflang-only-a").AbsoluteUri;
        var named = new Uri(_server.BaseUri, SyntheticSite.PLANTS_PATH + "hreflang-only-b").AbsoluteUri;
        // Not kept to the pages' folder, which would skip these checks for its own reason.
        var options = new CrawlOptions
        {
            StartUrl = naming, Concurrency = 2, Rendering = JavaScriptRendering.Off, StayInStartFolder = false, ListUrls = [naming, named]
        };

        await using var job = new CrawlEngine(NullLoggerFactory.Instance).Start(new CrawlRequest(options, path));
        var summary = await job.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(CrawlStatus.Completed, summary.Status);

        var analysis = await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        Assert.DoesNotContain(analysis.Issues, issue => issue.Issue == IssueCatalog.HREFLANG_UNLINKED);
        Assert.All(new[] { IssueCatalog.HREFLANG_UNLINKED, IssueCatalog.CANONICAL_UNLINKED, IssueCatalog.NON_INDEXABLE_INLINKS_ONLY },
            check => Assert.Contains((check.Id, SkippedCheckReasons.URL_LIST), analysis.Skipped.Select(skip => (skip.CheckId, skip.Reason))));
    }

    private string NewDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), $"si-list-{Guid.NewGuid():N}.sicrawl");
        _databases.Add(path);
        return path;
    }
}
