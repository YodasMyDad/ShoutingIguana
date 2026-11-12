using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Sitemaps;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>Cross-page rules over hand-built URL statuses, including cases the HTTP-only synthetic site can't plant.</summary>
public sealed class SiteRuleTests
{
    [Fact]
    public void RedirectTracesFollowChainsAndStopAtLoopsAndMissingTargets()
    {
        var site = new RuleHarness();
        var start = site.Fetched($"{site.Site}/a", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/b");
        site.Fetched($"{site.Site}/b", FetchOutcome.Redirect, 302, redirectTo: $"{site.Site}/c");
        var end = site.Ok($"{site.Site}/c");
        var loop = site.Fetched($"{site.Site}/x", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/y");
        site.Fetched($"{site.Site}/y", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/x");
        var dangling = site.Fetched($"{site.Site}/no-location", FetchOutcome.Redirect, 301);
        var urls = site.Table();

        Assert.Equal(new RedirectTrace(end, 2, IsLoop: false), urls.TraceRedirects(start));
        Assert.True(urls.TraceRedirects(loop).IsLoop);
        Assert.Equal(new RedirectTrace(dangling, 0, IsLoop: false), urls.TraceRedirects(dangling));
    }

    [Fact]
    public void RedirectRulesReportDowngradesTemporaryRedirectsAndBrokenEnds()
    {
        var site = new RuleHarness();
        site.Ok($"{site.Site}/");
        var downgrade = site.Fetched($"{site.Site}/secure", FetchOutcome.Redirect, 301, redirectTo: "http://example.com/insecure");
        site.Ok("http://example.com/insecure");
        var temporary = site.Fetched($"{site.Site}/promo", FetchOutcome.Redirect, 302, redirectTo: $"{site.Site}/");
        var broken = site.Fetched($"{site.Site}/moved", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/gone");
        site.Fetched($"{site.Site}/gone", FetchOutcome.ClientError, 404);
        var dangling = site.Fetched($"{site.Site}/no-location", FetchOutcome.Redirect, 301);

        var hits = new RedirectRules(site.Table()).Finish().ToList();

        Assert.Contains(new IssueHit(IssueCatalog.REDIRECT_DOWNGRADE, downgrade, null, site.Id("http://example.com/insecure")), hits);
        Assert.Contains(hits, hit => hit.Issue == IssueCatalog.REDIRECT_TEMPORARY && hit.UrlId == temporary);
        Assert.Contains(hits, hit => hit.Issue == IssueCatalog.REDIRECT_BROKEN && hit.UrlId == broken);
        Assert.Equal("301 Moved Permanently without a usable Location header", hits.Single(hit => hit.UrlId == dangling).Detail);
        Assert.Equal(4, hits.Count);
    }

    [Fact]
    public void ChainsAreReportedWhereVisitorsEnterThemAndLongOnesAreNotLoops()
    {
        var site = new RuleHarness();
        var home = site.Page($"{site.Site}/", RuleHarness.Html(body: "<a href=\"http://example.com/a\">A</a><a href=\"/linked-mid\">M</a><a href=\"/long-0\">L</a>"));

        // http -> https -> trailing slash -> new place: three hops, entered only at the first.
        var start = site.Fetched("http://example.com/a", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/a");
        site.Fetched($"{site.Site}/a", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/a/");
        site.Fetched($"{site.Site}/a/", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/b/");
        site.Ok($"{site.Site}/b/");

        // Mid-chain hops still count when something links to them, or a sitemap lists them.
        var linkedMid = site.Fetched($"{site.Site}/linked-mid", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/listed-mid");
        var listedMid = site.Fetched($"{site.Site}/listed-mid", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/x", inSitemap: true);
        site.Fetched($"{site.Site}/x", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/b/");

        // 25 hops: past the trace limit without repeating a URL.
        for (var hop = 0; hop < 25; hop++)
        {
            site.Fetched($"{site.Site}/long-{hop}", FetchOutcome.Redirect, 301, redirectTo: hop < 24 ? $"{site.Site}/long-{hop + 1}" : $"{site.Site}/b/");
        }

        var urls = site.Table();
        var rules = new RedirectRules(urls);
        rules.Observe(home);
        var chains = rules.Finish().Where(hit => hit.Issue == IssueCatalog.REDIRECT_CHAIN).ToList();

        var longChain = site.Id($"{site.Site}/long-0");
        Assert.Equal([start, linkedMid, longChain, listedMid], chains.Select(hit => hit.UrlId));
        Assert.Equal("3 redirects", chains.Single(hit => hit.UrlId == start).Detail);
        Assert.Equal("More than 20 redirects, which browsers don't follow", chains.Single(hit => hit.UrlId == longChain).Detail);
        Assert.Equal(new RedirectTrace(site.Id($"{site.Site}/long-20"), UrlTable.MAX_REDIRECT_HOPS, IsLoop: false), urls.TraceRedirects(site.Id($"{site.Site}/long-0")));
        Assert.True(urls.IsBrokenThroughRedirects(site.Id($"{site.Site}/long-0")));
        Assert.False(urls.IsBrokenThroughRedirects(site.Id($"{site.Site}/long-5")));
    }

    [Theory]
    [InlineData(10, "10 redirects")]
    [InlineData(12, "12 redirects, more than the 10 Googlebot follows")]
    public void ChainsLongerThanGooglebotFollowsSaySo(int hops, string detail)
    {
        var site = new RuleHarness();
        for (var hop = 0; hop < hops; hop++)
        {
            site.Fetched($"{site.Site}/r{hop}", FetchOutcome.Redirect, 301, redirectTo: hop < hops - 1 ? $"{site.Site}/r{hop + 1}" : $"{site.Site}/end", inSitemap: hop == 0);
        }

        site.Ok($"{site.Site}/end");

        Assert.Equal(detail, new RedirectRules(site.Table()).Finish().Single(hit => hit.Issue == IssueCatalog.REDIRECT_CHAIN).Detail);
    }

    [Fact]
    public void RobotsTxtProblemsCountOnlyForTheSitesOwnOrigins()
    {
        var site = new RuleHarness();
        site.Ok($"{site.Site}/");
        var scope = new CrawlScope(new Uri($"{site.Site}/"), includeSubdomains: false, stayInStartFolder: false);
        HashSet<string> main = ["https://example.com"];
        RobotsRecord[] robots =
        [
            new("https://example.com", 503, null, null, 0),
            new("https://partner.example", 404, null, null, 0),
            new("https://cdn.example", null, null, "Timed out", 0)
        ];

        var hits = new RobotsTxtRules(site.Table(), robots, scope, main).Finish().ToList();

        var unreachable = Assert.Single(hits);
        Assert.Equal(IssueCatalog.ROBOTS_TXT_UNREACHABLE, unreachable.Issue);
        Assert.Equal("https://example.com/robots.txt (503 Service Unavailable)", unreachable.Detail);

        // Google treats 429 like a server error; a redirect loop is handled as "no rules", as the crawler does.
        var limited = new RobotsTxtRules(site.Table(), [new("https://example.com", 429, null, null, 0)], scope, main).Finish().Single();
        var looping = new RobotsTxtRules(site.Table(), [new("https://example.com", null, null, "More than 5 redirects", 0)], scope, main).Finish().Single();
        Assert.Equal(IssueCatalog.ROBOTS_TXT_UNREACHABLE, limited.Issue);
        Assert.Equal(IssueCatalog.ROBOTS_TXT_MISSING, looping.Issue);
    }

    [Fact]
    public void RobotsTxtFailingOnAnotherOfTheSitesOriginsOnlyAffectsThatOrigin()
    {
        var site = new RuleHarness();
        site.Ok($"{site.Site}/");
        var scope = new CrawlScope(new Uri($"{site.Site}/"), includeSubdomains: true, stayInStartFolder: false);
        RobotsRecord[] robots =
        [
            new("https://example.com", 200, "User-agent: *", null, 0),
            new("https://blog.example.com", 503, null, null, 0),
            new("http://example.com", null, null, "Timed out", 0)
        ];

        var hit = new RobotsTxtRules(site.Table(), robots, scope, new HashSet<string> { "https://example.com" }).Finish().Single();

        Assert.Equal(new IssueHit(IssueCatalog.ROBOTS_TXT_UNREACHABLE_OTHER_ORIGIN, site.Id($"{site.Site}/"),
            "https://blog.example.com/robots.txt (503 Service Unavailable), http://example.com/robots.txt (Timed out)"), hit);
        Assert.Equal(IssueSeverity.Warning, hit.Issue.Severity);

        // The home page's origin counts as the site's own once the start URL redirects there.
        var moved = new RobotsTxtRules(site.Table(), robots, scope, new HashSet<string> { "https://example.com", "https://blog.example.com" }).Finish().ToList();
        Assert.Equal([IssueCatalog.ROBOTS_TXT_UNREACHABLE, IssueCatalog.ROBOTS_TXT_UNREACHABLE_OTHER_ORIGIN], moved.Select(found => found.Issue));
    }

    [Fact]
    public void SitemapRulesCompareTheSitemapsWithWhatWasCrawled()
    {
        var site = new RuleHarness();
        site.Ok($"{site.Site}/", inSitemap: true);
        var unlisted = site.Ok($"{site.Site}/unlisted");
        var listedNoindex = site.Ok($"{site.Site}/hidden", IndexabilityReason.Noindex, inSitemap: true);
        site.Fetched($"{site.Site}/guide.pdf", FetchOutcome.Success, 200, inSitemap: true);
        var urls = site.Table();
        StoredSitemap[] sitemaps =
        [
            new($"{site.Site}/sitemap.xml", 200, false, 3, null),
            new($"{site.Site}/sitemap-old.xml", 404, false, 0, "HTTP 404")
        ];

        var hits = new SitemapRules(urls, sitemaps, new CrawlOptions { StartUrl = site.Site }, crawlComplete: true).Finish().ToList();

        Assert.Contains(hits, hit => hit.Issue == IssueCatalog.NOT_IN_SITEMAP && hit.UrlId == unlisted);
        Assert.Equal("The URL is noindex", hits.Single(hit => hit.Issue == IssueCatalog.SITEMAP_NON_INDEXABLE).Detail);
        Assert.Equal(listedNoindex, hits.Single(hit => hit.Issue == IssueCatalog.SITEMAP_NON_INDEXABLE).UrlId);
        Assert.Contains("sitemap-old.xml (404 Not Found)", hits.Single(hit => hit.Issue == IssueCatalog.SITEMAP_ERROR).Detail);
        Assert.Equal(3, hits.Count);

    }

    [Theory]
    [InlineData(503, "HTTP 503", false)]
    [InlineData(429, "HTTP 429", false)]
    [InlineData(403, "HTTP 403", false)]
    [InlineData(null, "Timed out", false)]
    [InlineData(200, "Unexpected end of file", false)]
    [InlineData(404, "HTTP 404", true)]
    [InlineData(410, "HTTP 410", true)]
    public void SitemapCoverageNeedsEveryDeclaredSitemapToSayWhatItLists(int? status, string error, bool coverageChecked)
    {
        var site = new RuleHarness();
        site.Ok($"{site.Site}/", inSitemap: true);
        var post = site.Ok($"{site.Site}/blog/post");
        var urls = site.Table();
        StoredSitemap[] sitemaps =
        [
            new($"{site.Site}/page-sitemap.xml", 200, false, 1, null),
            new($"{site.Site}/post-sitemap.xml", status, false, 0, error)
        ];

        var hits = new SitemapRules(urls, sitemaps, new CrawlOptions { StartUrl = site.Site }, crawlComplete: true).Finish().ToList();

        // A sitemap that failed for a reason that says nothing about its contents may well list the post.
        Assert.Equal(coverageChecked, hits.Contains(new IssueHit(IssueCatalog.NOT_IN_SITEMAP, post)));
        Assert.Equal(coverageChecked ? null : sitemaps[1], SitemapRules.Unreadable(sitemaps));
        Assert.Contains(hits, hit => hit.Issue == IssueCatalog.SITEMAP_ERROR);
    }

    [Fact]
    public void ASitemapLeftUnreadAtTheCrawlersLimitsIsNotBrokenButMayListAnyPage()
    {
        var site = new RuleHarness();
        site.Ok($"{site.Site}/", inSitemap: true);
        site.Ok($"{site.Site}/blog/post");
        var urls = site.Table();
        StoredSitemap[] sitemaps =
        [
            new($"{site.Site}/sitemap-1.xml", 200, false, 1, null),
            new($"{site.Site}/sitemap-1001.xml", null, false, 0, $"{SitemapDiscoverer.NOT_READ_ERROR}: the crawler reads at most 1,000 sitemaps")
        ];

        var hits = new SitemapRules(urls, sitemaps, new CrawlOptions { StartUrl = site.Site }, crawlComplete: true).Finish();

        // Neither a broken sitemap nor a post missing from them: the sitemap left unread may well list it.
        Assert.Empty(hits);
        Assert.Equal(sitemaps[1], SitemapRules.Unreadable(sitemaps));
        Assert.Equal("the crawler reads at most 1,000 sitemaps", sitemaps[1].NotReadReason);
        Assert.Null(sitemaps[0].NotReadReason);
    }

    [Fact]
    public void SitemapEntriesOnAnotherHostAreReportedWithTheirStatus()
    {
        // A sitemap on example.com still listing www.example.com after a move to the bare domain, and a partner's page.
        var site = new RuleHarness();
        site.Ok($"{site.Site}/", inSitemap: true);
        var moved = site.Fetched("https://www.example.com/page", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/page", inSitemap: true);
        site.Ok($"{site.Site}/page", inSitemap: true);
        var gone = site.Fetched("https://www.example.com/gone", FetchOutcome.ClientError, 404, inSitemap: true);
        var fine = site.Fetched("https://www.example.com/fine", FetchOutcome.Success, 200, isHtml: true, inSitemap: true);
        var notChecked = site.Id("https://www.example.com/unchecked");
        var partner = site.Fetched("https://partner.example/listed", FetchOutcome.Success, 200, inSitemap: true);
        site.Fetched("https://partner.example/linked", FetchOutcome.ClientError, 404);
        var urls = site.Table();
        var statuses = Enumerable.Range(0, urls.Length).Select(id => urls[id]).ToArray();
        statuses[notChecked] = statuses[notChecked] with { InSitemap = true };
        StoredSitemap[] sitemaps = [new($"{site.Site}/sitemap.xml", 200, false, 7, null)];

        // The www twin is the same site, so its entries are on another host only because example.com's sitemap lists
        // them; the reader works that out from where each sitemap lives.
        Assert.True(urls[moved].IsInternal);
        var all = new SitemapRules(new UrlTable(statuses, urls.StartId), sitemaps, new CrawlOptions { StartUrl = site.Site }, crawlComplete: true,
            offHostEntries: new HashSet<int> { moved, gone, fine, notChecked }).Finish().ToList();

        Assert.Equal(
        [
            new IssueHit(IssueCatalog.SITEMAP_URL_OTHER_HOST, moved, "The URL redirects (301 Moved Permanently)"),
            new IssueHit(IssueCatalog.SITEMAP_URL_OTHER_HOST, gone, "The URL returns 404 Not Found"),
            new IssueHit(IssueCatalog.SITEMAP_URL_OTHER_HOST, fine, "The URL returns 200 OK"),
            new IssueHit(IssueCatalog.SITEMAP_URL_OTHER_HOST, notChecked, "The URL wasn't checked"),
            new IssueHit(IssueCatalog.SITEMAP_URL_OTHER_HOST, partner, "The URL returns 200 OK")
        ], all.Where(hit => hit.Issue == IssueCatalog.SITEMAP_URL_OTHER_HOST));

        // Being the site's own URLs, the www ones are judged like any other entry as well.
        Assert.Contains(all, hit => hit.Issue == IssueCatalog.SITEMAP_NON_INDEXABLE && hit.UrlId == moved);
    }

    [Fact]
    public void AFailedGuessAtSitemapXmlMeansNoSitemapNotABrokenOne()
    {
        var site = new RuleHarness();
        site.Ok($"{site.Site}/");
        site.Ok($"{site.Site}/page");
        var urls = site.Table();
        StoredSitemap[] probe = [new($"{site.Site}/sitemap.xml", 404, false, 0, "HTTP 404", IsProbe: true)];
        var options = new CrawlOptions { StartUrl = site.Site };

        Assert.Equal([IssueCatalog.SITEMAP_MISSING], new SitemapRules(urls, probe, options, crawlComplete: true).Finish().Select(hit => hit.Issue));
        Assert.Empty(new SitemapRules(urls, probe, options with { DiscoverSitemaps = false }, crawlComplete: true).Finish());

        // A stopped crawl can't know what's missing yet.
        Assert.Empty(new SitemapRules(urls, probe, options, crawlComplete: false).Finish());
    }

    [Fact]
    public void DuplicatesIgnoreCaseAndSpacingAndSkipNonIndexablePages()
    {
        var site = new RuleHarness();
        var first = site.Page($"{site.Site}/a", RuleHarness.Html("<title>Blue Widgets</title>"));
        var second = site.Page($"{site.Site}/b", RuleHarness.Html("<title> blue  WIDGETS </title>"));
        var noindex = site.Page($"{site.Site}/c", RuleHarness.Html("<title>Blue Widgets</title>"), IndexabilityReason.Noindex);
        var rules = new DuplicateRules(new AnalysisSettings());

        foreach (var page in new[] { first, second, noindex })
        {
            rules.Observe(page);
        }

        var titles = rules.Finish().Where(hit => hit.Issue == IssueCatalog.TITLE_DUPLICATE).ToList();
        Assert.Equal([first.UrlId, second.UrlId], titles.Select(hit => hit.UrlId).Order());
        Assert.All(titles, hit => Assert.Equal("1 other page", hit.Detail));

        // Each names the other, and the pair is named after its first page, which names itself as well: one group.
        Assert.Equal([(first.UrlId, (int?)second.UrlId), (second.UrlId, first.UrlId)], titles.Select(hit => (hit.UrlId, hit.RelatedUrlId)).Order());
        Assert.All(titles, hit => Assert.Equal([first.UrlId], hit.AllTargets));
    }

    [Fact]
    public void APaginatedSeriesMayRepeatItsHeadingButNotItsTitle()
    {
        var site = new RuleHarness();
        AnalyzedPage Page(string path, string title, string pagination = "") =>
            site.Page($"{site.Site}/{path}", RuleHarness.Html($"<title>{title}</title>{pagination}", "<h1>Garden sheds</h1>"));

        var first = Page("sheds", "Garden sheds", $"<link rel=\"next\" href=\"{site.Site}/sheds?p=2\">");
        var second = Page("sheds?p=2", "Garden sheds", $"<link rel=\"prev\" href=\"{site.Site}/sheds\"><link rel=\"next\" href=\"{site.Site}/sheds?p=3\">");
        var third = Page("sheds?p=3", "Garden sheds, page 3", $"<link rel=\"prev\" href=\"{site.Site}/sheds?p=2\">");
        var elsewhere = Page("more-sheds", "More garden sheds");

        // Within the series, only the repeated title is reported.
        var series = new DuplicateRules(new AnalysisSettings());
        foreach (var page in new[] { first, second, third })
        {
            series.Observe(page);
        }

        var hits = series.Finish().ToList();
        Assert.DoesNotContain(hits, hit => hit.Issue == IssueCatalog.H1_DUPLICATE);
        Assert.Equal([first.UrlId, second.UrlId], hits.Where(hit => hit.Issue == IssueCatalog.TITLE_DUPLICATE).Select(hit => hit.UrlId).Order());

        // A page outside it with the same heading is a duplicate of all three, and they of it. The four are one cluster, named
        // after its first page, which the series' pages name too, though their duplicate is the page outside it.
        var withOther = new DuplicateRules(new AnalysisSettings());
        foreach (var page in new[] { first, second, third, elsewhere })
        {
            withOther.Observe(page);
        }

        Assert.Equal(
        [
            new IssueHit(IssueCatalog.H1_DUPLICATE, first.UrlId, "1 other page", elsewhere.UrlId) { Targets = [first.UrlId] },
            new IssueHit(IssueCatalog.H1_DUPLICATE, second.UrlId, "1 other page", elsewhere.UrlId) { Targets = [first.UrlId] },
            new IssueHit(IssueCatalog.H1_DUPLICATE, third.UrlId, "1 other page", elsewhere.UrlId) { Targets = [first.UrlId] },
            new IssueHit(IssueCatalog.H1_DUPLICATE, elsewhere.UrlId, "3 other pages", first.UrlId)
        ], withOther.Finish().Where(hit => hit.Issue == IssueCatalog.H1_DUPLICATE).OrderBy(hit => hit.UrlId));

        // A noindex page in the middle joins the series with its own links, though it isn't compared itself.
        var through = new DuplicateRules(new AnalysisSettings());
        through.Observe(Page("racks", "Bike racks"));
        through.Observe(site.Page($"{site.Site}/racks?p=2", RuleHarness.Html(
            $"<title>Bike racks, page 2</title><link rel=\"prev\" href=\"{site.Site}/racks\"><link rel=\"next\" href=\"{site.Site}/racks?p=3\">",
            "<h1>Garden sheds</h1>"), IndexabilityReason.Noindex));
        through.Observe(Page("racks?p=3", "Bike racks, page 3"));
        Assert.DoesNotContain(through.Finish(), hit => hit.Issue == IssueCatalog.H1_DUPLICATE);
    }

    [Fact]
    public void NearDuplicatesAreGroupedAndExactCopiesReportedSeparately()
    {
        var site = new RuleHarness();
        var words = Enumerable.Range(0, 1_000).Select(index => $"term{(index * 7919) % 997}").ToArray();
        var edited = (string[])words.Clone();
        edited[500] = "different";
        var unrelated = Enumerable.Range(0, 1_000).Select(index => $"other{(index * 104729) % 991}").ToArray();
        AnalyzedPage Page(string path, string[] text) => site.Page($"{site.Site}/{path}", RuleHarness.Html("<title>t</title>", $"<p>{string.Join(' ', text)}</p>"));

        var original = Page("original", words);
        var copy = Page("copy", words);
        var near = Page("near", edited);
        var other = Page("other", unrelated);
        var rules = new DuplicateRules(new AnalysisSettings());
        foreach (var page in new[] { original, copy, near, other })
        {
            rules.Observe(page);
        }

        var hits = rules.Finish().ToList();
        Assert.Equal([original.UrlId, copy.UrlId], hits.Where(hit => hit.Issue == IssueCatalog.DUPLICATE_CONTENT).Select(hit => hit.UrlId).Order());

        // The edit is near both copies, so all three form one group, each pointing at its lowest-id other member, and the
        // group named after its lowest-id page, which names itself there.
        Assert.Equal(
        [
            new IssueHit(IssueCatalog.NEAR_DUPLICATE_CONTENT, original.UrlId, "2 similar pages", copy.UrlId) { Targets = [original.UrlId] },
            new IssueHit(IssueCatalog.NEAR_DUPLICATE_CONTENT, copy.UrlId, "2 similar pages", original.UrlId),
            new IssueHit(IssueCatalog.NEAR_DUPLICATE_CONTENT, near.UrlId, "2 similar pages", original.UrlId)
        ], hits.Where(hit => hit.Issue == IssueCatalog.NEAR_DUPLICATE_CONTENT).OrderBy(hit => hit.UrlId));
    }

    [Fact]
    public void CanonicalsThatLeadBackToThePageOrNowhereLeaveItIndexable()
    {
        var site = new RuleHarness();
        AnalyzedPage Page(string path, string canonical) => site.Page($"{site.Site}{path}",
            RuleHarness.Html($"<title>Short</title><link rel=\"canonical\" href=\"{site.Site}{canonical}\">"), IndexabilityReason.Canonicalised);

        var backHere = Page("/page/", "/page");
        site.Fetched($"{site.Site}/page", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/page/");
        var toGone = Page("/a", "/gone");
        site.Fetched($"{site.Site}/gone", FetchOutcome.ClientError, 404);
        var toGoneLater = Page("/b", "/moved");
        site.Fetched($"{site.Site}/moved", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/gone");
        var toPage = Page("/c", "/d");
        site.Ok($"{site.Site}/d");
        var toRedirect = Page("/e", "/old");
        site.Fetched($"{site.Site}/old", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/d");
        AnalyzedPage[] pages = [backHere, toGone, toGoneLater, toPage, toRedirect];
        var urls = site.Table(canonicals: pages.Select(page => (page.UrlId, page.Links.Single(link => link.Kind == LinkKind.Canonical).ToId)));
        var analysed = pages.Select(page => RuleHarness.AsAnalysed(page, urls)).ToList();
        var canonicalRules = new CanonicalRules(urls, site.UrlOf);
        List<IssueHit> Hits(AnalyzedPage page) => canonicalRules.Check(page).ToList();

        Assert.Equal([(backHere.UrlId, IndexabilityReason.None), (toGone.UrlId, IndexabilityReason.None), (toGoneLater.UrlId, IndexabilityReason.None)],
            urls.IndexabilityChanges);
        Assert.Equal([IndexabilityReason.None, IndexabilityReason.None, IndexabilityReason.None, IndexabilityReason.Canonicalised, IndexabilityReason.Canonicalised],
            analysed.Select(page => page.IndexabilityReason));

        // The canonical is still worth fixing, but the page is audited like any indexable page.
        Assert.Equal([new IssueHit(IssueCatalog.CANONICAL_TO_REDIRECT, backHere.UrlId, "The canonical URL redirects back to this page", site.Id($"{site.Site}/page"))],
            Hits(analysed[0]));
        Assert.Equal([IssueCatalog.TITLE_TOO_SHORT], new TitleRules(new AnalysisSettings()).Check(analysed[0]).Select(hit => hit.Issue));
        Assert.Equal("The canonical URL returns 404 Not Found", Hits(analysed[1]).Single().Detail);
        Assert.Equal("The canonical URL redirects to a URL that returns 404 Not Found", Hits(analysed[2]).Single(hit => hit.Issue == IssueCatalog.CANONICAL_BROKEN).Detail);
        Assert.Equal([IssueCatalog.CANONICALISED_PAGE], Hits(analysed[3]).Select(hit => hit.Issue));
        Assert.Equal([IssueCatalog.CANONICALISED_PAGE, IssueCatalog.CANONICAL_TO_REDIRECT], Hits(analysed[4]).Select(hit => hit.Issue));
    }

    /// <summary>
    /// A canonical to a redirect is fixed by naming where the redirect ends, so the detail gives that URL: after one
    /// redirect or several, whatever kind they are, and a URL the crawl knows but didn't fetch included. The related URL
    /// stays the canonical itself. One that comes back to the page needs no URL: it is the page's own.
    /// </summary>
    [Fact]
    public void ACanonicalToARedirectNamesWhereTheRedirectEnds()
    {
        var site = new RuleHarness();
        AnalyzedPage Page(string path, string canonical) => site.Page($"{site.Site}{path}",
            RuleHarness.Html($"<link rel=\"canonical\" href=\"{canonical}\">"), IndexabilityReason.Canonicalised);

        var once = Page("/once", $"{site.Site}/old");
        var old = site.Fetched($"{site.Site}/old", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/new");
        site.Ok($"{site.Site}/new");
        var thrice = Page("/thrice", $"{site.Site}/first");
        var first = site.Fetched($"{site.Site}/first", FetchOutcome.Redirect, 302, redirectTo: $"{site.Site}/second");
        site.Fetched($"{site.Site}/second", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/third");
        site.Fetched($"{site.Site}/third", FetchOutcome.Redirect, 308, redirectTo: $"{site.Site}/new");
        var away = Page("/away", $"{site.Site}/out");
        var leaving = site.Fetched($"{site.Site}/out", FetchOutcome.Redirect, 301, redirectTo: "https://partner.example/landing");
        var farAway = Page("/far-away", $"{site.Site}/out-first");
        var leavingTwice = site.Fetched($"{site.Site}/out-first", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/out");
        var refused = Page("/refused", $"{site.Site}/to-private");
        var toPrivate = site.Fetched($"{site.Site}/to-private", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/private/page");
        site.Fetched($"{site.Site}/private/page", FetchOutcome.RobotsDisallowed, 0);
        var back = Page("/back/", $"{site.Site}/back");
        var toBack = site.Fetched($"{site.Site}/back", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/back/");
        AnalyzedPage[] pages = [once, thrice, away, farAway, refused, back];
        var urls = site.Table(canonicals: pages.Select(page => (page.UrlId, page.Links.Single(link => link.Kind == LinkKind.Canonical).ToId)));
        var rules = new CanonicalRules(urls, site.UrlOf);
        IssueHit Hit(AnalyzedPage page) => rules.Check(RuleHarness.AsAnalysed(page, urls)).Single(hit => hit.Issue == IssueCatalog.CANONICAL_TO_REDIRECT);

        Assert.Equal(new IssueHit(IssueCatalog.CANONICAL_TO_REDIRECT, once.UrlId, "The canonical URL redirects (301 Moved Permanently) to https://example.com/new", old), Hit(once));
        Assert.Equal(new IssueHit(IssueCatalog.CANONICAL_TO_REDIRECT, thrice.UrlId,
            "The canonical URL redirects (302 Found) and, after 3 redirects, ends at https://example.com/new", first), Hit(thrice));

        // The partner's page was never asked for (links to other sites off, a limit, a stopped crawl), so it may redirect
        // again: it is where the redirects lead, not known to be where they end, and the detail says so.
        Assert.False(urls[site.Find("https://partner.example/landing")].Fetched);
        Assert.Equal(new IssueHit(IssueCatalog.CANONICAL_TO_REDIRECT, away.UrlId,
            "The canonical URL redirects (301 Moved Permanently) to https://partner.example/landing, which wasn't checked", leaving), Hit(away));
        Assert.Equal(new IssueHit(IssueCatalog.CANONICAL_TO_REDIRECT, farAway.UrlId,
            "The canonical URL redirects (301 Moved Permanently) and, after 2 redirects, leads to https://partner.example/landing, which wasn't checked", leavingTwice),
            Hit(farAway));

        // Nor is a URL the crawler was kept out of known to be the end.
        Assert.Equal(new IssueHit(IssueCatalog.CANONICAL_TO_REDIRECT, refused.UrlId,
            "The canonical URL redirects (301 Moved Permanently) to https://example.com/private/page, which is blocked by robots.txt", toPrivate), Hit(refused));
        Assert.Equal(new IssueHit(IssueCatalog.CANONICAL_TO_REDIRECT, back.UrlId, "The canonical URL redirects back to this page", toBack), Hit(back));
    }

    /// <summary>noindex with a canonical elsewhere is a contradiction; with one that comes back here or leads nowhere, it isn't.</summary>
    [Fact]
    public void ANoindexPageWhoseCanonicalComesBackOrLeadsNowhereIsNoContradiction()
    {
        var site = new RuleHarness();
        AnalyzedPage Page(string path, string canonical) => site.Page($"{site.Site}{path}",
            RuleHarness.Html($"<title>A noindex page with a canonical</title><meta name=\"robots\" content=\"noindex\"><link rel=\"canonical\" href=\"{site.Site}{canonical}\">"),
            IndexabilityReason.Noindex);

        var backHere = Page("/page/", "/page");
        site.Fetched($"{site.Site}/page", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/page/");
        var toGone = Page("/a", "/gone");
        site.Fetched($"{site.Site}/gone", FetchOutcome.ClientError, 404);
        var elsewhere = Page("/b", "/c");
        site.Ok($"{site.Site}/c");
        var urls = site.Table();
        var rules = new CanonicalRules(urls, site.UrlOf);
        List<IssueDefinition> Issues(AnalyzedPage page) => [.. rules.Check(RuleHarness.AsAnalysed(page, urls)).Select(hit => hit.Issue)];

        Assert.DoesNotContain(IssueCatalog.NOINDEX_WITH_CANONICAL, Issues(backHere));
        Assert.DoesNotContain(IssueCatalog.NOINDEX_WITH_CANONICAL, Issues(toGone));
        Assert.Contains(IssueCatalog.CANONICAL_BROKEN, Issues(toGone));
        Assert.Contains(IssueCatalog.NOINDEX_WITH_CANONICAL, Issues(elsewhere));
    }

    [Fact]
    public void AnInstantMetaRefreshIsARedirect()
    {
        var site = new RuleHarness();
        var home = site.Page($"{site.Site}/", RuleHarness.Html(body: "<a href=\"/old\">Old</a><a href=\"/later\">Later</a>"));
        var stub = site.Page($"{site.Site}/old", RuleHarness.Html("<meta http-equiv=\"refresh\" content=\"0; url=/new\"><meta name=\"robots\" content=\"noindex\">"),
            IndexabilityReason.Noindex);
        site.Ok($"{site.Site}/new", inSitemap: true);
        var delayed = site.Page($"{site.Site}/later", RuleHarness.Html("<meta http-equiv=\"refresh\" content=\"5; url=/new\">"));
        var target = site.Id($"{site.Site}/new");
        var urls = site.Table(instantRefreshes: [(stub.UrlId, target)]);

        Assert.Equal(new UrlStatus(Known: true, IsInternal: true, IsHttps: true, Depth: 1, Fetched: true, FetchOutcome.Redirect, 200, IsHtml: true,
            IsParsed: true, IndexabilityReason.Redirected, target, null, InSitemap: false, IsMetaRefresh: true), urls[stub.UrlId]);
        Assert.Equal([(stub.UrlId, IndexabilityReason.Redirected)], urls.IndexabilityChanges);
        Assert.True(RuleHarness.AsAnalysed(stub, urls).IsRedirect);
        Assert.False(RuleHarness.AsAnalysed(delayed, urls).IsRedirect);

        // Linking to the stub is linking to a redirect, and the link reaches /new.
        Assert.Equal("1 redirecting URL", new LinkRules(urls).Check(home).Single(hit => hit.Issue == IssueCatalog.LINKS_TO_REDIRECTS).Detail);
        var inlinks = new InlinkRules(urls, crawlComplete: true);
        inlinks.Observe(home);
        Assert.Empty(inlinks.Finish());

        // The noindex on a stub nobody sees doesn't matter; the refresh does.
        Assert.Equal([IssueCatalog.META_REFRESH], new IndexabilityRules().Check(RuleHarness.AsAnalysed(stub, urls)).Select(hit => hit.Issue));
        Assert.Equal($"The canonical URL redirects with an instant meta refresh to {site.Site}/new",
            new CanonicalRules(urls, site.UrlOf).Check(site.Page($"{site.Site}/x", RuleHarness.Html($"<link rel=\"canonical\" href=\"{site.Site}/old\">"),
                IndexabilityReason.Canonicalised)).Single(hit => hit.Issue == IssueCatalog.CANONICAL_TO_REDIRECT).Detail);
    }

    [Fact]
    public void OrphansAreSitemapPagesNoCrawledPageLinksTo()
    {
        var site = new RuleHarness();
        var home = site.Page($"{site.Site}/", RuleHarness.Html(body:
            "<a href=\"/linked\">Linked</a><a href=\"/only-nofollow\" rel=\"nofollow\">Hidden</a><a href=\"/services\">Services</a>"));
        site.Ok($"{site.Site}/linked", inSitemap: true);
        site.Ok($"{site.Site}/only-nofollow", inSitemap: true);
        site.Fetched($"{site.Site}/services", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/services/");
        site.Ok($"{site.Site}/services/", inSitemap: true);
        var orphan = site.Ok($"{site.Site}/orphan", inSitemap: true);
        site.Ok($"{site.Site}/", inSitemap: true);
        var urls = site.Table();
        var rules = new InlinkRules(urls, crawlComplete: true);

        rules.Observe(home);

        // /services/ is only linked through its redirecting form, which still counts.
        Assert.Equal([orphan], rules.Finish().Select(hit => hit.UrlId));

        var partial = new InlinkRules(urls, crawlComplete: false);
        partial.Observe(home);
        Assert.Empty(partial.Finish());
    }

    /// <summary>
    /// Started at https://example.com/, the home. www answers with a page; http://www goes there first; http:// redirects
    /// home, as it should.
    /// </summary>
    [Fact]
    public void AVersionThatAnswersWithAPageIsADuplicateOfTheSite()
    {
        var site = new RuleHarness();
        site.Ok($"{site.Site}/");
        var www = site.Probed("https://www.example.com/", FetchOutcome.Success, 200);
        var wwwOverHttp = site.Probed("http://www.example.com/", FetchOutcome.Redirect, 301, redirectTo: "https://www.example.com/");
        site.Probed("http://example.com/", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/");

        var rules = Versions(site);

        Assert.True(rules.CanCheck);
        Assert.Equal(
        [
            new IssueHit(IssueCatalog.DUPLICATE_SITE_VERSION, www, "Answers 200 OK instead of redirecting to https://example.com/"),
            new IssueHit(IssueCatalog.DUPLICATE_SITE_VERSION, wwwOverHttp,
                "Redirects to https://www.example.com/, which answers 200 OK instead of redirecting to https://example.com/", www)
        ], rules.Finish());
        Assert.Equal(IssueCategory.Content, IssueCatalog.DUPLICATE_SITE_VERSION.Category);
        Assert.Equal(IssueSeverity.Warning, IssueCatalog.DUPLICATE_SITE_VERSION.Severity);
    }

    /// <summary>
    /// A version that doesn't work isn't a copy of the site: a dead www twin is normal. Nor is one that leads off the site's
    /// versions, or loops. Its redirects are other checks' to judge.
    /// </summary>
    [Fact]
    public void AVersionThatFailsOrLeadsElsewhereIsNoDuplicate()
    {
        var site = new RuleHarness();
        site.Ok($"{site.Site}/");
        site.Probed("https://www.example.com/", FetchOutcome.NetworkError, 0);
        site.Probed("http://example.com/", FetchOutcome.Redirect, 301, redirectTo: "https://parked.example/");
        site.Probed("https://parked.example/", FetchOutcome.Success, 200);
        site.Probed("http://www.example.com/", FetchOutcome.Redirect, 301, redirectTo: "http://www.example.com/loop");
        site.Probed("http://www.example.com/loop", FetchOutcome.Redirect, 301, redirectTo: "http://www.example.com/");
        Assert.True(Versions(site).CanCheck);
        Assert.Empty(Versions(site).Finish());

        foreach (var (outcome, status) in new[] { (FetchOutcome.ClientError, 404), (FetchOutcome.ServerError, 500), (FetchOutcome.Blocked, 403), (FetchOutcome.RobotsDisallowed, 0) })
        {
            site.Probed("https://www.example.com/", outcome, status);
            Assert.Empty(Versions(site).Finish());
        }

        // A version answering with something other than a page (an empty 204, a file) isn't a copy of the site either.
        site.Fetched("https://www.example.com/", FetchOutcome.Success, 204, source: DiscoverySource.SiteVersion);
        Assert.Empty(Versions(site).Finish());
    }

    /// <summary>The start URL redirects to www, so www is the main version, and the bare domain must redirect there too.</summary>
    [Fact]
    public void TheMainVersionIsWhereTheStartUrlLeads()
    {
        var site = new RuleHarness();
        site.Fetched($"{site.Site}/", FetchOutcome.Redirect, 301, redirectTo: "https://www.example.com/");
        site.Ok("https://www.example.com/");
        var bare = site.Probed("http://example.com/", FetchOutcome.Success, 200);
        site.Probed("http://www.example.com/", FetchOutcome.Redirect, 301, redirectTo: "https://www.example.com/");

        var rules = Versions(site);

        Assert.True(rules.CanCheck);
        Assert.Equal([new IssueHit(IssueCatalog.DUPLICATE_SITE_VERSION, bare, "Answers 200 OK instead of redirecting to https://www.example.com/")],
            rules.Finish());
    }

    /// <summary>
    /// The check is skipped, not reported clean, when the crawl can't say how every version answers: a crawl file from
    /// before the crawler probed, a crawl stopped before the probes or their redirects were fetched, or a home page that
    /// isn't a page, so there's no main version to redirect to.
    /// </summary>
    [Fact]
    public void WhenAnyVersionsAnswerIsUnknownTheCheckCantRun()
    {
        var old = new RuleHarness();
        old.Ok($"{old.Site}/");
        Assert.False(Versions(old).CanCheck);
        Assert.Empty(Versions(old).Finish());

        var stopped = new RuleHarness();
        stopped.Ok($"{stopped.Site}/");
        stopped.Probed("https://www.example.com/", FetchOutcome.Success, 200);
        stopped.Probed("http://example.com/", FetchOutcome.Redirect, 301, redirectTo: $"{stopped.Site}/");
        stopped.Id("http://www.example.com/");
        Assert.False(Versions(stopped).CanCheck);
        Assert.Empty(Versions(stopped).Finish());

        // Fetched, but stopped before where it redirects, on another version, was.
        stopped.Probed("http://www.example.com/", FetchOutcome.Redirect, 301, redirectTo: "http://www.example.com/landing");
        Assert.False(Versions(stopped).CanCheck);

        // Where it leads off the versions, or home, decides nothing, fetched or not.
        stopped.Probed("http://www.example.com/", FetchOutcome.Redirect, 301, redirectTo: $"{stopped.Site}/new-home");
        Assert.True(Versions(stopped).CanCheck);

        // But a version itself is always asked, even one on the main version's origin: here the start URL leads to
        // www/en/, and the probe of www's / never answered.
        var moved = new RuleHarness();
        moved.Fetched($"{moved.Site}/", FetchOutcome.Redirect, 301, redirectTo: "https://www.example.com/en/");
        moved.Ok("https://www.example.com/en/");
        moved.Probed("http://example.com/", FetchOutcome.Redirect, 301, redirectTo: "https://www.example.com/en/");
        moved.Probed("http://www.example.com/", FetchOutcome.Redirect, 301, redirectTo: "https://www.example.com/en/");
        moved.Id("https://www.example.com/");
        Assert.False(Versions(moved).CanCheck);
        moved.Probed("https://www.example.com/", FetchOutcome.Redirect, 301, redirectTo: "https://www.example.com/en/");
        Assert.True(Versions(moved).CanCheck);
        Assert.Empty(Versions(moved).Finish());

        foreach (var (outcome, status) in new[] { (FetchOutcome.ServerError, 500), (FetchOutcome.RobotsDisallowed, 0), (FetchOutcome.Redirect, 301) })
        {
            var broken = new RuleHarness();
            broken.Fetched($"{broken.Site}/", outcome, status, redirectTo: outcome == FetchOutcome.Redirect ? "https://elsewhere.example/" : null);
            broken.Probed("https://www.example.com/", FetchOutcome.Success, 200);
            broken.Probed("http://example.com/", FetchOutcome.Success, 200);
            broken.Probed("http://www.example.com/", FetchOutcome.Success, 200);
            Assert.False(Versions(broken).CanCheck);
            Assert.Empty(Versions(broken).Finish());
        }
    }

    /// <summary>
    /// Probes aren't the site's URLs: a dead or failing version, or where one redirects, isn't a broken URL of the site.
    /// Once the site itself links there, it is.
    /// </summary>
    [Fact]
    public void ProbesOfTheSitesOtherVersionsAreNotItsBrokenUrls()
    {
        var site = new RuleHarness();
        site.Ok($"{site.Site}/");
        site.Probed("https://www.example.com/", FetchOutcome.NetworkError, 0);
        site.Probed("http://example.com/", FetchOutcome.Redirect, 301, redirectTo: "http://example.com/gone");
        site.Probed("http://example.com/gone", FetchOutcome.ClientError, 404);
        site.Probed("http://www.example.com/", FetchOutcome.RobotsDisallowed, 0);
        var linked = site.Fetched($"{site.Site}/down", FetchOutcome.ServerError, 503);

        var hits = new ResponseCodeRules(site.Table(), new Dictionary<int, string>()).Finish().ToList();

        Assert.Equal([new IssueHit(IssueCatalog.INTERNAL_SERVER_ERROR, linked, "503 Service Unavailable")], hits);
        Assert.Contains(new RedirectRules(site.Table()).Finish(), hit => hit.Issue == IssueCatalog.REDIRECT_BROKEN && hit.UrlId == site.Id("http://example.com/"));
    }

    /// <summary>A dead www twin's robots.txt fails on nearly every crawl; it counts only once the site links there.</summary>
    [Fact]
    public void RobotsTxtOfAnOriginOnlyTheProbesWentToIsIgnored()
    {
        var site = new RuleHarness();
        site.Ok($"{site.Site}/");
        var scope = new CrawlScope(new Uri($"{site.Site}/"), includeSubdomains: false, stayInStartFolder: false);
        HashSet<string> main = ["https://example.com"];
        RobotsRecord[] robots =
        [
            new("https://example.com", 200, "User-agent: *", null, 0),
            new("https://www.example.com", null, null, "Connection refused", 0),
            new("http://www.example.com", 404, null, null, 0),
            new("http://example.com", null, null, "Timed out", 0)
        ];

        var hits = new RobotsTxtRules(site.Table(), robots, scope, main, new HashSet<string> { "https://www.example.com", "http://www.example.com" }).Finish().ToList();

        Assert.Equal([new IssueHit(IssueCatalog.ROBOTS_TXT_UNREACHABLE_OTHER_ORIGIN, site.Id($"{site.Site}/"), "http://example.com/robots.txt (Timed out)")], hits);
    }

    private static SiteVersionRules Versions(RuleHarness site)
    {
        var urls = site.Table();
        return new SiteVersionRules(urls, new Uri($"{site.Site}/"), site.Find, site.UrlOf);
    }
}
