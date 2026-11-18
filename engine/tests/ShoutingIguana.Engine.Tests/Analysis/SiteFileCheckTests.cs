using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>
/// The "can I fix it from this?" pass (step 3b, index part): a check of the site's robots.txt or sitemaps names the files
/// it's about, so a view can show the file instead of the home page it's reported on; and the details say what to change:
/// the robots.txt rule that blocked a URL, the sitemaps that list a URL twice, the first entry with a wrong lastmod, an
/// empty robots.txt.
/// </summary>
public sealed class SiteFileCheckTests
{
    private const string ROBOTS = "User-agent: *\nDisallow: /cart/\n\nDisallow: /checkout\nAllow: /checkout/thanks\n";

    [Fact]
    public void TheChecksOfTheSitesFilesSayWhichKindOfFileTheyreAbout()
    {
        Assert.Equal(
        [
            "robots-txt-missing", "robots-txt-no-sitemap", "robots-txt-unreachable", "robots-txt-unreachable-other-origin"
        ], IssueCatalog.All.Where(check => check.About == IssueAbout.RobotsTxt).Select(check => check.Id).Order(StringComparer.Ordinal));
        Assert.Equal(
        [
            "sitemap-empty", "sitemap-error", "sitemap-lastmod-invalid", "sitemap-missing", "sitemap-near-limit", "sitemap-over-limit"
        ], IssueCatalog.All.Where(check => check.About == IssueAbout.Sitemap).Select(check => check.Id).Order(StringComparer.Ordinal));

        // Each is reported once, for the whole site: the file is what to show in place of the page it's reported on.
        Assert.All(IssueCatalog.All.Where(check => check.About is not null), check => Assert.True(check.ReportedOnce, check.Id));
    }

    [Fact]
    public void ASitemapCheckNamesEveryFileItsAbout()
    {
        var site = new RuleHarness();
        site.Ok($"{site.Site}/");
        var urls = site.Table();
        StoredSitemap[] sitemaps =
        [
            Read($"{site.Site}/a.xml", 62_113), Read($"{site.Site}/b.xml", 46_000), Read($"{site.Site}/empty.xml", 0),
            new($"{site.Site}/gone.xml", 404, false, 0, "HTTP 404"), new($"{site.Site}/down.xml", 503, false, 0, "HTTP 503"),
            new($"{site.Site}/c.xml", 500, false, 0, "HTTP 500"), new($"{site.Site}/d.xml", 500, false, 0, "HTTP 500")
        ];
        var entries = new SitemapEntryFacts
        {
            Lastmods = [new SitemapLastmodFaults($"{site.Site}/a.xml", 2, 0, $"{site.Site}/p/1", 32_503_680_000L), new SitemapLastmodFaults($"{site.Site}/b.xml", 0, 1, $"{site.Site}/p/2", -1)]
        };

        var hits = new SitemapRules(urls, sitemaps, new CrawlOptions { StartUrl = site.Site }, crawlComplete: true, entries: entries).Finish().ToList();

        // Every file, though the detail names the first three.
        Assert.Equal([$"{site.Site}/gone.xml", $"{site.Site}/down.xml", $"{site.Site}/c.xml", $"{site.Site}/d.xml"], Files(hits, IssueCatalog.SITEMAP_ERROR));
        Assert.Equal([$"{site.Site}/a.xml"], Files(hits, IssueCatalog.SITEMAP_OVER_LIMIT));
        Assert.Equal([$"{site.Site}/b.xml"], Files(hits, IssueCatalog.SITEMAP_NEAR_LIMIT));
        Assert.Equal([$"{site.Site}/empty.xml"], Files(hits, IssueCatalog.SITEMAP_EMPTY));
        Assert.Equal([$"{site.Site}/a.xml", $"{site.Site}/b.xml"], Files(hits, IssueCatalog.SITEMAP_LASTMOD_INVALID));

        // No sitemap at all names where it looked: the conventional /sitemap.xml the crawler tried.
        var none = new SitemapRules(urls, [new($"{site.Site}/sitemap.xml", 404, false, 0, "HTTP 404", IsProbe: true)], new CrawlOptions { StartUrl = site.Site },
            crawlComplete: true).Finish().ToList();
        Assert.Equal([$"{site.Site}/sitemap.xml"], Files(none, IssueCatalog.SITEMAP_MISSING));

        // A check about the pages, not the files, names none.
        Assert.All(hits.Where(hit => hit.Issue.About is null), hit => Assert.Null(hit.Files));
    }

    [Fact]
    public void ARobotsTxtCheckNamesEveryFileItsAbout()
    {
        var site = new RuleHarness();
        site.Ok($"{site.Site}/");
        var urls = site.Table();
        var scope = new CrawlScope(new Uri($"{site.Site}/"), includeSubdomains: true, stayInStartFolder: false);
        HashSet<string> main = ["https://example.com"];
        RobotsRecord[] robots =
        [
            new("https://example.com", 503, null, null, 0), new("https://shop.example.com", 500, null, null, 0), new("https://blog.example.com", 404, null, null, 0)
        ];

        var hits = new RobotsTxtRules(urls, robots, scope, main).Finish().ToList();

        Assert.Equal(["https://example.com/robots.txt"], Files(hits, IssueCatalog.ROBOTS_TXT_UNREACHABLE));
        Assert.Equal(["https://shop.example.com/robots.txt"], Files(hits, IssueCatalog.ROBOTS_TXT_UNREACHABLE_OTHER_ORIGIN));
        Assert.Equal(["https://blog.example.com/robots.txt"], Files(hits, IssueCatalog.ROBOTS_TXT_MISSING));

        StoredSitemap[] probed = [new($"{site.Site}/sitemap.xml", 200, false, 12, null, IsProbe: true)];
        var noSitemap = new RobotsTxtRules(urls, [new("https://example.com", 200, "User-agent: *\n", null, 0)], scope, main, sitemaps: probed).Finish().ToList();
        Assert.Equal(["https://example.com/robots.txt"], Files(noSitemap, IssueCatalog.ROBOTS_TXT_NO_SITEMAP));
    }

    [Fact]
    public void AnEmptyRobotsTxtSaysSo()
    {
        var site = new RuleHarness();
        site.Ok($"{site.Site}/");
        var urls = site.Table();
        var scope = new CrawlScope(new Uri($"{site.Site}/"), includeSubdomains: false, stayInStartFolder: false);
        StoredSitemap[] probed = [new($"{site.Site}/sitemap.xml", 200, false, 12, null, IsProbe: true)];
        string? Detail(string content) => new RobotsTxtRules(urls, [new("https://example.com", 200, content, null, 0)], scope, new HashSet<string> { "https://example.com" }, sitemaps: probed)
            .Finish().Where(hit => hit.Issue == IssueCatalog.ROBOTS_TXT_NO_SITEMAP).Select(hit => hit.Detail).SingleOrDefault();

        Assert.Equal("https://example.com/robots.txt is empty, so it has no Sitemap: line; the sitemap was found at https://example.com/sitemap.xml", Detail(string.Empty));
        Assert.Equal("https://example.com/robots.txt is empty, so it has no Sitemap: line; the sitemap was found at https://example.com/sitemap.xml", Detail(" \r\n\uFEFF\n"));
        Assert.Equal("https://example.com/robots.txt has no Sitemap: line; the sitemap was found at https://example.com/sitemap.xml", Detail("User-agent: *\nDisallow:\n"));
    }

    [Fact]
    public void AUrlInSeveralSitemapsNamesThem()
    {
        var site = new RuleHarness();
        site.Ok($"{site.Site}/");
        var twice = site.Ok($"{site.Site}/a", inSitemap: true);
        var four = site.Ok($"{site.Site}/b", inSitemap: true);
        var urls = site.Table();
        var entries = new SitemapEntryFacts
        {
            Repeated = new Dictionary<int, IReadOnlyList<string>>
            {
                [twice] = [$"{site.Site}/pages.xml", $"{site.Site}/extra.xml"],
                [four] = [$"{site.Site}/1.xml", $"{site.Site}/2.xml", $"{site.Site}/3.xml", $"{site.Site}/4.xml"]
            }
        };

        var hits = new SitemapRules(urls, [Read($"{site.Site}/pages.xml", 2)], new CrawlOptions { StartUrl = site.Site }, crawlComplete: true, entries: entries).Finish()
            .Where(hit => hit.Issue == IssueCatalog.SITEMAP_URL_REPEATED).ToDictionary(hit => hit.UrlId, hit => hit.Detail);

        Assert.Equal("In 2 sitemaps: https://example.com/pages.xml and https://example.com/extra.xml", hits[twice]);
        Assert.Equal("In 4 sitemaps: https://example.com/1.xml, https://example.com/2.xml and 2 more", hits[four]);
    }

    [Fact]
    public void AWrongLastmodNamesTheFirstEntryAndWhatItSays()
    {
        var site = new RuleHarness();
        site.Ok($"{site.Site}/");
        var urls = site.Table();
        string? Detail(params SitemapLastmodFaults[] faults) =>
            new SitemapRules(urls, [], new CrawlOptions { StartUrl = site.Site }, crawlComplete: true, entries: new SitemapEntryFacts { Lastmods = faults }).Finish()
                .Where(hit => hit.Issue == IssueCatalog.SITEMAP_LASTMOD_INVALID).Select(hit => hit.Detail).SingleOrDefault();

        Assert.Equal("1 entry: 1 in the future (https://example.com/a.xml): https://example.com/p/1, lastmod 3000-01-01",
            Detail(new SitemapLastmodFaults("https://example.com/a.xml", 1, 0, "https://example.com/p/1", 32_503_680_000L)));
        Assert.Equal("1,204 entries: 1,200 in the future, 4 unreadable (https://example.com/a.xml and 1 more); first: https://example.com/p/9, lastmod not a date",
            Detail(new SitemapLastmodFaults("https://example.com/a.xml", 1_200, 3, "https://example.com/p/9", -1), new SitemapLastmodFaults("https://example.com/b.xml", 0, 1, "https://example.com/p/2", -1)));
    }

    [Fact]
    public void AUrlRobotsTxtBlockedSaysTheRuleAndItsLine()
    {
        var site = new RuleHarness();
        site.Ok($"{site.Site}/");
        var cart = site.Fetched($"{site.Site}/cart/change?id=1", FetchOutcome.RobotsDisallowed, 0);
        var checkout = site.Fetched($"{site.Site}/checkout", FetchOutcome.RobotsDisallowed, 0);
        var down = site.Fetched($"{site.Site}/basket", FetchOutcome.RobotsDisallowed, 0);
        var urls = site.Table();
        RobotsRecord[] robots = [new("https://example.com", 200, ROBOTS, null, 0)];
        var reasons = new Dictionary<int, string> { [down] = "robots.txt unreachable: 503 Service Unavailable" };

        var hits = new ResponseCodeRules(urls, reasons, robots, "ShoutingIguana", site.UrlOf).Finish().Where(hit => hit.Issue == IssueCatalog.BLOCKED_BY_ROBOTS)
            .ToDictionary(hit => hit.UrlId, hit => hit.Detail);

        // The rule as the file writes it, and its line: each rule is one group.
        Assert.Equal("Disallow: /cart/, line 2", hits[cart]);
        Assert.Equal("Disallow: /checkout, line 4", hits[checkout]);

        // Refused because robots.txt couldn't be read still says so.
        Assert.Equal("robots.txt unreachable: 503 Service Unavailable", hits[down]);
    }

    [Fact]
    public void PagesMissingFromTheSitemapsComeFewestLinksFirstWithTheirTitleAndDepth()
    {
        // A sitemap helps the pages links don't lead to: a product linked from one page, not the policy every footer links.
        Assert.Equal(IssueRowOrder.FewestLinksFirst, IssueCatalog.NOT_IN_SITEMAP.RowOrder);
        Assert.Equal(
            [new UrlSort("inlinks"), new UrlSort("wordCount", Descending: true), new UrlSort("url")],
            IssueGroupReader.ImportanceOrder(IssueCatalog.NOT_IN_SITEMAP, summary: true));
        Assert.Equal(["title", "depth"], IssueCatalog.NOT_IN_SITEMAP.Evidence);

        // Every other check keeps its order by importance.
        Assert.All(IssueCatalog.All.Where(check => check != IssueCatalog.NOT_IN_SITEMAP), check => Assert.Equal(IssueRowOrder.Importance, check.RowOrder));
        Assert.Equal([new UrlSort("inlinks", Descending: true), new UrlSort("depth"), new UrlSort("url")],
            IssueGroupReader.ImportanceOrder(IssueCatalog.CANONICAL_MISSING, summary: true));
    }

    private static StoredSitemap Read(string url, int urls) => new(url, 200, false, urls, null) { ListedSitemaps = 0 };

    private static IReadOnlyList<string>? Files(List<IssueHit> hits, IssueDefinition check) => Assert.Single(hits, hit => hit.Issue == check).Files;
}
