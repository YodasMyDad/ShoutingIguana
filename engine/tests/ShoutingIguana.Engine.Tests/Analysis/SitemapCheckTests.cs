using System.Text;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Sitemaps;
using ShoutingIguana.Engine.Sitemaps.Models;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>
/// Step 3b's sitemap and robots.txt checks (batch 4 of docs/v2-new-checks.md): each entry's lastmod as the reader finds
/// it and the crawl stores it, the files over or near the protocol's limits, empty ones, URLs in more than one, dates that
/// can't be read or haven't happened, robots.txt that doesn't name the sitemap found, and a URL on another scheme.
/// </summary>
public sealed class SitemapCheckTests
{
    private const string URLSET = "<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">";

    [Fact]
    public async Task TheReaderGivesEachEntryItsLastmodInEitherOrder()
    {
        var entries = await Read(URLSET +
            "<url><loc>https://example.com/a</loc><lastmod>2026-11-01</lastmod></url>" +
            "<url><lastmod> 2026-10-01T09:30:00+01:00 </lastmod><loc>https://example.com/b</loc></url>" +
            "<url><loc>https://example.com/c</loc></url>" +
            "<url><loc>https://example.com/d</loc><image:image xmlns:image=\"http://www.google.com/schemas/sitemap-image/1.1\"><image:loc>https://example.com/d.jpg</image:loc></image:image><lastmod>3000-01-01</lastmod></url>" +
            "</urlset>");

        Assert.Equal(
        [
            new SitemapEntry("https://example.com/a", false, "2026-11-01"), new SitemapEntry("https://example.com/b", false, "2026-10-01T09:30:00+01:00"),
            new SitemapEntry("https://example.com/c", false), new SitemapEntry("https://example.com/d", false, "3000-01-01")
        ], entries);

        // An index's sitemaps have theirs too; a text sitemap's lines have none.
        Assert.Equal([new SitemapEntry("https://example.com/s.xml", true, "2026-01-02")],
            await Read("<sitemapindex xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\"><sitemap><lastmod>2026-01-02</lastmod><loc>https://example.com/s.xml</loc></sitemap></sitemapindex>"));
        Assert.Equal([new SitemapEntry("https://example.com/t", false)], await Read("https://example.com/t\n", isText: true));
    }

    [Theory]
    [InlineData("2026-11-17", 1_794_873_600L)]
    [InlineData("2026-11", 1_793_491_200L)]
    [InlineData("2026", 1_767_225_600L)]
    [InlineData("2026-11-17T09:30:00+00:00", 1_794_907_800L)]
    [InlineData("2026-11-17T09:30:00Z", 1_794_907_800L)]
    [InlineData("2026-11-17T10:30:00+01:00", 1_794_907_800L)]
    [InlineData("2026-11-17T10:30:00+0100", 1_794_907_800L)]
    [InlineData("2026-11-17T09:30:00.123Z", 1_794_907_800L)]
    [InlineData("2026-11-17T09:30Z", 1_794_907_800L)]
    [InlineData("2026-11-17T09:30:00", 1_794_907_800L)]
    [InlineData("2026-11-17 09:30:00", 1_794_907_800L)]
    [InlineData("3000-01-01", 32_503_680_000L)]
    [InlineData("17/11/2026", SitemapLastmod.UNREADABLE)]
    [InlineData("yesterday", SitemapLastmod.UNREADABLE)]
    [InlineData("2026-13-01", SitemapLastmod.UNREADABLE)]
    [InlineData("Tue, 17 Nov 2026 09:30:00 GMT", SitemapLastmod.UNREADABLE)]
    public void ALastmodIsReadAsW3CDatetimeAndWhatSearchEnginesAlsoTake(string written, long seconds)
    {
        Assert.Equal(seconds, SitemapLastmod.Parse(written));
    }

    [Fact]
    public void NoLastmodIsNull()
    {
        Assert.Null(SitemapLastmod.Parse(null));
        Assert.Null(SitemapLastmod.Parse("  "));
    }

    [Fact]
    public void AFileOverOrNearTheLimitsIsReportedOnceWithWhatItsOver()
    {
        var (urls, options) = Site();
        StoredSitemap[] sitemaps =
        [
            Read("https://example.com/a.xml", 62_113), Read("https://example.com/b.xml", 10) with { SizeBytes = 53_000_000 },
            Read("https://example.com/index.xml", 0) with { IsIndex = true, ListedSitemaps = 50_001 }, Read("https://example.com/c.xml", 50_000),
            Read("https://example.com/d.xml", 45_000), Read("https://example.com/e.xml", 100) with { SizeBytes = 48_000_000 },
            Read("https://example.com/f.xml", 44_999), Read("https://example.com/g.xml", 60_000) with { Error = "HTTP 500", StatusCode = 500 },
            Read("https://example.com/h.xml", 51_000)
        ];

        var hits = new SitemapRules(urls, sitemaps, options, crawlComplete: true).Finish().ToList();

        Assert.Equal("https://example.com/a.xml (62,113 URLs), https://example.com/b.xml (50.5 MB), https://example.com/index.xml (50,001 sitemaps) and 1 more",
            Detail(hits, IssueCatalog.SITEMAP_OVER_LIMIT));
        Assert.Equal("https://example.com/c.xml (50,000 URLs), https://example.com/d.xml (45,000 URLs), https://example.com/e.xml (45.8 MB)",
            Detail(hits, IssueCatalog.SITEMAP_NEAR_LIMIT));
        Assert.All(hits.Where(hit => hit.Issue is { ReportedOnce: true }), hit => Assert.Equal(urls.HomeId, hit.UrlId));
        Assert.Null(Detail(new SitemapRules(urls, [Read("https://example.com/f.xml", 44_999)], options, crawlComplete: true).Finish().ToList(), IssueCatalog.SITEMAP_NEAR_LIMIT));
    }

    [Fact]
    public void AReadSitemapThatListsNothingIsEmpty()
    {
        var (urls, options) = Site();
        StoredSitemap[] sitemaps =
        [
            Read("https://example.com/empty.xml", 0) with { ListedSitemaps = 0 }, Read("https://example.com/index.xml", 0) with { IsIndex = true, ListedSitemaps = 3 },
            Read("https://example.com/empty-index.xml", 0) with { ListedSitemaps = 0 }, Read("https://example.com/missing.xml", 0) with { StatusCode = 404, Error = "HTTP 404" },
            Read("https://example.com/full.xml", 12) with { ListedSitemaps = 0 },

            // An earlier version didn't count an index's sitemaps: one read as an index isn't empty, a urlset with none is.
            Read("https://example.com/old-index.xml", 0) with { IsIndex = true }, Read("https://example.com/old.xml", 0)
        ];

        Assert.Equal("https://example.com/empty.xml and 2 more", Detail(new SitemapRules(urls, sitemaps, options, crawlComplete: true).Finish().ToList(), IssueCatalog.SITEMAP_EMPTY));
    }

    [Fact]
    public void AUrlInSeveralSitemapsAndDatesThatArentRightAreFlagged()
    {
        var site = new RuleHarness();
        site.Ok($"{site.Site}/");
        var listed = site.Ok($"{site.Site}/a", inSitemap: true);
        var elsewhere = site.Fetched("https://other.example/b", FetchOutcome.Success, 200);
        var urls = site.Table();
        var entries = new SitemapEntryFacts
        {
            Repeated = new Dictionary<int, IReadOnlyList<string>>
            {
                [listed] = ["https://example.com/a.xml", "https://example.com/b.xml", "https://example.com/c.xml"], [elsewhere] = ["https://example.com/a.xml", "https://example.com/b.xml"]
            },
            Lastmods = [new SitemapLastmodFaults("https://example.com/a.xml", 1_200, 3), new SitemapLastmodFaults("https://example.com/b.xml", 0, 1)]
        };

        var hits = new SitemapRules(urls, [Read($"{site.Site}/a.xml", 1)], new CrawlOptions { StartUrl = site.Site }, crawlComplete: true, entries: entries).Finish().ToList();

        // Only the site's own URLs: another site's is another check's.
        var repeated = Assert.Single(hits, hit => hit.Issue == IssueCatalog.SITEMAP_URL_REPEATED);
        Assert.Equal((listed, "In 3 sitemaps: https://example.com/a.xml, https://example.com/b.xml and 1 more"), (repeated.UrlId, repeated.Detail));
        Assert.Equal("1,204 entries: 1,200 in the future, 4 unreadable (https://example.com/a.xml and 1 more)", Detail(hits, IssueCatalog.SITEMAP_LASTMOD_INVALID));
        Assert.Equal("1 entry: 1 unreadable (https://example.com/b.xml)",
            Detail(new SitemapRules(urls, [], new CrawlOptions { StartUrl = site.Site }, crawlComplete: true,
                entries: new SitemapEntryFacts { Lastmods = [new SitemapLastmodFaults("https://example.com/b.xml", 0, 1)] }).Finish().ToList(), IssueCatalog.SITEMAP_LASTMOD_INVALID));
    }

    [Fact]
    public void AUrlOnAnotherSchemeThanItsSitemapIsOnAnotherHostToo()
    {
        var site = new RuleHarness();
        site.Ok($"{site.Site}/");
        var plain = site.Ok("http://example.com/page", inSitemap: true);
        var urls = site.Table();
        var entries = new SitemapEntryFacts { OtherScheme = new Dictionary<int, (string Url, string Sitemap)> { [plain] = ("http", "https") } };

        var hit = Assert.Single(new SitemapRules(urls, [Read($"{site.Site}/sitemap.xml", 1)], new CrawlOptions { StartUrl = site.Site }, crawlComplete: true, entries: entries).Finish(),
            hit => hit.Issue == IssueCatalog.SITEMAP_URL_OTHER_HOST);
        Assert.Equal((plain, "The URL is http://, the sitemap https://"), (hit.UrlId, hit.Detail));
    }

    [Fact]
    public void RobotsTxtThatDoesntNameTheSitemapFoundAnywayIsFlaggedOnce()
    {
        var site = new RuleHarness();
        site.Ok($"{site.Site}/");
        var urls = site.Table();
        var scope = new CrawlScope(new Uri($"{site.Site}/"), includeSubdomains: false, stayInStartFolder: false);
        HashSet<string> main = ["https://example.com", "https://www.example.com"];
        StoredSitemap[] probed = [new($"{site.Site}/sitemap.xml", 200, false, 12, null, IsProbe: true)];
        string? Detail(RobotsRecord[] robots, StoredSitemap[] sitemaps) =>
            new RobotsTxtRules(urls, robots, scope, main, sitemaps: sitemaps).Finish().Where(hit => hit.Issue == IssueCatalog.ROBOTS_TXT_NO_SITEMAP).Select(hit => hit.Detail).SingleOrDefault();

        Assert.Equal("https://example.com/robots.txt has no Sitemap: line; the sitemap was found at https://example.com/sitemap.xml",
            Detail([new("https://example.com", 200, "User-agent: *\nDisallow: /cart/\n# Sitemap: commented out", null, 0)], probed));
        Assert.Equal("https://example.com/robots.txt is empty, so it has no Sitemap: line; the sitemap was found at https://example.com/sitemap.xml",
            Detail([new("https://example.com", 200, string.Empty, null, 0)], probed));

        // A Sitemap: line on either main origin, in any case, names it; robots.txt that didn't answer, or no sitemap read, says nothing.
        Assert.Null(Detail([new("https://example.com", 200, "User-agent: *\r\n  SITEMAP: https://example.com/sitemap_index.xml\r\n", null, 0)], probed));
        Assert.Null(Detail([new("https://example.com", 200, string.Empty, null, 0), new("https://www.example.com", 200, "Sitemap: https://www.example.com/s.xml", null, 0)], probed));
        Assert.Null(Detail([new("https://example.com", 200, "\uFEFFSitemap: https://example.com/s.xml", null, 0)], probed));
        Assert.Null(Detail([new("https://example.com", 404, null, null, 0)], probed));
        Assert.Null(Detail([new("https://example.com", 200, string.Empty, null, 0)], [new($"{site.Site}/sitemap.xml", 404, false, 0, "HTTP 404", IsProbe: true)]));
    }

    private static (UrlTable Urls, CrawlOptions Options) Site()
    {
        var site = new RuleHarness();
        site.Ok($"{site.Site}/");
        return (site.Table(), new CrawlOptions { StartUrl = site.Site });
    }

    private static StoredSitemap Read(string url, int urls) => new(url, 200, false, urls, null) { ListedSitemaps = 0 };

    private static string? Detail(List<IssueHit> hits, IssueDefinition check) => hits.Where(hit => hit.Issue == check).Select(hit => hit.Detail).SingleOrDefault();

    private static async Task<List<SitemapEntry>> Read(string body, bool isText = false)
    {
        List<SitemapEntry> entries = [];
        await foreach (var entry in SitemapReader.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(body)), isText, CancellationToken.None))
        {
            entries.Add(entry);
        }

        return entries;
    }
}
