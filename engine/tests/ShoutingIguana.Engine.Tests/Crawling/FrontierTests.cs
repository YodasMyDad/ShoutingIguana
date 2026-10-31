using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>
/// The frontier: what is fetched, how deep it is, and what the limits let through. These tests hold back chosen
/// responses to force the order in which the crawler sees links and sitemaps; what it stores must not depend on it.
/// </summary>
public sealed class FrontierTests : IAsyncLifetime
{
    private const int PAGES = 600;
    private const string HTML = "text/html; charset=utf-8";

    private readonly CrawlFiles _files = new();
    private TestWebHost _synthetic = null!;
    private SyntheticSite _site = null!;
    private string _origin = null!;

    public async Task InitializeAsync()
    {
        _synthetic = await TestWebHost.StartAsync((baseUri, externalBaseUri) =>
        {
            _site = new SyntheticSite(new SyntheticSiteOptions { PageCount = PAGES }, baseUri, externalBaseUri);
            return _site.Handle;
        });
        _origin = _synthetic.BaseUri.GetLeftPart(UriPartial.Authority);
    }

    public async Task DisposeAsync()
    {
        await _synthetic.DisposeAsync();
        _files.Dispose();
    }

    [Fact]
    public async Task DepthDoesNotDependOnTiming()
    {
        var unhindered = await CrawlSyntheticAsync(Options());

        // The sitemaps are read before the home page answers, so sitemap URLs are queued long before any link.
        var sitemapsFirst = await CrawlSyntheticAsync(Options(), "/", "SELECT sitemaps_done FROM crawl");

        // The only path to some pages goes through /c/0, which answers last: their first links are longer paths, and
        // pages recorded meanwhile have to pass the shorter path on.
        var shortcutLast = await CrawlSyntheticAsync(Options(), "/c/0", $"""
            SELECT (SELECT COUNT(*) FROM urls WHERE state IN ({(int)UrlState.Queued}, {(int)UrlState.Fetching}) AND depth < {CrawlDepth.UNLINKED}) = 1
               AND (SELECT state FROM urls WHERE url = '{_origin}/c/0') = {(int)UrlState.Fetching}
            """);

        var expected = TestCrawls.Rows(unhindered, CrawlTables.URLS);
        Assert.Equal(expected, TestCrawls.Rows(sitemapsFirst, CrawlTables.URLS));
        Assert.Equal(expected, TestCrawls.Rows(shortcutLast, CrawlTables.URLS));
        foreach (var path in new[] { unhindered, sitemapsFirst, shortcutLast })
        {
            Assert.Equal(_site.GetExpectations().Orphans, CrawlStatsReader.Read(path).UnlinkedPageCount);
        }
    }

    [Fact]
    public async Task SitemapsDoNotLiftTheDepthLimit()
    {
        // Sitemaps first, so every sitemap page is known before any link: the case that let them cascade.
        var path = await CrawlSyntheticAsync(Options() with { MaxDepth = 1 }, "/", "SELECT sitemaps_done FROM crawl");

        // Sitemap URLs are crawled whatever their depth, but nothing else past depth 1 is: links found on them aren't
        // followed beyond the limit. Off the sitemaps, only the 20 categories the home page links are crawled.
        const string LISTED = "SELECT url_id FROM sitemap_urls";
        Assert.Equal(0, TestCrawls.Count(path, $"SELECT COUNT(*) FROM sitemap_urls s JOIN urls u ON u.id = s.url_id WHERE u.state <> {(int)UrlState.Done}"));
        Assert.Equal(0, TestCrawls.Count(path, $"""
            SELECT COUNT(*) FROM urls
            WHERE is_internal = 1 AND fetch_mode = {(int)FetchMode.Crawl} AND state = {(int)UrlState.Done} AND depth > 1 AND id NOT IN ({LISTED})
            """));
        Assert.Equal(SyntheticSite.CATEGORY_COUNT, TestCrawls.Count(path, $"""
            SELECT COUNT(*) FROM urls u JOIN responses r ON r.url_id = u.id WHERE r.word_count IS NOT NULL AND u.id NOT IN ({LISTED})
            """));
        Assert.True(CrawlStatsReader.Read(path).SkipReasons.GetValueOrDefault(SkipReason.DepthLimit) > 0);
    }

    [Fact]
    public async Task LinkedUrlsGetTheUrlLimitFirst()
    {
        const int LIMIT = 150;
        var path = await CrawlSyntheticAsync(Options() with { MaxUrls = LIMIT }, "/", "SELECT sitemaps_done FROM crawl");

        // Breadth-first from the home page: everything it links, then pages two clicks away, and no page that only
        // a sitemap lists, although the sitemaps were read first.
        var fetched = $"is_internal = 1 AND state = {(int)UrlState.Done}";
        Assert.Equal(LIMIT, TestCrawls.Count(path, $"SELECT COUNT(*) FROM urls WHERE {fetched}"));
        Assert.Equal(LIMIT, TestCrawls.Count(path, $"SELECT COUNT(*) FROM urls WHERE is_internal = 1 AND state <> {(int)UrlState.Skipped}"));
        Assert.Equal(2, TestCrawls.Count(path, $"SELECT MAX(depth) FROM urls WHERE {fetched}"));
        Assert.Equal(0, TestCrawls.Count(path, $"SELECT COUNT(*) FROM urls WHERE is_internal = 1 AND depth <= 1 AND state <> {(int)UrlState.Done}"));
        Assert.True(CrawlStatsReader.Read(path).SkipReasons.GetValueOrDefault(SkipReason.UrlLimit) > 0);
    }

    [Fact]
    public async Task SitemapsAndLinksGiveTheSameCrawlInEitherOrder()
    {
        await using var site = await StartLinkedSiteAsync();
        var origin = site.BaseUri.GetLeftPart(UriPartial.Authority);
        var linksFirst = _files.NewPath();
        var sitemapFirst = _files.NewPath();

        // Links first: the sitemap waits until the home page's links are stored. By then /embed has been
        // status-checked, and /embed-held is being checked when the sitemap arrives.
        site.Hold("/sitemap.xml");
        site.Hold("/embed-held");
        await using (var job = TestCrawls.Start(Options(site), linksFirst))
        {
            await TestCrawls.WaitUntilAsync(linksFirst, $"SELECT COUNT(*) FROM urls WHERE url = '{origin}/embed' AND state = {(int)UrlState.Done}");
            await TestCrawls.WaitUntilAsync(linksFirst, $"SELECT COUNT(*) FROM urls WHERE url = '{origin}/embed-held' AND state = {(int)UrlState.Fetching}");
            site.Release("/sitemap.xml");
            await TestCrawls.WaitUntilAsync(linksFirst, "SELECT sitemaps_done FROM crawl");
            site.Release("/embed-held");
            Assert.Equal(CrawlStatus.Completed, (await job.Completion.WaitAsync(TestCrawls.TIMEOUT)).Status);
        }

        // Sitemap first: the home page waits until every sitemap URL is stored.
        site.Hold("/");
        await using (var job = TestCrawls.Start(Options(site), sitemapFirst))
        {
            await TestCrawls.WaitUntilAsync(sitemapFirst, "SELECT sitemaps_done FROM crawl");
            site.Release("/");
            Assert.Equal(CrawlStatus.Completed, (await job.Completion.WaitAsync(TestCrawls.TIMEOUT)).Status);
        }

        foreach (var table in new[] { CrawlTables.URLS, CrawlTables.RESPONSES, CrawlTables.LINKS, CrawlTables.SITEMAP_URLS })
        {
            Assert.Equal(TestCrawls.Rows(sitemapFirst, table), TestCrawls.Rows(linksFirst, table));
        }

        // Both crawl every page in full, each at the depth of its shortest link, followed or not.
        string Row(string path, int depth) => $"{origin}{path} | 1 | {(int)FetchMode.Crawl} | {depth} | {(int)UrlState.Done} | {(int)SkipReason.None}";
        List<string> expected = [Row("/", 0), Row("/about", 1), Row("/de/", 1), Row("/embed", 1), Row("/embed-held", 1), Row("/private", 1), Row("/sitemap-only", CrawlDepth.UNLINKED)];
        Assert.Equal(expected.Order(StringComparer.Ordinal), TestCrawls.Rows(linksFirst, CrawlTables.URLS));
        Assert.Equal(0, TestCrawls.Count(linksFirst, "SELECT COUNT(*) FROM responses WHERE word_count IS NULL"));
    }

    private CrawlOptions Options() => new() { StartUrl = _synthetic.BaseUri.AbsoluteUri, Concurrency = 8 };

    private static CrawlOptions Options(TestWebHost site) => new() { StartUrl = site.BaseUri.AbsoluteUri, Concurrency = 4 };

    /// <summary>Crawls the synthetic site to completion, holding back one path until a query on the crawl is true.</summary>
    private async Task<string> CrawlSyntheticAsync(CrawlOptions options, string? heldPath = null, string? releaseWhen = null)
    {
        var path = _files.NewPath();
        if (heldPath is not null)
        {
            _synthetic.Hold(heldPath);
        }

        await using var job = TestCrawls.Start(options, path);
        if (heldPath is not null)
        {
            await TestCrawls.WaitUntilAsync(path, releaseWhen!);
            _synthetic.Release(heldPath);
        }

        Assert.Equal(CrawlStatus.Completed, (await job.Completion.WaitAsync(TestCrawls.TIMEOUT)).Status);
        return path;
    }

    /// <summary>
    /// A home page whose links the crawler doesn't follow by default (an hreflang alternate, a nofollow link) or only
    /// status-checks (iframes), all of which the sitemap lists, plus a page only the sitemap knows.
    /// </summary>
    private static Task<TestWebHost> StartLinkedSiteAsync() => TestWebHost.StartAsync((baseUri, _) =>
    {
        var origin = baseUri.GetLeftPart(UriPartial.Authority);
        string[] listed = ["/de/", "/private", "/embed", "/embed-held", "/sitemap-only"];
        Dictionary<string, SyntheticResponse> responses = new(StringComparer.Ordinal)
        {
            ["/robots.txt"] = new(200, "text/plain; charset=utf-8", $"User-agent: *\nDisallow:\n\nSitemap: {origin}/sitemap.xml\n", null, null),
            ["/sitemap.xml"] = new(200, "application/xml; charset=utf-8",
                """<?xml version="1.0" encoding="UTF-8"?><urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">""" +
                string.Concat(listed.Select(path => $"<url><loc>{origin}{path}</loc></url>")) + "</urlset>", null, null),
            ["/"] = Page("Home", """<link rel="alternate" hreflang="de" href="/de/">""",
                """<a href="/private" rel="nofollow">Private</a> <iframe src="/embed"></iframe> <iframe src="/embed-held"></iframe> <a href="/about">About</a>""")
        };

        foreach (var path in listed.Append("/about"))
        {
            responses[path] = Page($"Page {path}", string.Empty, "<p>A page of the test site.</p>");
        }

        return (path, _) => responses.TryGetValue(path, out var response) ? response : new SyntheticResponse(404, HTML, "<p>Not found</p>", null, null);
    });

    private static SyntheticResponse Page(string title, string head, string body) => new(200, HTML,
        $"""<!DOCTYPE html><html lang="en"><head><title>{title}</title>{head}</head><body><h1>{title}</h1>{body}<p><a href="/">Home</a></p></body></html>""",
        null, null);
}
