using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>
/// The faults the audit of 2026-11-24 found by running every check on real sites (docs/v2-check-audit.md, "The third
/// audit"): each test is the markup or the answers a real site gave, cut down, and what the check now makes of it.
/// </summary>
public class RealSiteProofTests
{
    private static readonly AnalysisSettings SETTINGS = new();

    /// <summary>crawler-test.com: a stray &lt;link rel="stylesheet" href="#"&gt; names the page itself, which is no stylesheet sent as HTML.</summary>
    [Fact]
    public void AStylesheetLinkNamingThePageItselfIsNoMismatchedFile()
    {
        var site = new RuleHarness();
        var style = site.Fetched($"{site.Site}/real.css", FetchOutcome.Success, 200, isHtml: true, isParsed: false);
        var page = site.Page($"{site.Site}/page", RuleHarness.Html(body: "<link rel=\"stylesheet\" href=\"#\"><link rel=\"stylesheet\" href=\"/real.css\">"));
        var rule = new ResourceRules(site.Table(), _ => "text/html");
        rule.Observe(page);

        Assert.Equal([style], rule.Finish().Where(hit => hit.Issue == IssueCatalog.CONTENT_TYPE_MISMATCH).Select(hit => hit.UrlId));
    }

    /// <summary>httpbin.org/redirect/5 in a crawl of a list of URLs: nothing links to a listed URL, and its chain is still entered there.</summary>
    [Fact]
    public void AChainThatStartsAtAUrlOfTheCrawlsListIsReported()
    {
        var site = new RuleHarness();
        var listed = site.Fetched($"{site.Site}/redirect/3", FetchOutcome.Redirect, 302, redirectTo: $"{site.Site}/redirect/2", source: DiscoverySource.Seed);
        site.Fetched($"{site.Site}/redirect/2", FetchOutcome.Redirect, 302, redirectTo: $"{site.Site}/redirect/1");
        site.Fetched($"{site.Site}/redirect/1", FetchOutcome.Redirect, 302, redirectTo: $"{site.Site}/get");
        site.Ok($"{site.Site}/get");

        var chain = Assert.Single(new RedirectRules(site.Table(), site.UrlOf).Finish(), hit => hit.Issue == IssueCatalog.REDIRECT_CHAIN);

        Assert.Equal((listed, "3 redirects"), (chain.UrlId, chain.Detail));
    }

    /// <summary>crawler-test.com/javascript/window-location-external: a script sent the browser on, and the page has no tag to look for.</summary>
    [Fact]
    public void ARedirectByScriptIsCalledAScriptNotAMetaRefresh()
    {
        var site = new RuleHarness();
        var page = site.Page($"{site.Site}/moved", RuleHarness.Html());
        page = page with { Facts = page.Facts with { MetaRefresh = new MetaRefreshFact(0, "http://www.example.org/", FromScript: true) } };

        var hit = Assert.Single(new IndexabilityRules().Check(page), hit => hit.Issue == IssueCatalog.META_REFRESH);

        Assert.Equal("A script moves to http://www.example.org/ as the page loads", hit.Detail);
    }

    /// <summary>cnx-software.com: a heading's id written with lowercase percent escapes, and the link beside it written the same way.</summary>
    [Fact]
    public void AnIdWithLowercasePercentEscapesIsFoundByTheSameFragment()
    {
        const string ID = "%e0%b9%81%e0%b8%81";
        var site = new RuleHarness();
        var page = site.Page($"{site.Site}/review", RuleHarness.Html(body: $"<a href=\"#{ID}\">Unboxing</a><a href=\"#nowhere\">Nowhere</a><h2 id=\"{ID}\">Unboxing</h2>"));
        var rule = new FragmentRules(site.Table(), _ => null, () => [], site.UrlOf);
        rule.Observe(page);

        var hit = Assert.Single(rule.Finish());

        Assert.Equal("1 link to missing sections: #nowhere", hit.Detail);
        Assert.False(FragmentRules.Misses(ID.ToUpperInvariant(), [ID]));
    }

    /// <summary>Wix's product pages: a &lt;div href&gt; holding the full-size photo for the gallery's script. No page is lost with it.</summary>
    [Fact]
    public void AnHrefToAnImageFileOnADivIsNoUncrawlableLink()
    {
        var site = new RuleHarness();
        var page = site.Page($"{site.Site}/product", RuleHarness.Html(body:
            "<div class=\"media-wrapper-hook\" href=\"https://static.wixstatic.com/media/77ef67_a.jpg/v1/fill/w_1000,h_668,q_85/77ef67_a.jpg\"><img src=\"/a.jpg\" alt=\"A box\" width=\"1\" height=\"1\"></div>" +
            "<span href=\"/about\">About us</span>"));

        Assert.Equal(1, page.Facts.UncrawlableLinks!.Count);
        Assert.Equal("<span href>", Assert.Single(page.Facts.UncrawlableLinks.Examples).Why);
    }

    /// <summary>videolan.org "More →", softmaker.com "Read more …", oo-software.com "Learn more →": generic text with a space before its symbol.</summary>
    [Theory]
    [InlineData("More →")]
    [InlineData("Read more …")]
    [InlineData("Learn more  →")]
    [InlineData("» Click here «")]
    public void GenericLinkTextWithASpaceBeforeItsSymbolIsStillGeneric(string text) =>
        Assert.True(LinkStructureRules.IsGeneric(text, LinkFlags.None));

    /// <summary>
    /// omgubuntu.co.uk/?amp: an AMP copy of page 1 names the series' page 2 as rel="next" and links the AMP copy of page 2;
    /// page 2 points back to page 1, the AMP page's canonical. The series is in order.
    /// </summary>
    [Fact]
    public void AnAmpCopysPaginationIsJudgedByThePageItIsACopyOf()
    {
        var site = new RuleHarness();
        var first = site.Page($"{site.Site}/", RuleHarness.Html("<link rel=\"next\" href=\"/page/2\">", "<a href=\"/page/2\">Older</a>"));
        var second = site.Page($"{site.Site}/page/2", RuleHarness.Html("<link rel=\"prev\" href=\"/\">", "<a href=\"/\">Newer</a>"));
        var amp = site.Page($"{site.Site}/?amp", RuleHarness.Html("<link rel=\"canonical\" href=\"/\"><link rel=\"next\" href=\"/page/2\">", "<a href=\"/page/2?amp\">Older</a>"),
            IndexabilityReason.Canonicalised);
        var secondAmp = site.Page($"{site.Site}/page/2?amp", RuleHarness.Html("<link rel=\"canonical\" href=\"/page/2\"><link rel=\"prev\" href=\"/\">", "<a href=\"/?amp\">Newer</a>"),
            IndexabilityReason.Canonicalised);
        var urls = site.Table(canonicals: [(amp.UrlId, first.UrlId), (secondAmp.UrlId, second.UrlId)]);
        var rule = new PaginationRules(urls, site.Find, site.UrlOf);
        Array.ForEach([first, second, amp, secondAmp], rule.Observe);

        Assert.DoesNotContain(rule.Finish(), hit => hit.Issue == IssueCatalog.PAGINATION_SEQUENCE_ERROR || hit.Issue == IssueCatalog.PAGINATION_NOT_LINKED);
    }

    /// <summary>A copy's canonical doesn't excuse a next page that points back somewhere else.</summary>
    [Fact]
    public void ANextPageThatPointsBackToNeitherThePageNorItsCanonicalIsStillOutOfOrder()
    {
        var site = new RuleHarness();
        var first = site.Page($"{site.Site}/", RuleHarness.Html());
        var copy = site.Page($"{site.Site}/?amp", RuleHarness.Html("<link rel=\"next\" href=\"/page/2\">", "<a href=\"/page/2\">Older</a>"), IndexabilityReason.Canonicalised);
        var second = site.Page($"{site.Site}/page/2", RuleHarness.Html("<link rel=\"prev\" href=\"/elsewhere\">"));
        var rule = new PaginationRules(site.Table(canonicals: [(copy.UrlId, first.UrlId)]), site.Find, site.UrlOf);
        Array.ForEach([first, copy, second], rule.Observe);

        Assert.Equal("/page/2 doesn't point back", rule.Finish().Single(hit => hit.Issue == IssueCatalog.PAGINATION_SEQUENCE_ERROR && hit.UrlId == copy.UrlId).Detail);
    }

    /// <summary>
    /// theregister.com: robots.txt turns every crawler away (User-agent: *, Disallow: /) and lets Googlebot in. Its sitemap's
    /// pages were refused to this crawl, not to Google, so they aren't called non-indexable. A URL Googlebot is refused too still is.
    /// </summary>
    [Fact]
    public void ASitemapUrlRobotsTxtRefusesTheCrawlerButAllowsGooglebotIsNotCalledNonIndexable()
    {
        const string ROBOTS = "User-agent: *\nAllow: /ads.txt\nDisallow: /\n\nUser-agent: Googlebot\nAllow: /\nDisallow: /private/\n";
        var site = new RuleHarness();
        site.Ok($"{site.Site}/");
        site.Fetched($"{site.Site}/article", FetchOutcome.RobotsDisallowed, 0, inSitemap: true);
        var hidden = site.Fetched($"{site.Site}/private/draft", FetchOutcome.RobotsDisallowed, 0, inSitemap: true);
        var googlebot = new GooglebotAccess([new RobotsRecord(site.Site, 200, ROBOTS, null, 0)], site.UrlOf);
        StoredSitemap[] sitemaps = [new($"{site.Site}/sitemap.xml", 200, false, 2, null)];

        var hits = new SitemapRules(site.Table(), sitemaps, new CrawlOptions { StartUrl = $"{site.Site}/" }, crawlComplete: true, googlebot: googlebot).Finish()
            .Where(hit => hit.Issue == IssueCatalog.SITEMAP_NON_INDEXABLE).ToList();

        Assert.Equal([hidden], hits.Select(hit => hit.UrlId));
        Assert.Equal("The URL is blocked by robots.txt", hits[0].Detail);
    }

    /// <summary>
    /// w3.org: a firewall's challenge answered every URL, /sitemap.xml included, with 403: whether the site has a sitemap wasn't
    /// learnt. A guess that timed out or got a server error says as little. A plain 404 on a site that was read says there is none.
    /// </summary>
    [Theory]
    [InlineData(404, true, true)]
    [InlineData(403, true, true)]
    [InlineData(403, false, false)]
    [InlineData(503, true, false)]
    [InlineData(429, true, false)]
    [InlineData(null, true, false)]
    public void NoSitemapIsReportedOnlyWhenTheGuessAtItWasAnswered(int? status, bool homeWasRead, bool reported)
    {
        var site = new RuleHarness();
        if (homeWasRead)
        {
            site.Ok($"{site.Site}/");
        }
        else
        {
            site.Fetched($"{site.Site}/", FetchOutcome.Blocked, 403);
        }

        StoredSitemap[] sitemaps = [new($"{site.Site}/sitemap.xml", status, false, 0, status is null ? "Timed out" : $"HTTP {status}", IsProbe: true)];

        var hits = new SitemapRules(site.Table(), sitemaps, new CrawlOptions { StartUrl = $"{site.Site}/" }, crawlComplete: true).Finish();

        Assert.Equal(reported, hits.Any(hit => hit.Issue == IssueCatalog.SITEMAP_MISSING));
    }

    /// <summary>crawler-test.com's two canonicals: both URLs have to be read in the detail, so one on the page's host is its path.</summary>
    [Fact]
    public void ConflictingCanonicalsAreBothNamedInFull()
    {
        var site = new RuleHarness("https://crawler-test.example");
        var page = site.Page($"{site.Site}/canonical_tags/canonical_tag_html_header_conflict",
            RuleHarness.Html("<link rel=\"canonical\" href=\"https://crawler-test.example/canonical_tags/html\">"),
            headers: [new ResponseHeader("link", "<https://crawler-test.example/canonical_tags/header>; rel=\"canonical\"")]);

        var hit = Assert.Single(new CanonicalRules(site.Table(), site.UrlOf).Check(page), hit => hit.Issue == IssueCatalog.CANONICAL_MULTIPLE);

        Assert.Equal("2 URLs: /canonical_tags/html (head) · /canonical_tags/header (HTTP header)", hit.Detail);
    }

    /// <summary>
    /// wordpress.org/news/category/events/: a crawl that stopped at its URL limit read pages 2 and 9 of one archive and none
    /// between. They share the archive's H1 as pages of one series do, though no rel=next/prev joins them.
    /// </summary>
    [Fact]
    public void PagesOfOneNumberedSeriesReadWithAGapDontDuplicateEachOthersH1()
    {
        var site = new RuleHarness();
        AnalyzedPage Page(string path, string head, string h1 = "Events") =>
            site.Page($"{site.Site}{path}", RuleHarness.Html($"<title>{path}</title>{head}", $"<h1>{h1}</h1>"));
        var second = Page("/category/events/page/2/", "<link rel=\"prev\" href=\"/category/events/\"><link rel=\"next\" href=\"/category/events/page/3/\">");
        var ninth = Page("/category/events/page/9/", "<link rel=\"prev\" href=\"/category/events/page/8/\"><link rel=\"next\" href=\"/category/events/page/10/\">");
        var other = Page("/category/releases/page/2/", "<link rel=\"prev\" href=\"/category/releases/\">", "Releases");
        var rules = new DuplicateRules(SETTINGS);
        Array.ForEach([second, ninth, other], rules.Observe);

        Assert.DoesNotContain(rules.Finish(), hit => hit.Issue == IssueCatalog.H1_DUPLICATE);

        // Another archive's page with the same heading is still a duplicate of them.
        var same = Page("/category/meetups/page/2/", "<link rel=\"prev\" href=\"/category/meetups/\">");
        rules = new DuplicateRules(SETTINGS);
        Array.ForEach([second, ninth, same], rules.Observe);
        Assert.Equal([second.UrlId, ninth.UrlId, same.UrlId], rules.Finish().Where(hit => hit.Issue == IssueCatalog.H1_DUPLICATE).Select(hit => hit.UrlId).Order());
    }

    /// <summary>
    /// gov.uk: 25,443 URLs only its sitemaps list (/government/publications/x/x, an attachment under its publication's name)
    /// have no link to fix. info.cern.ch's /hypertext/WWW/hypertext/WWW/, built by a relative link on a page, has.
    /// </summary>
    [Fact]
    public void ARepeatingPathIsJudgedOnlyWhereAPageLinksToIt()
    {
        var site = new RuleHarness();
        var listed = site.Fetched($"{site.Site}/publications/annual-report/annual-report", FetchOutcome.Success, 200, isHtml: true, inSitemap: true);
        var page = site.Page($"{site.Site}/", RuleHarness.Html(body: "<a href=\"/hypertext/WWW/hypertext/WWW/Addressing.html\">Addressing</a>"));
        var linked = site.Find($"{site.Site}/hypertext/WWW/hypertext/WWW/Addressing.html");
        var urls = site.Table();
        urls.CountInlinks(page);

        var hits = new UrlTextRules(urls, [(listed, site.UrlOf(listed)!), (linked, site.UrlOf(linked)!)]).Finish();

        Assert.Equal([linked], hits.Where(hit => hit.Issue == IssueCatalog.URL_REPETITIVE_PATH).Select(hit => hit.UrlId));
    }

    /// <summary>
    /// kilmingtonvillage.com (Weebly): every picture on the page is an og:image, the page's banner first. Networks show the
    /// first, so a small one further down isn't what's shared.
    /// </summary>
    [Fact]
    public void OnlyTheFirstShareImageOfEachTagIsMeasured()
    {
        var site = new RuleHarness();
        site.Fetched($"{site.Site}/banner.jpg", FetchOutcome.Success, 200, contentType: "image/jpeg", size: 90_000, pixelWidth: 2_461, pixelHeight: 591);
        var logo = site.Fetched($"{site.Site}/logo.jpg", FetchOutcome.Success, 200, contentType: "image/jpeg", size: 6_000_000, pixelWidth: 364, pixelHeight: 54);
        string Images(params string[] paths) => string.Concat(paths.Select(path => $"<meta property=\"og:image\" content=\"{site.Site}{path}\">"));
        var bannerFirst = site.Page($"{site.Site}/", RuleHarness.Html(Images("/banner.jpg", "/logo.jpg")));
        var logoFirst = site.Page($"{site.Site}/hall", RuleHarness.Html(Images("/logo.jpg", "/banner.jpg")));
        var rules = new SocialRules(site.Table());

        Assert.DoesNotContain(rules.Check(bannerFirst), hit => hit.Issue == IssueCatalog.SOCIAL_IMAGE_TOO_SMALL || hit.Issue == IssueCatalog.SOCIAL_IMAGE_TOO_LARGE);
        var small = Assert.Single(rules.Check(logoFirst), hit => hit.Issue == IssueCatalog.SOCIAL_IMAGE_TOO_SMALL);
        Assert.Equal(("og:image is 364 × 54 px, under 200 × 200", logo), (small.Detail, small.RelatedUrlId));
        Assert.Single(rules.Check(logoFirst), hit => hit.Issue == IssueCatalog.SOCIAL_IMAGE_TOO_LARGE);
    }

    /// <summary>
    /// cnx-software.com: a &lt;div&gt; in the head put fifteen tags in the body. The detail was cut before it named the div, and
    /// its 97 pages fell into five groups by where the cut landed.
    /// </summary>
    [Fact]
    public void TheCauseOfHeadTagsInTheBodySurvivesALongListOfTags()
    {
        var head = "<div id=\"fluid-privacy-opt-out-link\" style=\"display:none\"></div><meta name=\"robots\" content=\"index\"><title>News</title>" +
                   "<meta name=\"description\" content=\"News\"><link rel=\"alternate\" hreflang=\"en\" href=\"/\"><link rel=\"amphtml\" href=\"/?amp\">" +
                   string.Concat(Enumerable.Range(0, 10).Select(index => $"<meta property=\"og:x{index}\" content=\"x\">")) +
                   "<meta name=\"twitter:card\" content=\"summary\"><meta name=\"twitter:site\" content=\"@x\">";
        var site = new RuleHarness();
        var page = site.Page($"{site.Site}/news", $"<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">{head}</head><body><p>News</p></body></html>");

        var detail = Assert.Single(new HtmlRules().Check(page), hit => hit.Issue == IssueCatalog.HEAD_TAGS_IN_BODY).Detail!;

        Assert.EndsWith("on line 1 ended the head early", detail);
        Assert.StartsWith("meta robots, title", detail);
        Assert.True(detail.Length <= RuleText.DETAIL_MAX);
        Assert.Equal("<div id=\"fluid-privacy-opt-out-link\" st… ended the head early", IssueFaults.HeadEndCause(detail));
    }

    /// <summary>debian.org/doc/debian-policy/: initial-scale=1 and initial-scale=1.0 are one setting.</summary>
    [Theory]
    [InlineData("width=device-width, initial-scale=1", "width=device-width, initial-scale=1.0", false)]
    [InlineData("width=device-width, initial-scale=1", "initial-scale=1,width=device-width", false)]
    [InlineData("width=device-width, initial-scale=1", "width=1024", true)]
    public void TwoViewportTagsDifferOnlyWhenTheirSettingsDo(string first, string second, bool differ)
    {
        var site = new RuleHarness();
        var page = site.Page($"{site.Site}/policy", RuleHarness.Html($"<meta name=\"viewport\" content=\"{first}\"><meta name=\"viewport\" content=\"{second}\">"));

        Assert.Equal(differ, new HtmlRules().Check(page).Any(hit => hit.Issue == IssueCatalog.VIEWPORT_INVALID));
    }

    /// <summary>omgubuntu.co.uk/?amp: the size is on the &lt;amp-img&gt;, which keeps the space; the &lt;img&gt; the AMP optimiser writes inside has none.</summary>
    [Fact]
    public void AnAmpImageIsSizedByItsAmpImg()
    {
        var site = new RuleHarness();
        var page = site.Page($"{site.Site}/?amp", RuleHarness.Html(body:
            "<amp-img width=\"406\" height=\"232\" src=\"/a.webp\" alt=\"A browser\"><img class=\"i-amphtml-fill-content\" decoding=\"async\" alt=\"A browser\" src=\"/a.webp\"></amp-img>" +
            "<img src=\"/b.webp\" alt=\"Unsized\">"));

        var hit = Assert.Single(new ImageRules(SETTINGS, site.Table(), site.Find).Check(page), hit => hit.Issue == IssueCatalog.IMAGE_MISSING_DIMENSIONS);

        Assert.Equal([site.Find($"{site.Site}/b.webp")], hit.AllTargets);
    }

    /// <summary>
    /// mozilla.org: every page's x-default names the home page, which answers 302 to the visitor's language: what x-default
    /// is for. softmaker.com's x-default names an old http:// address that redirects for good: an entry to correct.
    /// </summary>
    [Theory]
    [InlineData(302, false)]
    [InlineData(301, true)]
    public void AnXDefaultThatRedirectsEachVisitorToTheirLanguageIsNoFault(int status, bool flagged)
    {
        var site = new RuleHarness();
        site.Fetched($"{site.Site}/", FetchOutcome.Redirect, status, redirectTo: $"{site.Site}/en-US/");
        site.Ok($"{site.Site}/en-US/");
        var page = site.Page($"{site.Site}/hu/", RuleHarness.Html(
            "<link rel=\"alternate\" hreflang=\"hu\" href=\"/hu/\"><link rel=\"alternate\" hreflang=\"en-US\" href=\"/en-US/\"><link rel=\"alternate\" hreflang=\"x-default\" href=\"/\">",
            lang: " lang=\"hu\""));

        var hits = new HreflangRules(site.Table(start: $"{site.Site}/hu/"), site.UrlOf).Check(page);

        Assert.Equal(flagged, hits.Any(hit => hit.Issue == IssueCatalog.HREFLANG_TO_NON_INDEXABLE));
    }

    /// <summary>A language's own entry that redirects is still at fault, whatever the redirect.</summary>
    [Fact]
    public void ALanguagesEntryThatRedirectsTemporarilyIsStillNonIndexable()
    {
        var site = new RuleHarness();
        site.Fetched($"{site.Site}/fr", FetchOutcome.Redirect, 302, redirectTo: $"{site.Site}/fr/");
        var page = site.Page($"{site.Site}/hu/", RuleHarness.Html(
            "<link rel=\"alternate\" hreflang=\"hu\" href=\"/hu/\"><link rel=\"alternate\" hreflang=\"fr\" href=\"/fr\">", lang: " lang=\"hu\""));

        Assert.Contains(new HreflangRules(site.Table(start: $"{site.Site}/hu/"), site.UrlOf).Check(page), hit => hit.Issue == IssueCatalog.HREFLANG_TO_NON_INDEXABLE);
    }

    /// <summary>waldenfloral.com (Wix): an empty {} block beside the page's real one. Nothing is in it to be wrong.</summary>
    [Fact]
    public void AnEmptyJsonLdBlockHasNoSchemaFault()
    {
        var site = new RuleHarness();
        var page = site.Page($"{site.Site}/cities/florist", RuleHarness.Html(
            "<script type=\"application/ld+json\">{\"@context\":\"https://schema.org/\",\"@type\":\"ImageObject\",\"contentUrl\":\"https://example.com/a.jpg\"}</script>" +
            "<script type=\"application/ld+json\">{}</script>"));

        Assert.DoesNotContain(new StructuredDataRules().Check(page), hit => hit.Issue == IssueCatalog.STRUCTURED_DATA_SCHEMA_ERRORS);
    }

    /// <summary>brooklyncandlestudio.com: "offers": [ ], then the block's end. The parser's advice to a programmer isn't the owner's.</summary>
    [Fact]
    public void AJsonSyntaxErrorIsWordedForTheSitesOwner()
    {
        var site = new RuleHarness();
        var page = site.Page($"{site.Site}/blogs/press", RuleHarness.Html(
            "<script type=\"application/ld+json\">{\n\"@context\": \"https://schema.org\",\n\"@type\": \"Product\",\n\"name\": \"Candle\",\n\"offers\": [ ],\n}</script>"));

        var detail = Assert.Single(new StructuredDataRules().Check(page), hit => hit.Issue == IssueCatalog.STRUCTURED_DATA_INVALID).Detail!;

        Assert.Contains("contains a trailing comma at the end", detail);
        Assert.DoesNotContain("reader options", detail);
        Assert.DoesNotContain("this mode", detail);
    }

    /// <summary>
    /// cnx-software.com: &lt;meta name="referrer"&gt; sets the page's referrer policy, and a meta Content-Security-Policy its
    /// policy, where no header does. Only a header can set frame-ancestors, so X-Frame-Options is still missing.
    /// </summary>
    [Fact]
    public void APolicySetInAMetaTagIsNotAMissingHeader()
    {
        var site = new RuleHarness();
        ResponseHeader[] headers = [new("strict-transport-security", "max-age=31536000"), new("x-content-type-options", "nosniff")];
        var bare = site.Page($"{site.Site}/", RuleHarness.Html(), headers: headers);
        var urls = site.Table();
        Assert.Equal("Missing Content-Security-Policy, X-Frame-Options, Referrer-Policy", Detail(bare));

        var withMeta = site.Page($"{site.Site}/", RuleHarness.Html(
            "<meta name=\"referrer\" content=\"always\"><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'self'; frame-ancestors 'none'\">"), headers: headers);
        Assert.Equal("Missing X-Frame-Options", Detail(withMeta));

        string? Detail(AnalyzedPage page) =>
            new SecurityRules(urls).Check(page).SingleOrDefault(hit => hit.Issue == IssueCatalog.SECURITY_HEADERS_MISSING).Detail;
    }

    /// <summary>A meta Content-Security-Policy with upgrade-insecure-requests has the browser ask for each http:// file over HTTPS, as the header's does.</summary>
    [Fact]
    public void UpgradeInsecureRequestsInAMetaTagLeavesNoMixedContent()
    {
        var site = new RuleHarness();
        site.Fetched("http://cdn.example.net/a.js", FetchOutcome.Success, 200, contentType: "text/javascript");
        var plain = site.Page($"{site.Site}/a", RuleHarness.Html(body: "<script src=\"http://cdn.example.net/a.js\"></script>"));
        var upgraded = site.Page($"{site.Site}/b", RuleHarness.Html("<meta http-equiv=\"Content-Security-Policy\" content=\"upgrade-insecure-requests\">",
            "<script src=\"http://cdn.example.net/a.js\"></script>"));
        var rules = new SecurityRules(site.Table());

        Assert.Contains(rules.Check(plain), hit => hit.Issue == IssueCatalog.MIXED_CONTENT);
        Assert.DoesNotContain(rules.Check(upgraded), hit => hit.Issue == IssueCatalog.MIXED_CONTENT);
    }

    /// <summary>http-password.badssl.com: a password field with no form round it, on an HTTP page.</summary>
    [Fact]
    public void APasswordFieldOutsideAnyFormOnAnHttpPageIsNotSecure()
    {
        var site = new RuleHarness("http://example.com");
        var page = site.Page($"{site.Site}/login", RuleHarness.Html(body: "<input type=\"password\" id=\"value\" placeholder=\"password\"><input type=\"text\">"));

        var hit = Assert.Single(new SecurityRules(site.Table()).Check(page), hit => hit.Issue == IssueCatalog.FORM_NOT_SECURE);

        Assert.Equal("A password form on an http:// page", hit.Detail);
    }

    /// <summary>waldenfloral.com: 2,097,400 bytes read "2.0 MB, past the 2 MB Google reads". A size that rounds to its limit is given to the byte.</summary>
    [Theory]
    [InlineData(2_097_400, "2,097,400 bytes, past the 2 MB Google reads")]
    [InlineData(2_253_575, "2.1 MB, past the 2 MB Google reads")]
    public void ASizeJustPastItsLimitIsGivenToTheByte(long bytes, string detail)
    {
        var site = new RuleHarness();
        var page = site.Page($"{site.Site}/product", RuleHarness.Html(), bodyBytes: bytes);

        Assert.Equal(detail, new PerformanceRules(SETTINGS).Check(page).Single(hit => hit.Issue == IssueCatalog.HTML_PAST_CRAWL_LIMIT).Detail);
    }

    /// <summary>
    /// brooklyncandlestudio.com's country picker (670 words of option text on every page), debian.org's forms and
    /// wordpress.org's embed-code boxes: a form control's text is none of the page's words.
    /// </summary>
    [Fact]
    public void AFormControlsTextIsNotThePagesWords()
    {
        var site = new RuleHarness();
        var options = string.Concat(Enumerable.Range(0, 200).Select(index => $"<option value=\"{index}\">Institutional Organisation Territory {index} (EUR €)</option>"));
        var page = site.Page($"{site.Site}/cart", RuleHarness.Html(body:
            $"<p>Your cart is empty. Have a look round the shop.</p><form><select name=\"country\">{options}</select>" +
            "<textarea>&lt;blockquote class=\"embed\"&gt;systematically underestimate considerable organisational complexity.&lt;/blockquote&gt;</textarea></form>"));

        Assert.Equal(10, page.Facts.WordCount);
        Assert.Equal(10, page.Facts.TotalWordCount);
        Assert.DoesNotContain(new TextStructureRules(SETTINGS).Check(page), hit => hit.Issue == IssueCatalog.READABILITY_LOW);

        // Shopify's picker is a form of buttons, not a <select>: the same control, written out.
        var buttons = string.Concat(Enumerable.Range(0, 200).Select(index => $"<li><button type=\"submit\" name=\"country_code\"><span>Institutional Organisation Territory {index} (EUR €)</span></button></li>"));
        var shop = site.Page($"{site.Site}/pages/contact", RuleHarness.Html(body:
            $"<p>Your cart is empty. Have a look round the shop.</p><form method=\"post\" action=\"/localization\" class=\"shopify-localization-form\"><ul role=\"list\">{buttons}</ul></form>"));
        Assert.Equal(10, shop.Facts.WordCount);
        Assert.Equal(10, shop.Facts.TotalWordCount);
    }

    /// <summary>
    /// kingarthurbaking.com (&lt;ul role="navigation"&gt; in the &lt;header&gt;), carrabelle.org (Max Mega Menu) and
    /// kilmingtonvillage.com (Weebly's wsite-menu): a header's menu written without &lt;nav&gt; is the template's, not the page's words.
    /// </summary>
    [Theory]
    [InlineData("<ul id=\"mobile-nav\" role=\"navigation\">")]
    [InlineData("<ul class=\"mnav-l1\" role=\"navigation\">")]
    [InlineData("<ul class=\"mega-menu max-mega-menu\">")]
    [InlineData("<ul class=\"wsite-menu-default\">")]
    public void AHeadersMenuWithoutANavElementIsNotThePagesWords(string menu)
    {
        var site = new RuleHarness();
        var items = string.Concat(Enumerable.Range(0, 60).Select(index => $"<li><a href=\"/c/{index}\">Category number {index}</a></li>"));
        var page = site.Page($"{site.Site}/returns", "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Returns</title></head><body>" +
                                                     $"<header><p>King of flour</p>{menu}{items}</ul></header><main><h1>Returns</h1><p>Send it back within thirty days.</p></main></body></html>");

        // The header's own line, the heading and the sentence.
        Assert.Equal(10, page.Facts.WordCount);
        Assert.All(page.Links.Where(link => link.Kind == LinkKind.Hyperlink), link => Assert.Equal(LinkPosition.Navigation, link.Position));
    }

    /// <summary>oo-software.com: a score of 29.72 read "Reading ease 30, under 30".</summary>
    [Fact]
    public void AReadingEaseThatRoundsToTheLimitIsShownToATenth()
    {
        Assert.Equal("29.7", TextStructureRules.Shown(29.72, 30));
        Assert.Equal("29.9", TextStructureRules.Shown(29.97, 30));
        Assert.Equal("29", TextStructureRules.Shown(29.4, 30));
        Assert.Equal("-23", TextStructureRules.Shown(-23.2, 30));
    }
}
