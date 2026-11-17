using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>
/// The checks that say they are reported once (<see cref="IssueDefinition.ReportedOnce"/>), each held against its own
/// rule on a site with the fault many times over: one issue all the same, on the URL that stands for the site, or one for
/// each other version of the site. A host words their count as "checked once, for the whole site", so a rule that
/// reported one issue a fault would make that a lie.
/// </summary>
public sealed class ReportedOnceTests
{
    private const string NOINDEX = "<meta name=\"robots\" content=\"noindex\">";

    [Fact]
    public void TheChecksReportedOnceAreTheNineTheirRulesReportOnce()
    {
        Assert.Equal(
        [
            "duplicate-site-version", "no-https", "robots-txt-missing", "robots-txt-no-sitemap", "robots-txt-unreachable", "robots-txt-unreachable-other-origin",
            "security-headers-missing", "site-hidden-from-search", "sitemap-empty", "sitemap-error", "sitemap-lastmod-invalid", "sitemap-missing",
            "sitemap-near-limit", "sitemap-over-limit"
        ], IssueCatalog.All.Where(check => check.ReportedOnce).Select(check => check.Id).Order(StringComparer.Ordinal));

        // A fault of the whole site is always reported once; a check reported once needn't be a fault of the whole site.
        Assert.All(IssueCatalog.All.Where(check => check.SiteWide), check => Assert.True(check.ReportedOnce, check.Id));
        Assert.Contains(IssueCatalog.All, check => check is { ReportedOnce: true, SiteWide: false });

        // It is how a count is worded, like the subject: no part of the checks' fingerprint.
        Assert.Equal(AnalysisFreshness.ChecksFingerprint, AnalysisFreshness.Fingerprint(IssueCatalog.All.Select(check => check with { ReportedOnce = !check.ReportedOnce })));
    }

    /// <summary>
    /// Every one of them, by its own rule, on a site where the fault is there several times: robots.txt failing on two
    /// main origins and another, missing on two more; five sitemaps that can't be read; no sitemap at all; every page
    /// without the security headers, and over plain HTTP; three other versions of the site answering; the home page and
    /// most other pages noindex.
    /// </summary>
    [Fact]
    public void EachIsOneIssueHoweverManyTimesTheFaultIsThere()
    {
        List<IssueHit> hits = [];

        // An HTTPS site of four pages, every one noindex, the home page among them, and none sending a security header.
        var site = new RuleHarness();
        var home = site.Page($"{site.Site}/", RuleHarness.Html(NOINDEX), IndexabilityReason.Noindex);
        AnalyzedPage[] pages =
        [
            home,
            site.Page($"{site.Site}/a", RuleHarness.Html(NOINDEX), IndexabilityReason.Noindex),
            site.Page($"{site.Site}/b", RuleHarness.Html(NOINDEX), IndexabilityReason.Noindex),
            site.Page($"{site.Site}/c", RuleHarness.Html(NOINDEX), IndexabilityReason.Noindex)
        ];
        var urls = site.Table();
        var security = new SecurityRules(urls);
        var visibility = new SiteVisibilityRules(urls);
        foreach (var page in pages)
        {
            hits.AddRange(security.Check(page));
            visibility.Observe(page);
        }

        hits.AddRange(visibility.Finish());

        // robots.txt fails on both main origins and on a subdomain, and is missing on two more subdomains.
        var scope = new CrawlScope(new Uri($"{site.Site}/"), includeSubdomains: true, stayInStartFolder: false);
        RobotsRecord[] robots =
        [
            new("https://example.com", 503, null, null, 0), new("https://www.example.com", 500, null, null, 0), new("https://blog.example.com", 503, null, null, 0),
            new("https://shop.example.com", 404, null, null, 0), new("https://cdn.example.com", 410, null, null, 0)
        ];
        hits.AddRange(new RobotsTxtRules(urls, robots, scope, new HashSet<string> { "https://example.com", "https://www.example.com" }).Finish());

        // Five sitemaps that can't be read: one issue, which lists the first three and counts the rest.
        var options = new CrawlOptions { StartUrl = site.Site };
        StoredSitemap[] failing = [.. Enumerable.Range(1, 5).Select(number => new StoredSitemap($"{site.Site}/sitemap-{number}.xml", 500, false, 0, "HTTP 500"))];
        var sitemapError = Assert.Single(new SitemapRules(urls, failing, options, crawlComplete: true).Finish(), hit => hit.Issue == IssueCatalog.SITEMAP_ERROR);
        Assert.EndsWith("sitemap-3.xml (500 Internal Server Error) and 2 more", sitemapError.Detail);
        hits.Add(sitemapError);

        // No sitemap at all: only the crawler's own guess, which failed.
        StoredSitemap[] none = [new($"{site.Site}/sitemap.xml", 404, false, 0, "HTTP 404", IsProbe: true)];
        hits.AddRange(new SitemapRules(urls, none, options, crawlComplete: true).Finish().Where(hit => hit.Issue == IssueCatalog.SITEMAP_MISSING));

        // Four files over the limits, three near them, three empty, and dates wrong in two: one issue each.
        StoredSitemap[] files =
        [
            .. Enumerable.Range(1, 4).Select(number => new StoredSitemap($"{site.Site}/big-{number}.xml", 200, false, 60_000, null)),
            .. Enumerable.Range(1, 3).Select(number => new StoredSitemap($"{site.Site}/near-{number}.xml", 200, false, 46_000, null)),
            .. Enumerable.Range(1, 3).Select(number => new StoredSitemap($"{site.Site}/empty-{number}.xml", 200, false, 0, null) { ListedSitemaps = 0 })
        ];
        var lastmods = new SitemapEntryFacts { Lastmods = [new($"{site.Site}/big-1.xml", 10, 2), new($"{site.Site}/big-2.xml", 0, 5)] };
        hits.AddRange(new SitemapRules(urls, files, options, crawlComplete: true, entries: lastmods).Finish()
            .Where(hit => hit.Issue != IssueCatalog.SITEMAP_ERROR && hit.Issue != IssueCatalog.SITEMAP_MISSING));

        // robots.txt answers on both main origins, and neither names the sitemap the crawler found anyway.
        RobotsRecord[] silent = [new("https://example.com", 200, "User-agent: *", null, 0), new("https://www.example.com", 200, string.Empty, null, 0)];
        hits.AddRange(new RobotsTxtRules(urls, silent, scope, new HashSet<string> { "https://example.com", "https://www.example.com" }, sitemaps: files).Finish());

        // Each of these, however many pages or origins or files are at fault, is one issue, on the home page.
        var onHome = hits.Where(hit => hit.Issue.ReportedOnce).ToList();
        Assert.Equal(
        [
            "robots-txt-missing", "robots-txt-no-sitemap", "robots-txt-unreachable", "robots-txt-unreachable-other-origin", "security-headers-missing",
            "site-hidden-from-search", "sitemap-empty", "sitemap-error", "sitemap-lastmod-invalid", "sitemap-missing", "sitemap-near-limit", "sitemap-over-limit"
        ], onHome.Select(hit => hit.Issue.Id).Order(StringComparer.Ordinal));
        Assert.All(onHome, hit => Assert.Equal(home.UrlId, hit.UrlId));
        Assert.Equal("https://example.com/robots.txt (503 Service Unavailable), https://www.example.com/robots.txt (500 Internal Server Error)",
            onHome.Single(hit => hit.Issue == IssueCatalog.ROBOTS_TXT_UNREACHABLE).Detail);
        Assert.Equal("https://shop.example.com/robots.txt (404 Not Found), https://cdn.example.com/robots.txt (410 Gone)",
            onHome.Single(hit => hit.Issue == IssueCatalog.ROBOTS_TXT_MISSING).Detail);

        // A site over plain HTTP, every page of it: reported on the home page alone.
        var plain = new RuleHarness("http://example.com");
        AnalyzedPage[] insecure = [plain.Page($"{plain.Site}/", RuleHarness.Html()), plain.Page($"{plain.Site}/a", RuleHarness.Html()), plain.Page($"{plain.Site}/b", RuleHarness.Html())];
        var plainSecurity = new SecurityRules(plain.Table());
        var noHttps = Assert.Single(insecure.SelectMany(plainSecurity.Check), hit => hit.Issue == IssueCatalog.NO_HTTPS);
        Assert.Equal(insecure[0].UrlId, noHttps.UrlId);

        // Three other versions of the site answer with a page: one issue for each version, on that version's URL.
        var versions = new RuleHarness();
        versions.Ok($"{versions.Site}/");
        int[] others =
        [
            versions.Probed("https://www.example.com/", FetchOutcome.Success, 200), versions.Probed("http://www.example.com/", FetchOutcome.Success, 200),
            versions.Probed("http://example.com/", FetchOutcome.Success, 200)
        ];
        var duplicates = new SiteVersionRules(versions.Table(), new Uri($"{versions.Site}/"), versions.Find, versions.UrlOf).Finish().ToList();
        Assert.All(duplicates, hit => Assert.Equal(IssueCatalog.DUPLICATE_SITE_VERSION, hit.Issue));
        Assert.Equal(others.Order(), duplicates.Select(hit => hit.UrlId).Order());

        // That is every check that says it is reported once.
        Assert.Equal(IssueCatalog.All.Where(check => check.ReportedOnce).Select(check => check.Id).Order(StringComparer.Ordinal),
            onHome.Select(hit => hit.Issue.Id).Append(noHttps.Issue.Id).Append(IssueCatalog.DUPLICATE_SITE_VERSION.Id).Order(StringComparer.Ordinal));
    }
}
