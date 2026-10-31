using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;
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
        Assert.Equal("HTTP 301 without a usable Location header", hits.Single(hit => hit.UrlId == dangling).Detail);
        Assert.Equal(4, hits.Count);
    }

    [Fact]
    public void RobotsTxtProblemsCountOnlyForTheSitesOwnOrigins()
    {
        var site = new RuleHarness();
        site.Ok($"{site.Site}/");
        var scope = new CrawlScope(new Uri($"{site.Site}/"), includeSubdomains: false, stayInStartFolder: false);
        RobotsRecord[] robots =
        [
            new("https://example.com", 503, null, null, 0),
            new("https://partner.example", 404, null, null, 0),
            new("https://cdn.example", null, null, "Timed out", 0)
        ];

        var hits = new RobotsTxtRules(site.Table(), robots, scope).Finish().ToList();

        var unreachable = Assert.Single(hits);
        Assert.Equal(IssueCatalog.ROBOTS_TXT_UNREACHABLE, unreachable.Issue);
        Assert.Equal("https://example.com/robots.txt (HTTP 503)", unreachable.Detail);

        // Google treats 429 like a server error; a redirect loop is handled as "no rules", as the crawler does.
        var limited = new RobotsTxtRules(site.Table(), [new("https://example.com", 429, null, null, 0)], scope).Finish().Single();
        var looping = new RobotsTxtRules(site.Table(), [new("https://example.com", null, null, "More than 5 redirects", 0)], scope).Finish().Single();
        Assert.Equal(IssueCatalog.ROBOTS_TXT_UNREACHABLE, limited.Issue);
        Assert.Equal(IssueCatalog.ROBOTS_TXT_MISSING, looping.Issue);
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
        Assert.Contains("sitemap-old.xml (HTTP 404)", hits.Single(hit => hit.Issue == IssueCatalog.SITEMAP_ERROR).Detail);
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
        var nearHits = hits.Where(hit => hit.Issue == IssueCatalog.NEAR_DUPLICATE_CONTENT).ToList();
        Assert.Contains(nearHits, hit => hit.UrlId == near.UrlId);
        Assert.DoesNotContain(nearHits, hit => hit.UrlId == other.UrlId);
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
        var canonicalRules = new CanonicalRules(urls);
        List<IssueHit> Hits(AnalyzedPage page) => canonicalRules.Check(page).ToList();

        Assert.Equal([(backHere.UrlId, IndexabilityReason.None), (toGone.UrlId, IndexabilityReason.None), (toGoneLater.UrlId, IndexabilityReason.None)],
            urls.IndexabilityChanges);
        Assert.Equal([IndexabilityReason.None, IndexabilityReason.None, IndexabilityReason.None, IndexabilityReason.Canonicalised, IndexabilityReason.Canonicalised],
            analysed.Select(page => page.IndexabilityReason));

        // The canonical is still worth fixing, but the page is audited like any indexable page.
        Assert.Equal([new IssueHit(IssueCatalog.CANONICAL_TO_REDIRECT, backHere.UrlId, "The canonical URL redirects back to this page", site.Id($"{site.Site}/page"))],
            Hits(analysed[0]));
        Assert.Equal([IssueCatalog.TITLE_TOO_SHORT], new TitleRules(new AnalysisSettings()).Check(analysed[0]).Select(hit => hit.Issue));
        Assert.Equal("The canonical URL returns HTTP 404", Hits(analysed[1]).Single().Detail);
        Assert.Equal("The canonical URL redirects to a URL that returns HTTP 404", Hits(analysed[2]).Single(hit => hit.Issue == IssueCatalog.CANONICAL_BROKEN).Detail);
        Assert.Equal([IssueCatalog.CANONICALISED_PAGE], Hits(analysed[3]).Select(hit => hit.Issue));
        Assert.Equal([IssueCatalog.CANONICALISED_PAGE, IssueCatalog.CANONICAL_TO_REDIRECT], Hits(analysed[4]).Select(hit => hit.Issue));
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
        Assert.Equal("The canonical URL redirects with an instant meta refresh",
            new CanonicalRules(urls).Check(site.Page($"{site.Site}/x", RuleHarness.Html($"<link rel=\"canonical\" href=\"{site.Site}/old\">"),
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
}
