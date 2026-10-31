using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>
/// The frontier: what is fetched, how deep it is, and what the limits let through. These tests hold back chosen
/// responses to force the order in which the crawler sees links and sitemaps; what it stores must not depend on it.
/// </summary>
public sealed class FrontierTests : IDisposable
{
    private const string HTML = "text/html; charset=utf-8";

    private readonly CrawlFiles _files = new();

    public void Dispose() => _files.Dispose();

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

    private static CrawlOptions Options(TestWebHost site) => new() { StartUrl = site.BaseUri.AbsoluteUri, Concurrency = 4 };

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
