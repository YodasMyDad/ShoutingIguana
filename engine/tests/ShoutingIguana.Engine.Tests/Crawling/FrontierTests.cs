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

    public async Task InitializeAsync() =>
        _synthetic = await TestWebHost.StartAsync((baseUri, externalBaseUri) =>
        {
            _site = new SyntheticSite(new SyntheticSiteOptions { PageCount = PAGES }, baseUri, externalBaseUri);
            return (path, external) => _site.Handle(path, external);
        });

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
        var sitemapsFirst = await CrawlSitemapsFirstAsync(Options());

        Assert.Equal(TestCrawls.Rows(unhindered, CrawlTables.URLS), TestCrawls.Rows(sitemapsFirst, CrawlTables.URLS));
        foreach (var path in new[] { unhindered, sitemapsFirst })
        {
            Assert.Equal(_site.GetExpectations().Orphans, CrawlStatsReader.Read(path).UnlinkedPageCount);
        }
    }

    [Fact]
    public async Task DepthIsTheShortestPathWhicheverPageAnswersFirst()
    {
        // Home links /a and /b; /b leads to /d through /c, and /a links /d directly. So /d is two clicks deep and /e,
        // linked only from /d, three.
        await using var site = await StartChainSiteAsync();
        var origin = site.BaseUri.GetLeftPart(UriPartial.Authority);
        string StateIs(string page, UrlState state) => $"SELECT COUNT(*) FROM urls WHERE url = '{origin}{page}' AND state = {(int)state}";

        var unhindered = _files.NewPath();
        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(Options(site), unhindered)).Status);

        // /a answers last: /d and /e are recorded through the long way first, and the shorter path to /d arrives after.
        var shortcutLast = _files.NewPath();
        site.Hold("/a");
        await using (var job = TestCrawls.Start(Options(site), shortcutLast))
        {
            await TestCrawls.WaitUntilAsync(shortcutLast, StateIs("/e", UrlState.Done));
            site.Release("/a");
            Assert.Equal(CrawlStatus.Completed, (await job.Completion.WaitAsync(TestCrawls.TIMEOUT)).Status);
        }

        // /d is claimed the long way and still loading when the shorter path to it arrives.
        var shortcutWhileLoading = _files.NewPath();
        site.Hold("/a");
        site.Hold("/d");
        await using (var job = TestCrawls.Start(Options(site), shortcutWhileLoading))
        {
            await TestCrawls.WaitUntilAsync(shortcutWhileLoading, StateIs("/d", UrlState.Fetching));
            site.Release("/a");
            await TestCrawls.WaitUntilAsync(shortcutWhileLoading, $"SELECT COUNT(*) FROM urls WHERE url = '{origin}/d' AND depth = 2");
            site.Release("/d");
            Assert.Equal(CrawlStatus.Completed, (await job.Completion.WaitAsync(TestCrawls.TIMEOUT)).Status);
        }

        // The probe of the https:// version is claimed with the start URL, at its depth.
        const string DEPTHS = "SELECT url, depth FROM urls";
        List<string> expected = [$"{origin}/ | 0", $"{origin}/a | 1", $"{origin}/b | 1", $"{origin}/c | 2", $"{origin}/d | 2", $"{origin}/e | 3", $"https{origin[4..]}/ | 0"];
        Assert.Equal(expected, TestCrawls.Rows(unhindered, DEPTHS));
        Assert.Equal(expected, TestCrawls.Rows(shortcutLast, DEPTHS));
        Assert.Equal(expected, TestCrawls.Rows(shortcutWhileLoading, DEPTHS));
    }

    [Fact]
    public async Task SitemapsDoNotLiftTheDepthLimit()
    {
        // Sitemaps first, so every sitemap page is known before any link: the case that let them cascade.
        var path = await CrawlSitemapsFirstAsync(Options() with { MaxDepth = 1 });

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
        var path = await CrawlSitemapsFirstAsync(Options() with { MaxUrls = LIMIT });

        // Breadth-first from the home page: everything it links, then pages two clicks away, and no page that only
        // a sitemap lists, although the sitemaps were read first. The probe of the https:// version is fetched as well,
        // without taking a slot.
        var site = $"is_internal = 1 AND source <> {(int)DiscoverySource.SiteVersion}";
        var fetched = $"{site} AND state = {(int)UrlState.Done}";
        Assert.Equal(LIMIT, TestCrawls.Count(path, $"SELECT COUNT(*) FROM urls WHERE {fetched}"));
        Assert.Equal(LIMIT, TestCrawls.Count(path, $"SELECT COUNT(*) FROM urls WHERE {site} AND state <> {(int)UrlState.Skipped}"));
        Assert.Equal(1, TestCrawls.Count(path, $"SELECT COUNT(*) FROM urls WHERE source = {(int)DiscoverySource.SiteVersion} AND state = {(int)UrlState.Done}"));
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
            var summary = await job.Completion.WaitAsync(TestCrawls.TIMEOUT);
            Assert.Equal(CrawlStatus.Completed, summary.Status);

            // /embed was status-checked, then fetched again in full: counted once, where the file has it.
            ProgressAgreement.AssertMatchesTheFile(summary.Progress, linksFirst);
        }

        // Sitemap first: the home page waits until every sitemap URL is stored, and a little longer.
        site.Hold("/");
        await using (var job = TestCrawls.Start(Options(site), sitemapFirst))
        {
            await TestCrawls.WaitUntilAsync(sitemapFirst, "SELECT sitemaps_done FROM crawl");
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            site.Release("/");
            var summary = await job.Completion.WaitAsync(TestCrawls.TIMEOUT);
            Assert.Equal(CrawlStatus.Completed, summary.Status);
            ProgressAgreement.AssertMatchesTheFile(summary.Progress, sitemapFirst);
        }

        foreach (var table in new[] { CrawlTables.URLS, CrawlTables.RESPONSES, CrawlTables.LINKS, CrawlTables.SITEMAP_URLS })
        {
            Assert.Equal(TestCrawls.Rows(sitemapFirst, table), TestCrawls.Rows(linksFirst, table));
        }

        // Both crawl every page in full, each at the depth of its shortest link, followed or not. /de/ has no link: only
        // an hreflang entry names it, which the crawl doesn't follow, so it is read as a page the sitemap lists, at no
        // click depth. The https:// version is only probed, a status check.
        string Row(string path, int depth) => $"{origin}{path} | 1 | {(int)FetchMode.Crawl} | {depth} | {(int)UrlState.Done} | {(int)SkipReason.None}";
        var probe = $"https{origin[4..]}/ | 1 | {(int)FetchMode.Check} | 0 | {(int)UrlState.Done} | {(int)SkipReason.None}";
        List<string> expected = [Row("/", 0), Row("/about", 1), Row("/de/", CrawlDepth.UNLINKED), Row("/embed", 1), Row("/embed-held", 1), Row("/private", 1), Row("/sitemap-only", CrawlDepth.UNLINKED), probe];
        Assert.Equal(expected.Order(StringComparer.Ordinal), TestCrawls.Rows(linksFirst, CrawlTables.URLS));
        Assert.Equal(0, TestCrawls.Count(linksFirst, $"""
            SELECT COUNT(*) FROM responses r JOIN urls u ON u.id = r.url_id
            WHERE r.word_count IS NULL AND u.source <> {(int)DiscoverySource.SiteVersion}
            """));
    }

    /// <summary>
    /// The fourth pass's cases, as the real pages have them: goodsmile.info names /zh/ as its zh-TW version, a 404 no
    /// page links to; wisepilgrim.com's home page names its versions by hreflang alone; montreuxjazzfestival.com/fr/news/
    /// names its second page in rel="next" and has no link to it. The crawl follows none of these links unless told to,
    /// and a URL only they name used to be left unasked, so a version that was gone was never found. Now it is asked how
    /// it answers, last of all and at no click depth, and never read: what it links to stays out of the crawl.
    /// </summary>
    [Fact]
    public async Task AUrlOnlyHreflangOrRelNextNamesIsAskedHowItAnswersAndNotRead()
    {
        await using var site = await TestWebHost.StartAsync((_, _) => Serve(new(StringComparer.Ordinal)
        {
            ["/"] = Page("Home",
                """<link rel="alternate" href="/" hreflang="ja" /><link rel="alternate" href="/de/" hreflang="de" /><link rel="alternate" href="/zh/" hreflang="zh-TW" /><link rel="next" href="/page/2/">""",
                """<a href="/about">About</a> <button type="button">Load more</button>"""),
            ["/about"] = Page("About", string.Empty, "<p>About the test site.</p>"),
            ["/de/"] = Page("Startseite", """<link rel="alternate" href="/" hreflang="ja" /><link rel="alternate" href="/de/" hreflang="de" />""", """<a href="/de/nur-hier">Nur hier</a>"""),
            ["/de/nur-hier"] = Page("Nur hier", string.Empty, "<p>Only the German version links here.</p>"),
            ["/page/2/"] = Page("Page 2", """<link rel="prev" href="/">""", """<a href="/page/2/more">More</a>"""),
            ["/page/2/more"] = Page("More", string.Empty, "<p>Only page 2 links here.</p>")
        }));
        var origin = site.BaseUri.GetLeftPart(UriPartial.Authority);
        var path = _files.NewPath();
        await using (var job = TestCrawls.Start(Options(site), path))
        {
            Assert.Equal(CrawlStatus.Completed, (await job.Completion.WaitAsync(TestCrawls.TIMEOUT)).Status);
        }

        string Row(string url, FetchMode mode, int depth) => $"{origin}{url} | 1 | {(int)mode} | {depth} | {(int)UrlState.Done} | {(int)SkipReason.None}";
        var rows = TestCrawls.Rows(path, $"{CrawlTables.URLS} WHERE source <> {(int)DiscoverySource.SiteVersion} AND url NOT LIKE '%/robots.txt' AND url NOT LIKE '%/sitemap.xml'");
        Assert.Equal(new[]
        {
            Row("/", FetchMode.Crawl, 0), Row("/about", FetchMode.Crawl, 1), Row("/de/", FetchMode.Check, CrawlDepth.UNLINKED),
            Row("/page/2/", FetchMode.Check, CrawlDepth.UNLINKED), Row("/zh/", FetchMode.Check, CrawlDepth.UNLINKED)
        }.Order(StringComparer.Ordinal), rows);

        var analysis = await new CrawlAnalyzer(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance).AnalyzeAsync(path);
        var home = IssueReader.ReadAll(path).Where(issue => issue.Url == origin + "/").Select(issue => issue.Issue).ToList();
        Assert.Contains(IssueCatalog.HREFLANG_BROKEN_TARGET.Id, home);
        Assert.Contains(IssueCatalog.PAGINATION_NOT_LINKED.Id, home);
        Assert.DoesNotContain(IssueCatalog.HREFLANG_TO_NON_INDEXABLE.Id, home);
        Assert.DoesNotContain(IssueCatalog.HREFLANG_MISSING_RETURN.Id, home);

        // /de/ answered and wasn't read, so which versions nothing links to can't be told, and the check says so.
        Assert.Contains(new SkippedCheck(IssueCatalog.HREFLANG_UNLINKED.Id, SkippedCheckReasons.HREFLANG_OFF,
            "Hreflang links weren't followed in this crawl, and 1 language version they name wasn't read."), analysis.Skipped);

        // Told to follow them, the crawl reads both as pages, with what they link to.
        var followed = _files.NewPath();
        await using (var job = TestCrawls.Start(Options(site) with { CrawlHreflang = true, CrawlPagination = true }, followed))
        {
            Assert.Equal(CrawlStatus.Completed, (await job.Completion.WaitAsync(TestCrawls.TIMEOUT)).Status);
        }

        var read = TestCrawls.Rows(followed, CrawlTables.URLS);
        Assert.Contains(Row("/de/", FetchMode.Crawl, 1), read);
        Assert.Contains(Row("/de/nur-hier", FetchMode.Crawl, 2), read);
        Assert.Contains(Row("/page/2/more", FetchMode.Crawl, 2), read);
    }

    private CrawlOptions Options() => new() { StartUrl = _synthetic.BaseUri.AbsoluteUri, Concurrency = 8 };

    private static CrawlOptions Options(TestWebHost site) => new() { StartUrl = site.BaseUri.AbsoluteUri, Concurrency = 4 };

    /// <summary>
    /// Crawls the synthetic site with the home page held back until every sitemap URL is stored, and half a second
    /// longer: time enough for a crawler that doesn't wait for linked pages to crawl the sitemaps first.
    /// </summary>
    private Task<string> CrawlSitemapsFirstAsync(CrawlOptions options) =>
        CrawlSyntheticAsync(options, "/", "SELECT sitemaps_done FROM crawl", TimeSpan.FromMilliseconds(500));

    /// <summary>Crawls the synthetic site to completion, holding back one path until a query on the crawl is true, then for <paramref name="linger"/>.</summary>
    private async Task<string> CrawlSyntheticAsync(CrawlOptions options, string? heldPath = null, string? releaseWhen = null, TimeSpan linger = default)
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
            await Task.Delay(linger);
            _synthetic.Release(heldPath);
        }

        var summary = await job.Completion.WaitAsync(TestCrawls.TIMEOUT);
        Assert.Equal(CrawlStatus.Completed, summary.Status);

        // Status checks made full fetches leave the live counts where the file's are.
        ProgressAgreement.AssertMatchesTheFile(summary.Progress, path);
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

        return Serve(responses);
    });

    /// <summary>Home links /a and /b. The long way to /d goes /b, /c; /a links /d directly; /d links /e. No sitemap.</summary>
    private static Task<TestWebHost> StartChainSiteAsync() => TestWebHost.StartAsync((_, _) => Serve(new(StringComparer.Ordinal)
    {
        ["/"] = Page("Home", string.Empty, """<a href="/a">A</a> <a href="/b">B</a>"""),
        ["/a"] = Page("A", string.Empty, """<a href="/d">D</a>"""),
        ["/b"] = Page("B", string.Empty, """<a href="/c">C</a>"""),
        ["/c"] = Page("C", string.Empty, """<a href="/d">D</a>"""),
        ["/d"] = Page("D", string.Empty, """<a href="/e">E</a>"""),
        ["/e"] = Page("E", string.Empty, "<p>The end of the chain.</p>")
    }));

    private static Func<string, bool, SyntheticResponse> Serve(Dictionary<string, SyntheticResponse> responses) =>
        (path, _) => responses.TryGetValue(path, out var response) ? response : new SyntheticResponse(404, HTML, "<p>Not found</p>", null, null);

    private static SyntheticResponse Page(string title, string head, string body) => new(200, HTML,
        $"""<!DOCTYPE html><html lang="en"><head><title>{title}</title>{head}</head><body><h1>{title}</h1>{body}<p><a href="/">Home</a></p></body></html>""",
        null, null);
}
