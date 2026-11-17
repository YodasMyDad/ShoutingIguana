using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>
/// The core condition of each rule, both ways: every test has a case the rule must flag and one it must leave alone,
/// so a rule whose condition is inverted or removed fails here.
/// </summary>
public sealed class RuleConditionTests
{
    private static readonly AnalysisSettings SETTINGS = new();

    [Fact]
    public void DescriptionsMustExistOnceAndFitTheLimits()
    {
        var site = new RuleHarness();
        var rules = new DescriptionRules(SETTINGS);
        string Meta(string content) => $"<meta name=\"description\" content=\"{content}\">";
        var good = new string('d', 100);

        Assert.Empty(Issues(rules, site.Page($"{site.Site}/a", RuleHarness.Html(Meta(good)))));
        Assert.Equal([IssueCatalog.DESCRIPTION_MISSING], Issues(rules, site.Page($"{site.Site}/b", RuleHarness.Html())));
        Assert.Equal([IssueCatalog.DESCRIPTION_MISSING], Issues(rules, site.Page($"{site.Site}/c", RuleHarness.Html(Meta(" ")))));
        Assert.Equal([IssueCatalog.DESCRIPTION_MULTIPLE], Issues(rules, site.Page($"{site.Site}/d", RuleHarness.Html(Meta(good) + Meta(good)))));
        // A d is 1,139 units, 7.79 px at 14 px: 127 of them are 989 px, over 985, and 126 are 981; 51 are 397 px, under 400, and 52 are 405.
        Assert.Equal([IssueCatalog.DESCRIPTION_TOO_LONG], Issues(rules, site.Page($"{site.Site}/e", RuleHarness.Html(Meta(new string('d', 127))))));
        Assert.Empty(Issues(rules, site.Page($"{site.Site}/f", RuleHarness.Html(Meta(new string('d', 126))))));
        Assert.Equal([IssueCatalog.DESCRIPTION_TOO_SHORT], Issues(rules, site.Page($"{site.Site}/g", RuleHarness.Html(Meta(new string('d', 51))))));
        Assert.Empty(Issues(rules, site.Page($"{site.Site}/h", RuleHarness.Html(Meta(new string('d', 52))))));
        Assert.Empty(Issues(rules, site.Page($"{site.Site}/i", RuleHarness.Html(), IndexabilityReason.Noindex)));
    }

    [Fact]
    public void PagesNeedOneH1WithText()
    {
        var site = new RuleHarness();
        var rules = new HeadingRules();

        Assert.Empty(Issues(rules, site.Page($"{site.Site}/a", RuleHarness.Html(body: "<h1>Title</h1><h2>Section</h2>"))));
        Assert.Equal([IssueCatalog.H1_MISSING], Issues(rules, site.Page($"{site.Site}/b", RuleHarness.Html(body: "<h2>Only a section</h2>"))));
        Assert.Equal("The H1 has no text", new HeadingRules().Check(site.Page($"{site.Site}/c", RuleHarness.Html(body: "<h1> </h1>"))).Single().Detail);
        Assert.Equal([IssueCatalog.H1_MULTIPLE], Issues(rules, site.Page($"{site.Site}/d", RuleHarness.Html(body: "<h1>One</h1><h1>Two</h1>"))));
        Assert.Empty(Issues(rules, site.Page($"{site.Site}/e", RuleHarness.Html(), IndexabilityReason.Noindex)));
    }

    [Fact]
    public void DocumentBasicsAndOpenGraphTags()
    {
        var site = new RuleHarness();
        var rules = new HtmlRules();
        const string VIEWPORT = "<meta name=\"viewport\" content=\"width=device-width\">";
        const string OPEN_GRAPH = "<meta property=\"og:title\" content=\"T\"><meta property=\"og:description\" content=\"D\"><meta property=\"og:image\" content=\"/i.jpg\">";

        Assert.Empty(Issues(rules, site.Page($"{site.Site}/a", RuleHarness.Html(VIEWPORT + OPEN_GRAPH))));
        Assert.Equal([IssueCatalog.VIEWPORT_MISSING], Issues(rules, site.Page($"{site.Site}/b", RuleHarness.Html(OPEN_GRAPH))));
        Assert.Equal([IssueCatalog.LANG_MISSING], Issues(rules, site.Page($"{site.Site}/c", RuleHarness.Html(VIEWPORT + OPEN_GRAPH, lang: " lang=\" \""))));
        Assert.Equal("Missing og:description, og:image",
            rules.Check(site.Page($"{site.Site}/d", RuleHarness.Html(VIEWPORT + "<meta property=\"og:title\" content=\"T\">"))).Single().Detail);

        // A tag that's there with nothing in it is told apart from one that's missing.
        Assert.Equal("Missing og:image; empty og:title", rules.Check(site.Page($"{site.Site}/f", RuleHarness.Html(
            VIEWPORT + "<meta property=\"og:title\" content=\" \"><meta property=\"og:description\" content=\"D\">"))).Single().Detail);
        Assert.Equal("Empty og:description", rules.Check(site.Page($"{site.Site}/g", RuleHarness.Html(
            VIEWPORT + "<meta property=\"og:title\" content=\"T\"><meta property=\"og:description\" content=\"\"><meta property=\"og:image\" content=\"/i.jpg\">"))).Single().Detail);

        // Open Graph only matters for pages that can be shared from search; the document basics apply everywhere.
        Assert.Equal([IssueCatalog.VIEWPORT_MISSING], Issues(rules, site.Page($"{site.Site}/e", RuleHarness.Html(), IndexabilityReason.Noindex)));
    }

    [Fact]
    public void ASlowPageSaysHowLongItWaitedForTheFirstByte()
    {
        var site = new RuleHarness();
        var rules = new PerformanceRules(SETTINGS);
        var slow = site.Page($"{site.Site}/slow", RuleHarness.Html(), responseMs: 5_512) with { TimeToFirstByteMs = 5_505 };

        Assert.Equal([new IssueHit(IssueCatalog.SLOW_RESPONSE, slow.UrlId, "5,512 ms, over 1,000 ms (first byte after 5,505 ms)")], rules.Check(slow));
        Assert.Empty(rules.Check(site.Page($"{site.Site}/fast", RuleHarness.Html(), responseMs: SETTINGS.SlowResponseMs)));
    }

    [Fact]
    public void HtmlPastGooglesCrawlLimitIsSeparateFromLargeHtml()
    {
        var site = new RuleHarness();
        var rules = new PerformanceRules(SETTINGS);
        const long limit = PerformanceRules.GOOGLEBOT_HTML_BYTES;

        Assert.Equal(2 * 1024 * 1024, limit);
        Assert.Equal(IssueSeverity.Warning, IssueCatalog.HTML_PAST_CRAWL_LIMIT.Severity);

        var over = site.Page($"{site.Site}/over", RuleHarness.Html(), bodyBytes: limit + 1);
        Assert.Equal([IssueCatalog.LARGE_PAGE, IssueCatalog.HTML_PAST_CRAWL_LIMIT], rules.Check(over).Select(hit => hit.Issue));

        var atTheLimit = site.Page($"{site.Site}/at", RuleHarness.Html(), bodyBytes: limit);
        Assert.Equal([IssueCatalog.LARGE_PAGE], rules.Check(atTheLimit).Select(hit => hit.Issue));

        var largeOnly = site.Page($"{site.Site}/large", RuleHarness.Html(), bodyBytes: SETTINGS.LargePageBytes + 1);
        Assert.Equal([IssueCatalog.LARGE_PAGE], rules.Check(largeOnly).Select(hit => hit.Issue));
    }

    [Fact]
    public void RobotsDirectivesAndMetaRefresh()
    {
        var site = new RuleHarness();
        var rules = new IndexabilityRules();
        string Robots(string content) => $"<meta name=\"robots\" content=\"{content}\">";

        Assert.Empty(Issues(rules, site.Page($"{site.Site}/a", RuleHarness.Html(Robots("index, follow")))));
        Assert.Equal([IssueCatalog.NOINDEX_PAGE], Issues(rules, site.Page($"{site.Site}/b", RuleHarness.Html(Robots("noindex")), IndexabilityReason.Noindex)));
        Assert.Equal([IssueCatalog.NOFOLLOW_PAGE], Issues(rules, site.Page($"{site.Site}/c", RuleHarness.Html(Robots("nofollow")))));
        Assert.Equal([IssueCatalog.NOINDEX_PAGE, IssueCatalog.NOFOLLOW_PAGE], Issues(rules, site.Page($"{site.Site}/d", RuleHarness.Html(Robots("none")), IndexabilityReason.Noindex)));

        // A snippet directive on a page that isn't indexed doesn't hide anything.
        Assert.Equal([IssueCatalog.NOINDEX_PAGE], Issues(rules, site.Page($"{site.Site}/e", RuleHarness.Html(Robots("noindex, nosnippet")), IndexabilityReason.Noindex)));

        IssueHit Refresh(string path, string content) =>
            rules.Check(site.Page($"{site.Site}/{path}", RuleHarness.Html($"<meta http-equiv=\"refresh\" content=\"{content}\">"))).Single();
        Assert.Equal("Moves to /new after 5 seconds", Refresh("f", "5; url=/new").Detail);
        Assert.Equal("Reloads the page after 30 seconds", Refresh("g", "30").Detail);
    }

    [Fact]
    public void FailingInternalUrlsAreReportedByOutcome()
    {
        var site = new RuleHarness();
        var notFound = site.Fetched($"{site.Site}/gone", FetchOutcome.ClientError, 404);
        var failing = site.Fetched($"{site.Site}/error", FetchOutcome.ServerError, 502);
        var slow = site.Fetched($"{site.Site}/slow", FetchOutcome.Timeout, 0);
        var refused = site.Fetched($"{site.Site}/refused", FetchOutcome.NetworkError, 0);
        var blocked = site.Fetched($"{site.Site}/blocked", FetchOutcome.Blocked, 403);
        var challenged = site.Fetched($"{site.Site}/challenge", FetchOutcome.Blocked, 403);
        var disallowed = site.Fetched($"{site.Site}/private", FetchOutcome.RobotsDisallowed, 0);
        site.Ok($"{site.Site}/fine");
        site.Fetched($"{site.Site}/moved", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/fine");
        site.Fetched("https://other.example/gone", FetchOutcome.ClientError, 404);
        site.Id($"{site.Site}/never-fetched");

        var hits = new ResponseCodeRules(site.Table(), new Dictionary<int, string> { [challenged] = "Cloudflare challenge" }).Finish().ToList();

        Assert.Equal(
        [
            new IssueHit(IssueCatalog.INTERNAL_CLIENT_ERROR, notFound, "404 Not Found"),
            new IssueHit(IssueCatalog.INTERNAL_SERVER_ERROR, failing, "502 Bad Gateway"),
            new IssueHit(IssueCatalog.INTERNAL_UNREACHABLE, slow, "Timed out"),
            new IssueHit(IssueCatalog.INTERNAL_UNREACHABLE, refused, "Connection failed"),
            new IssueHit(IssueCatalog.INTERNAL_BLOCKED, blocked, "403 Forbidden"),
            new IssueHit(IssueCatalog.INTERNAL_BLOCKED, challenged, "Cloudflare challenge"),
            new IssueHit(IssueCatalog.BLOCKED_BY_ROBOTS, disallowed)
        ], hits);
    }

    [Fact]
    public void HreflangAnnotationsAreCheckedOnIndexablePages()
    {
        var site = new RuleHarness();
        site.Ok($"{site.Site}/fr");
        site.Fetched($"{site.Site}/gone", FetchOutcome.ClientError, 404);
        site.Ok($"{site.Site}/hidden", IndexabilityReason.Noindex);
        site.Fetched($"{site.Site}/moved", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/fr");
        string Alternate(string code, string path) => $"<link rel=\"alternate\" hreflang=\"{code}\" href=\"{site.Site}{path}\">";
        List<IssueDefinition> Check(string path, string head, IndexabilityReason reason = IndexabilityReason.None, string body = "")
        {
            // The page first, so the table knows every URL it mentions.
            var page = site.Page($"{site.Site}{path}", RuleHarness.Html(head, body), reason);
            return Issues(new HreflangRules(site.Table()), page);
        }

        Assert.Empty(Check("/en", Alternate("en", "/en") + Alternate("fr", "/fr") + Alternate("x-default", "/en")));
        Assert.Empty(Check("/plain", string.Empty));
        Assert.Equal([IssueCatalog.HREFLANG_INVALID_CODE], Check("/uk", Alternate("en-UK", "/uk") + Alternate("x-default", "/uk")));
        Assert.Equal([IssueCatalog.HREFLANG_INVALID_CODE], Check("/latam", Alternate("es-419", "/latam") + Alternate("x-default", "/latam")));
        Assert.Equal([IssueCatalog.HREFLANG_MISSING_SELF], Check("/no-self", Alternate("fr", "/fr") + Alternate("x-default", "/fr")));
        Assert.Equal([IssueCatalog.HREFLANG_DUPLICATE_CODE],
            Check("/twice", Alternate("en", "/twice") + Alternate("fr", "/fr") + Alternate("fr", "/fr-ca") + Alternate("x-default", "/twice")));
        Assert.Equal([IssueCatalog.HREFLANG_MISSING_X_DEFAULT], Check("/no-default", Alternate("en", "/no-default") + Alternate("fr", "/fr")));
        Assert.Equal([IssueCatalog.HREFLANG_BROKEN_TARGET], Check("/broken", Alternate("en", "/broken") + Alternate("de", "/gone") + Alternate("x-default", "/broken")));
        Assert.Equal([IssueCatalog.HREFLANG_TO_NON_INDEXABLE],
            Check("/to-hidden", Alternate("en", "/to-hidden") + Alternate("de", "/hidden") + Alternate("es", "/moved") + Alternate("x-default", "/to-hidden")));
        Assert.Equal([IssueCatalog.HREFLANG_OUTSIDE_HEAD], Check("/body", string.Empty, body: Alternate("en", "/body")));

        // A canonicalised variant repeating its canonical's set is fine: Google reads hreflang on canonical pages.
        Assert.Empty(Check("/variant", Alternate("en", "/en") + Alternate("fr", "/fr"), IndexabilityReason.Canonicalised));
    }

    [Fact]
    public void HreflangAlternatesMustLinkBack()
    {
        var site = new RuleHarness();
        string Alternate(string code, string path) => $"<link rel=\"alternate\" hreflang=\"{code}\" href=\"{site.Site}{path}\">";
        var en = site.Page($"{site.Site}/en", RuleHarness.Html(
            Alternate("en", "/en") + Alternate("fr", "/fr") + Alternate("de", "/de") + Alternate("es", "/es") + Alternate("it", "/it")));
        var fr = site.Page($"{site.Site}/fr", RuleHarness.Html(Alternate("fr", "/fr") + Alternate("en", "/en")));
        var de = site.Page($"{site.Site}/de", RuleHarness.Html(Alternate("de", "/de")));
        var it = site.Page($"{site.Site}/it", RuleHarness.Html(Alternate("it", "/it")));
        site.Ok($"{site.Site}/es", IndexabilityReason.Noindex);
        var rules = new HreflangRules(site.Table());

        foreach (var page in new[] { en, fr, de, it })
        {
            rules.Observe(page);
        }

        // /de and /it don't link back, and the issue names both; /fr does; /es isn't indexable, which is reported on its own.
        Assert.Equal([new IssueHit(IssueCatalog.HREFLANG_MISSING_RETURN, en.UrlId, "2 alternates don't link back", de.UrlId) { Targets = [de.UrlId, it.UrlId] }],
            rules.Finish());
    }

    [Fact]
    public void FragmentsMustMatchAnIdOnTheTargetPage()
    {
        var site = new RuleHarness();
        var page = site.Page($"{site.Site}/a", RuleHarness.Html(body:
            "<h2 id=\"here\">Here</h2><a href=\"#here\">1</a><a href=\"#nowhere\">2</a><a href=\"#top\">3</a><a href=\"#!/route\">4</a>" +
            "<a href=\"#q=1\">5</a><a href=\"/b#there\">6</a><a href=\"/b#missing\">7</a><a href=\"/b#missing\">8</a><a href=\"/c#anything\">9</a>"));
        var target = site.Page($"{site.Site}/b", RuleHarness.Html(body: "<a name=\"there\">There</a>"));
        var capped = site.Ok($"{site.Site}/c");
        var urls = site.Table();
        PageFacts cappedFacts = new() { AnchorIds = Enumerable.Range(0, PageWalker.MAX_IDS).Select(index => $"id{index}").ToList() };
        Dictionary<int, PageFacts> facts = new() { [target.UrlId] = target.Facts, [capped] = cappedFacts };
        var links = page.Links.Where(link => link.Fragment is not null && link.ToId != page.UrlId).Select(link => (page.UrlId, link.ToId, link.Fragment!)).ToList();
        var rules = new FragmentRules(urls, facts.GetValueOrDefault, () => links, site.UrlOf);

        rules.Observe(page);
        rules.Observe(target);

        // #nowhere here, and #missing on /b (linked twice); a capped id list can't prove an id is missing. The issue names /b,
        // and the page itself as no other URL; its detail names the sections that are missing, each once.
        Assert.Equal([new IssueHit(IssueCatalog.BROKEN_FRAGMENT_LINKS, page.UrlId, "3 links to missing sections: #nowhere, /b#missing", target.UrlId)
            {
                Targets = [target.UrlId, IssueHit.NO_OTHER_URL]
            }],
            rules.Finish());
    }

    /// <summary>
    /// An id written percent-encoded, as table-of-contents plugins write non-Latin headings, is found by a link written the
    /// same way, and a plain id by an encoded link, as browsers find both. A link matching neither form is still broken.
    /// </summary>
    [Fact]
    public void FragmentsMatchIdsAsWrittenOrDecoded()
    {
        const string ENCODED = "%E3%81%AF%E3%81%98%E3%82%81%E3%81%AB";
        var site = new RuleHarness();
        var page = site.Page($"{site.Site}/post", RuleHarness.Html(body:
            $"<h2 id=\"{ENCODED}\">はじめに</h2><h2 id=\"まとめ\">まとめ</h2><a href=\"#{ENCODED}\">1</a><a href=\"#まとめ\">2</a>" +
            $"<a href=\"#%E3%81%BE%E3%81%A8%E3%82%81\">3</a><a href=\"/other#{ENCODED}\">4</a><a href=\"#%E3%81%AA%E3%81%84\">5</a>"));
        var other = site.Page($"{site.Site}/other", RuleHarness.Html(body: $"<p id=\"{ENCODED}\">はじめに</p>"));
        var urls = site.Table();
        Dictionary<int, PageFacts> facts = new() { [other.UrlId] = other.Facts };
        var links = page.Links.Where(link => link.Fragment is not null && link.ToId != page.UrlId).Select(link => (page.UrlId, link.ToId, link.Fragment!)).ToList();
        var rules = new FragmentRules(urls, facts.GetValueOrDefault, () => links, site.UrlOf);

        rules.Observe(page);
        rules.Observe(other);

        // Only #ない (encoded) has no id to find, on the page itself: no other URL. The detail names it as the link wrote it.
        Assert.Equal([new IssueHit(IssueCatalog.BROKEN_FRAGMENT_LINKS, page.UrlId, "1 link to missing sections: #%E3%81%AA%E3%81%84")], rules.Finish());
    }

    /// <summary>A page that moves on at once with a meta refresh is a redirect, so its links aren't checked, as for a 301.</summary>
    [Fact]
    public void AnInstantRefreshStubsFragmentLinksAreNotChecked()
    {
        var site = new RuleHarness();
        var stub = site.Page($"{site.Site}/old", RuleHarness.Html("<meta http-equiv=\"refresh\" content=\"0; url=/new\">",
            "<a href=\"#nowhere\">1</a><a href=\"/b#missing\">2</a>"));
        var page = site.Page($"{site.Site}/a", RuleHarness.Html(body: "<a href=\"/b#missing\">3</a>"));
        var target = site.Page($"{site.Site}/b", RuleHarness.Html(body: "<p id=\"there\">There</p>"));
        var urls = site.Table(instantRefreshes: [(stub.UrlId, site.Id($"{site.Site}/new"))]);
        Dictionary<int, PageFacts> facts = new() { [target.UrlId] = target.Facts };
        var links = new[] { stub, page }
            .SelectMany(from => from.Links.Where(link => link.Fragment is not null && link.ToId != from.UrlId).Select(link => (from.UrlId, link.ToId, link.Fragment!)))
            .ToList();
        var rules = new FragmentRules(urls, facts.GetValueOrDefault, () => links, site.UrlOf);

        foreach (var analysed in new[] { stub, page, target })
        {
            rules.Observe(RuleHarness.AsAnalysed(analysed, urls));
        }

        Assert.True(urls[stub.UrlId].IsMetaRefresh);
        Assert.Equal([new IssueHit(IssueCatalog.BROKEN_FRAGMENT_LINKS, page.UrlId, "1 link to missing sections: /b#missing", target.UrlId)], rules.Finish());
    }

    [Fact]
    public void CanonicalTargetsMustBeIndexablePages()
    {
        var site = new RuleHarness();
        site.Ok($"{site.Site}/noindex", IndexabilityReason.Noindex);
        site.Ok($"{site.Site}/elsewhere", IndexabilityReason.Canonicalised);
        site.Fetched($"{site.Site}/private", FetchOutcome.RobotsDisallowed, 0);
        site.Fetched($"{site.Site}/walled", FetchOutcome.Blocked, 403);
        site.Fetched("https://other.example/walled", FetchOutcome.Blocked, 403);
        site.Fetched("https://other.example/private", FetchOutcome.RobotsDisallowed, 0);
        site.Ok($"{site.Site}/fine");
        AnalyzedPage Page(string path, string canonical) =>
            site.Page($"{site.Site}{path}", RuleHarness.Html($"<link rel=\"canonical\" href=\"{canonical}\">"), IndexabilityReason.Canonicalised);
        var pages = new Dictionary<string, AnalyzedPage>
        {
            ["noindex"] = Page("/a", $"{site.Site}/noindex"),
            ["canonicalised"] = Page("/b", $"{site.Site}/elsewhere"),
            ["disallowed"] = Page("/c", $"{site.Site}/private"),
            ["blocked"] = Page("/d", $"{site.Site}/walled"),
            ["external blocked"] = Page("/e", "https://other.example/walled"),
            ["external disallowed"] = Page("/f", "https://other.example/private"),
            ["unchecked"] = Page("/g", $"{site.Site}/never-fetched"),
            ["fine"] = Page("/h", $"{site.Site}/fine")
        };
        var rules = new CanonicalRules(site.Table(), site.UrlOf);
        List<IssueDefinition> Targets(string name) => Issues(rules, pages[name]).Where(issue => issue != IssueCatalog.CANONICALISED_PAGE).ToList();

        Assert.Equal("The canonical URL is noindex", rules.Check(pages["noindex"]).Single(hit => hit.Issue == IssueCatalog.CANONICAL_TO_NON_INDEXABLE).Detail);
        Assert.Equal([IssueCatalog.CANONICAL_TO_NON_INDEXABLE], Targets("canonicalised"));
        Assert.Equal([IssueCatalog.CANONICAL_TO_NON_INDEXABLE], Targets("disallowed"));

        // A site refusing the crawler (a firewall, a rate limit) says nothing about Google, on this site or another: the URL
        // is unverified, as the sitemap checks take it (the real-site audit's jitsunami, rate-limited on every product).
        // Nor does another site's robots.txt, and an unchecked URL says nothing at all.
        Assert.Empty(Targets("blocked"));
        Assert.Empty(Targets("external blocked"));
        Assert.Empty(Targets("external disallowed"));
        Assert.Empty(Targets("unchecked"));
        Assert.Empty(Targets("fine"));
    }

    private static List<IssueDefinition> Issues(IPageRule rule, AnalyzedPage page) => rule.Check(page).Select(hit => hit.Issue).ToList();
}
