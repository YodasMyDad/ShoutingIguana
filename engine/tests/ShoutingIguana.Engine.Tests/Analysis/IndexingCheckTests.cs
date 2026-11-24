using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>
/// The new checks of step 3b's first batch (docs/v2-new-checks.md, "Indexing and canonicals"): what a page's robots rules
/// say beyond index and follow, and how its canonical is written and where it leads.
/// </summary>
public sealed class IndexingCheckTests
{
    private const string TOKEN = "ShoutingIguana";
    private static readonly DateTimeOffset FETCHED = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MetaRobotsAndTheHeaderDisagreeingIsAConflictButOneMentioningARuleTheOtherDoesntIsnt()
    {
        var site = new RuleHarness();
        List<IssueHit> Hits(string path, string robots, string header) =>
        [
            .. new DirectiveRules(site.Table(), TOKEN).Check(site.Page($"{site.Site}/{path}", RuleHarness.Html($"<meta name=\"robots\" content=\"{robots}\">"),
                headers: [new ResponseHeader("x-robots-tag", header)])).Where(hit => hit.Issue == IssueCatalog.ROBOTS_DIRECTIVES_CONFLICT)
        ];

        Assert.Equal("The HTML says \"index, follow\"; the X-Robots-Tag header says \"noindex\"", Assert.Single(Hits("a", "index, follow", "noindex")).Detail);
        Assert.Single(Hits("b", "nofollow", "follow"));
        Assert.Single(Hits("c", "all", "none"));

        // Search engines combine rules each source alone gives, and another crawler's rules aren't this one's.
        Assert.Empty(Hits("d", "noindex", "max-snippet:50"));
        Assert.Empty(Hits("e", "noindex", "bingbot: index"));
        Assert.Empty(Hits("f", "noindex, nofollow", "noindex"));
        Assert.Single(Hits("g", "noindex", "googlebot: index"));
    }

    [Fact]
    public void ANoindexPageOneOrTwoClicksFromHomeIsFlaggedUnlessItsAddressNamesAPrivatePage()
    {
        var site = new RuleHarness();
        var noindex = RuleHarness.Html("<meta name=\"robots\" content=\"noindex\">");
        var home = site.Page($"{site.Site}/", noindex, IndexabilityReason.Noindex);
        var pages = new[]
        {
            site.Page($"{site.Site}/services/", noindex, IndexabilityReason.Noindex) with { Depth = 1 },
            site.Page($"{site.Site}/services/design", noindex, IndexabilityReason.Noindex) with { Depth = 2 },
            site.Page($"{site.Site}/services/design/old", noindex, IndexabilityReason.Noindex) with { Depth = 3 },
            site.Page($"{site.Site}/my-account/orders", noindex, IndexabilityReason.Noindex) with { Depth = 1 },
            site.Page($"{site.Site}/Thank-You", noindex, IndexabilityReason.Noindex) with { Depth = 2 },
            site.Page($"{site.Site}/about", RuleHarness.Html()) with { Depth = 1 }
        };
        var rules = new DirectiveRules(site.Table(), TOKEN);

        var hits = pages.Prepend(home).SelectMany(rules.Check).Where(hit => hit.Issue == IssueCatalog.NOINDEX_NEAR_HOME).ToList();

        Assert.Equal(
        [
            new IssueHit(IssueCatalog.NOINDEX_NEAR_HOME, pages[0].UrlId, "1 click from the start"),
            new IssueHit(IssueCatalog.NOINDEX_NEAR_HOME, pages[1].UrlId, "2 clicks from the start")
        ], hits);
    }

    [Fact]
    public void AnUnavailableAfterDateThatHasPassedIsFlaggedOnAPageThatIsntNoindex()
    {
        var site = new RuleHarness();
        var rules = new DirectiveRules(site.Table(), TOKEN);
        string? Detail(string path, string meta = "", string? header = null) => rules
            .Check(site.Page($"{site.Site}/{path}", RuleHarness.Html(meta.Length == 0 ? string.Empty : $"<meta name=\"robots\" content=\"{meta}\">"),
                headers: header is null ? [] : [new ResponseHeader("x-robots-tag", header)]) with { FetchedAt = FETCHED })
            .SingleOrDefault(hit => hit.Issue == IssueCatalog.UNAVAILABLE_AFTER_PASSED).Detail;

        Assert.Equal("unavailable_after: 2020-01-01, passed", Detail("a", "unavailable_after: 2020-01-01"));
        Assert.Null(Detail("b", "unavailable_after: 2099-01-01"));
        Assert.Null(Detail("c", "noindex, unavailable_after: 2020-01-01"));
        Assert.Null(Detail("d", "unavailable_after: when the sale ends"));

        // An RFC 850 date has a comma of its own, and the rules after it are no part of it.
        Assert.Equal("unavailable_after: Monday, 21-Sep-20 15:00:00 GMT, passed", Detail("e", "unavailable_after: Monday, 21-Sep-20 15:00:00 GMT, noarchive"));
        Assert.Equal("unavailable_after: 2026-11-17T11:00:00Z, passed", Detail("f", header: "googlebot: unavailable_after: 2026-11-17T11:00:00Z"));
        Assert.Null(Detail("g", header: "bingbot: unavailable_after: 2020-01-01"));
        Assert.Null(Detail("h", header: "unavailable_after: 2026-11-17T13:00:00Z"));
    }

    [Theory]
    [InlineData("2020-09-21", "2020-09-21T00:00:00Z")]
    [InlineData("2020-09-21T15:00:00+01:00", "2020-09-21T14:00:00Z")]
    [InlineData("Mon, 21 Sep 2020 15:00:00 GMT", "2020-09-21T15:00:00Z")]
    [InlineData("21 Sep 20 15:00 PST", "2020-09-21T23:00:00Z")]
    [InlineData("Monday, 21-Sep-20 15:00:00 GMT", "2020-09-21T15:00:00Z")]
    [InlineData("23-Jul-2007 18:00:00 EST", "2007-07-23T23:00:00Z")]
    public void UnavailableAfterDatesAreReadInTheFormatsGoogleNames(string written, string utc)
    {
        Assert.True(DirectiveRules.TryReadDate(written, out var date));
        Assert.Equal(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture), date);
    }

    [Theory]
    [InlineData("soon")]
    [InlineData("")]
    [InlineData("the 21st")]
    public void AnUnreadableUnavailableAfterDateIsIgnored(string written) => Assert.False(DirectiveRules.TryReadDate(written, out _));

    [Fact]
    public void RulesThatKeepImagesVideoOrTranslationOutOfSearchAreFlaggedOnIndexablePages()
    {
        var site = new RuleHarness();
        var rules = new DirectiveRules(site.Table(), TOKEN);
        List<IssueHit> Hits(string path, string robots, IndexabilityReason reason = IndexabilityReason.None) =>
            [.. rules.Check(site.Page($"{site.Site}/{path}", RuleHarness.Html($"<meta name=\"robots\" content=\"{robots}\">"), reason))];

        Assert.Equal("max-image-preview:none", Hits("a", "max-image-preview:none").Single(hit => hit.Issue == IssueCatalog.MEDIA_PREVIEW_BLOCKED).Detail);
        Assert.Equal("noimageindex, max-video-preview:0",
            Hits("b", "noimageindex, max-video-preview: 0").Single(hit => hit.Issue == IssueCatalog.MEDIA_PREVIEW_BLOCKED).Detail);
        Assert.DoesNotContain(Hits("c", "max-image-preview:large, max-video-preview:-1"), hit => hit.Issue == IssueCatalog.MEDIA_PREVIEW_BLOCKED);
        Assert.Contains(Hits("d", "notranslate"), hit => hit.Issue == IssueCatalog.TRANSLATION_BLOCKED);
        Assert.DoesNotContain(Hits("e", "noindex, notranslate, noimageindex", IndexabilityReason.Noindex),
            hit => hit.Issue == IssueCatalog.TRANSLATION_BLOCKED || hit.Issue == IssueCatalog.MEDIA_PREVIEW_BLOCKED);
    }

    [Fact]
    public void IndexIfEmbeddedDoesNothingWithoutNoindex()
    {
        var site = new RuleHarness();
        var rules = new DirectiveRules(site.Table(), TOKEN);

        Assert.Contains(rules.Check(site.Page($"{site.Site}/a", RuleHarness.Html("<meta name=\"googlebot\" content=\"indexifembedded\">"))),
            hit => hit.Issue == IssueCatalog.INDEXIFEMBEDDED_WITHOUT_NOINDEX);
        Assert.DoesNotContain(rules.Check(site.Page($"{site.Site}/b", RuleHarness.Html("<meta name=\"googlebot\" content=\"noindex, indexifembedded\">"),
            IndexabilityReason.Noindex)), hit => hit.Issue == IssueCatalog.INDEXIFEMBEDDED_WITHOUT_NOINDEX);
    }

    /// <summary>A rule written with a space after its colon is one rule: "max-image-preview: none" isn't "none", which would be noindex.</summary>
    [Fact]
    public void AValuedRuleWithASpaceAfterItsColonIsOneRule()
    {
        var robots = RobotsMetaParser.Parse(["max-image-preview: none"], [], TOKEN);

        Assert.False(robots.NoIndex || robots.NoFollow);
        Assert.Equal(["max-image-preview: none"], robots.Directives);
        Assert.True(RobotsMetaParser.Parse(["noindex nofollow"], [], TOKEN) is { NoIndex: true, NoFollow: true });
    }

    [Fact]
    public void TheParserNotesACanonicalsOtherAttributesTheNextAndPrevLinksAndItsCollection()
    {
        var page = new Uri("https://example.com/list/page/2/");
        var parsed = HtmlPageParser.Parse(RuleHarness.Html(
            "<link rel=\"canonical\" href=\"/list/page/2/\" media=\"screen\" hreflang=\"en\">" +
            "<link rel=\"prev\" href=\"/list/\"><link rel=\"next\" href=\"/list/page/3/\"><link rel=\"previous\" href=\"http://[bad\">"), page, [], TOKEN);

        Assert.Equal(PageFacts.CURRENT_COLLECTION, parsed.Facts.CollectedVersion);
        Assert.Equal("hreflang, media", Assert.Single(parsed.Facts.Canonicals).Attributes);
        Assert.Equal(
            [new PaginationFact(false, "https://example.com/list/"), new PaginationFact(true, "https://example.com/list/page/3/"), new PaginationFact(false, null)],
            parsed.Facts.Pagination);
        Assert.Null(HtmlPageParser.Parse(RuleHarness.Html("<link rel=\"canonical\" href=\"/a\">"), page, [], TOKEN).Facts.Canonicals.Single().Attributes);
    }

    [Fact]
    public void ARefreshHeaderIsAPagesRefreshAndWinsOverTheMetaTag()
    {
        var page = new Uri("https://example.com/old");
        var parsed = HtmlPageParser.Parse(RuleHarness.Html("<meta http-equiv=\"refresh\" content=\"5; url=/meta\">"), page,
            [new ResponseHeader("refresh", "0; url=/new")], TOKEN);

        Assert.Equal(new MetaRefreshFact(0, "/new", FromHttpHeader: true), parsed.Facts.MetaRefresh);
        Assert.Equal(["https://example.com/new"], parsed.Links.Where(link => link.Kind == LinkKind.MetaRefresh).Select(link => link.Target.AbsoluteUri));

        var site = new RuleHarness();
        var hits = new IndexabilityRules().Check(site.Page($"{site.Site}/a", RuleHarness.Html(), headers: [new ResponseHeader("refresh", "5; url=/b")])).ToList();
        Assert.Equal("The Refresh header moves to /b after 5 seconds", hits.Single(hit => hit.Issue == IssueCatalog.META_REFRESH).Detail);
        Assert.Equal("Reloads the page after 30 seconds", new IndexabilityRules().Check(site.Page($"{site.Site}/c",
            RuleHarness.Html("<meta http-equiv=\"refresh\" content=\"30\">"))).Single(hit => hit.Issue == IssueCatalog.META_REFRESH).Detail);
    }

    [Theory]
    [InlineData("https://example.com/list/?page=2", "https://example.com/list/", "Page 2 names page 1 as its canonical")]
    [InlineData("https://example.com/list/?PAGE=3&sort=new", "https://example.com/list/?sort=new", "Page 3 names page 1 as its canonical")]
    [InlineData("https://example.com/list/?p=2", "https://example.com/list/?p=1", "Page 2 names page 1 as its canonical")]
    [InlineData("https://example.com/blog/page/2/", "https://example.com/blog/", "Page 2 names page 1 as its canonical")]
    [InlineData("https://example.com/blog/page-12", "https://example.com/blog", "Page 12 names page 1 as its canonical")]
    [InlineData("https://example.com/list/?page=2", "https://example.com/other/", null)]
    [InlineData("https://example.com/list/?page=1&x=1", "https://example.com/list/?x=1", null)]
    [InlineData("https://example.com/news/2", "https://example.com/news/", null)]
    public void ALaterPageOfASeriesCanonicalisedToItsFirstIsFlaggedInsteadOfCanonicalised(string url, string canonical, string? detail)
    {
        var site = new RuleHarness();
        site.Ok(canonical);
        var page = site.Page(url, RuleHarness.Html($"<link rel=\"canonical\" href=\"{canonical}\">"), IndexabilityReason.Canonicalised);

        var hits = new CanonicalRules(site.Table(), site.UrlOf).Check(page).ToList();

        Assert.Equal(detail, hits.SingleOrDefault(hit => hit.Issue == IssueCatalog.CANONICAL_TO_FIRST_PAGE).Detail);
        Assert.Equal(detail is null, hits.Any(hit => hit.Issue == IssueCatalog.CANONICALISED_PAGE));
        Assert.All(hits.Where(hit => hit.Issue == IssueCatalog.CANONICAL_TO_FIRST_PAGE), hit => Assert.Equal(site.Find(canonical), hit.RelatedUrlId));
    }

    /// <summary>Without a page number in the URL, the rel="prev" says: the canonical is where it leads, and that page has none of its own.</summary>
    [Fact]
    public void ALaterPageIsKnownByItsPrevLinkToAPageWithNone()
    {
        var site = new RuleHarness();
        var first = site.Page($"{site.Site}/sofas", RuleHarness.Html("<link rel=\"next\" href=\"/sofas-more\">"));
        var middle = site.Page($"{site.Site}/sofas-more", RuleHarness.Html("<link rel=\"prev\" href=\"/sofas\">"));
        var facts = new Dictionary<int, PageFacts> { [first.UrlId] = first.Facts, [middle.UrlId] = middle.Facts };
        var second = site.Page($"{site.Site}/sofas-more", RuleHarness.Html("<link rel=\"prev\" href=\"/sofas\"><link rel=\"canonical\" href=\"/sofas\">"),
            IndexabilityReason.Canonicalised);
        var third = site.Page($"{site.Site}/sofas-last", RuleHarness.Html("<link rel=\"prev\" href=\"/sofas-more\"><link rel=\"canonical\" href=\"/sofas-more\">"),
            IndexabilityReason.Canonicalised);
        var rules = new CanonicalRules(site.Table(), site.UrlOf, facts.GetValueOrDefault);

        Assert.Equal("A later page names page 1 as its canonical", rules.Check(second).Single(hit => hit.Issue == IssueCatalog.CANONICAL_TO_FIRST_PAGE).Detail);
        Assert.Contains(rules.Check(third), hit => hit.Issue == IssueCatalog.CANONICALISED_PAGE);
        Assert.DoesNotContain(rules.Check(third), hit => hit.Issue == IssueCatalog.CANONICAL_TO_FIRST_PAGE);
    }

    [Fact]
    public void ACanonicalToAnotherSiteIsFlaggedInsteadOfCanonicalised()
    {
        var site = new RuleHarness();
        site.Fetched("https://staging.example.org/sofas", FetchOutcome.Success, 200, isHtml: true, isParsed: false);
        var page = site.Page($"{site.Site}/sofas", RuleHarness.Html("<link rel=\"canonical\" href=\"https://staging.example.org/sofas\">"),
            IndexabilityReason.Canonicalised);

        var hits = new CanonicalRules(site.Table(), site.UrlOf).Check(page).ToList();

        Assert.Equal([new IssueHit(IssueCatalog.CANONICAL_OTHER_DOMAIN, page.UrlId, "On another host: staging.example.org", site.Find("https://staging.example.org/sofas"))],
            hits);

        // The www twin is the same site.
        var twin = site.Page($"{site.Site}/beds", RuleHarness.Html("<link rel=\"canonical\" href=\"https://www.example.com/beds\">"), IndexabilityReason.Canonicalised);
        Assert.Equal([IssueCatalog.CANONICALISED_PAGE], new CanonicalRules(site.Table(), site.UrlOf).Check(twin).Select(hit => hit.Issue));
    }

    /// <summary>
    /// Pages whose canonicals, followed page to page and through redirects, come back round are on a loop, whatever order
    /// the crawl read them in, and aren't reported as canonicalised to a non-indexable page; a page that leads into the
    /// loop is.
    /// </summary>
    [Fact]
    public void PagesOnACanonicalLoopAreFlaggedForTheLoopAndNotAsCanonicalisedToANonIndexablePage()
    {
        var site = new RuleHarness();
        var html = (string target) => RuleHarness.Html($"<link rel=\"canonical\" href=\"{site.Site}{target}\">");
        var a = site.Page($"{site.Site}/a", html("/b"), IndexabilityReason.Canonicalised);
        var b = site.Page($"{site.Site}/b", html("/c-old"), IndexabilityReason.Canonicalised);
        site.Fetched($"{site.Site}/c-old", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/c");
        var c = site.Page($"{site.Site}/c", html("/a"), IndexabilityReason.Canonicalised);
        var lead = site.Page($"{site.Site}/lead", html("/a"), IndexabilityReason.Canonicalised);
        var urls = site.Table(canonicals: [(lead.UrlId, a.UrlId), (c.UrlId, a.UrlId), (a.UrlId, b.UrlId), (b.UrlId, site.Find($"{site.Site}/c-old"))]);
        var rules = new CanonicalRules(urls, site.UrlOf);

        Assert.Equal("A loop of 3: /a → /b → /c → back to /a", rules.Check(a).Single(hit => hit.Issue == IssueCatalog.CANONICAL_LOOP).Detail);
        Assert.Equal([IssueCatalog.CANONICALISED_PAGE, IssueCatalog.CANONICAL_LOOP, IssueCatalog.CANONICAL_TO_REDIRECT], rules.Check(b).Select(hit => hit.Issue));
        Assert.Equal([IssueCatalog.CANONICALISED_PAGE, IssueCatalog.CANONICAL_LOOP], rules.Check(c).Select(hit => hit.Issue));
        Assert.Equal([IssueCatalog.CANONICALISED_PAGE, IssueCatalog.CANONICAL_TO_NON_INDEXABLE], rules.Check(lead).Select(hit => hit.Issue));
        Assert.False(urls.IsOnCanonicalLoop(lead.UrlId));
    }

    /// <summary>
    /// One loop is one fault, so its pages share one group: the fault is the loop as its detail lists it, from the step
    /// that comes first, so each page's detail (which starts from that page) reads as the same loop. Each page still names
    /// the page its own canonical names.
    /// </summary>
    [Fact]
    public void ThePagesOfOneCanonicalLoopAreOneGroup()
    {
        var site = new RuleHarness();
        var html = (string target) => RuleHarness.Html($"<link rel=\"canonical\" href=\"{site.Site}{target}\">");
        var a = site.Page($"{site.Site}/a", html("/b"), IndexabilityReason.Canonicalised);
        var b = site.Page($"{site.Site}/b", html("/c"), IndexabilityReason.Canonicalised);
        var c = site.Page($"{site.Site}/c", html("/a"), IndexabilityReason.Canonicalised);
        var x = site.Page($"{site.Site}/x", html("/y"), IndexabilityReason.Canonicalised);
        var y = site.Page($"{site.Site}/y", html("/x"), IndexabilityReason.Canonicalised);
        var urls = site.Table(canonicals: [(a.UrlId, b.UrlId), (b.UrlId, c.UrlId), (c.UrlId, a.UrlId), (x.UrlId, y.UrlId), (y.UrlId, x.UrlId)]);
        var rules = new CanonicalRules(urls, site.UrlOf);
        IssueHit Loop(AnalyzedPage page) => rules.Check(page).Single(hit => hit.Issue == IssueCatalog.CANONICAL_LOOP);
        string Fault(AnalyzedPage page) => IssueCatalog.CANONICAL_LOOP.FaultOf!(Loop(page).Detail!);

        Assert.Equal(IssueGrouping.PageDetail, IssueCatalog.CANONICAL_LOOP.Grouping);
        Assert.Equal("A loop of 3: /c → /a → /b → back to /c", Loop(c).Detail);
        Assert.Equal(["A loop of 3: /a → /b → /c → back to /a"], new[] { a, b, c }.Select(Fault).Distinct());
        Assert.Equal(["A loop of 2: /x → /y → back to /x"], new[] { x, y }.Select(Fault).Distinct());
        Assert.Equal([b.UrlId, c.UrlId, a.UrlId], new[] { a, b, c }.Select(page => Loop(page).RelatedUrlId));
        // A detail cut short, which can't be read as a loop, is its own fault.
        Assert.Equal("A loop of 9: /p → /q → …", IssueCatalog.CANONICAL_LOOP.FaultOf!("A loop of 9: /p → /q → …"));
    }

    [Fact]
    public void ACanonicalWithAFragmentOrAnAlternatesAttributesIsFlaggedThoughItNamesThePageItself()
    {
        var site = new RuleHarness();
        var rules = new CanonicalRules(site.Table(), site.UrlOf);
        List<IssueHit> Hits(string path, string link) => [.. rules.Check(site.Page($"{site.Site}/{path}", RuleHarness.Html(link)))];

        Assert.Equal([new IssueHit(IssueCatalog.CANONICAL_FRAGMENT, site.Id($"{site.Site}/a"), "#reviews")],
            Hits("a", $"<link rel=\"canonical\" href=\"{site.Site}/a#reviews\">"));
        Assert.Empty(Hits("b", $"<link rel=\"canonical\" href=\"{site.Site}/b#\">"));
        Assert.Equal([new IssueHit(IssueCatalog.CANONICAL_INVALID_ATTRIBUTE, site.Id($"{site.Site}/c"), "media")],
            Hits("c", $"<link rel=\"canonical\" media=\"only screen and (max-width: 640px)\" href=\"{site.Site}/c\">"));
    }

    /// <summary>
    /// Google: "rel="canonical" annotations with hreflang, lang, media, and type attributes are not used for
    /// canonicalization". Such a link is no canonical: the page isn't canonicalised by it, it contradicts no noindex, and it
    /// isn't a second canonical beside the page's real one.
    /// </summary>
    [Fact]
    public void ACanonicalWithAnAlternatesAttributesIsntTakenAsThePagesCanonical()
    {
        var site = new RuleHarness();
        site.Ok($"{site.Site}/other");
        var rules = new CanonicalRules(site.Table(), site.UrlOf);
        var qualified = $"<link rel=\"canonical\" hreflang=\"en\" href=\"{site.Site}/other\">";
        List<IssueHit> Hits(string path, string head) => [.. rules.Check(site.Page($"{site.Site}/{path}", RuleHarness.Html(head)))];

        Assert.Equal([new IssueHit(IssueCatalog.CANONICAL_INVALID_ATTRIBUTE, site.Id($"{site.Site}/a"), "hreflang")], Hits("a", qualified));
        Assert.Equal([new IssueHit(IssueCatalog.CANONICAL_INVALID_ATTRIBUTE, site.Id($"{site.Site}/b"), "hreflang")],
            Hits("b", $"<meta name=\"robots\" content=\"noindex\">{qualified}"));
        Assert.Equal([new IssueHit(IssueCatalog.CANONICAL_INVALID_ATTRIBUTE, site.Id($"{site.Site}/c"), "hreflang")],
            Hits("c", $"{qualified}<link rel=\"canonical\" href=\"{site.Site}/c\">"));

        // The parse records it, for the check, but not as a canonical link of the page: nothing follows it as one.
        var parsed = HtmlPageParser.Parse(RuleHarness.Html(qualified), new Uri($"{site.Site}/d"), [], "ShoutingIguana");
        Assert.Equal("hreflang", Assert.Single(parsed.Facts.Canonicals).Attributes);
        Assert.DoesNotContain(parsed.Links, link => link.Kind == LinkKind.Canonical);
    }

    [Fact]
    public void ARedirectWhoseLinkHeaderNamesAnotherCanonicalThanWhereItEndsIsFlagged()
    {
        var site = new RuleHarness();
        var moved = site.Fetched($"{site.Site}/moved", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/hop");
        site.Fetched($"{site.Site}/hop", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/final");
        site.Ok($"{site.Site}/final");
        var agreeing = site.Fetched($"{site.Site}/agreeing", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/final");
        var plain = site.Fetched($"{site.Site}/plain", FetchOutcome.Redirect, 302, redirectTo: $"{site.Site}/final");
        IReadOnlyList<ResponseHeader> Link(string url) => [new ResponseHeader("link", $"<{url}>; rel=\"canonical\""), new ResponseHeader("location", "/x")];
        (int, IReadOnlyList<ResponseHeader>)[] redirects =
            [(moved, Link("/other")), (agreeing, Link($"{site.Site}/final")), (plain, [new ResponseHeader("location", $"{site.Site}/final")])];

        var hits = new RedirectHeaderRules(site.Table(), () => redirects, site.UrlOf).Finish().ToList();

        Assert.Equal([new IssueHit(IssueCatalog.CANONICAL_ON_REDIRECT, moved, $"Redirects to {site.Site}/final; its canonical header names {site.Site}/other")], hits);
    }

    [Fact]
    public void ACanonicalPageNothingLinksToIsFlaggedOnlyWhenEveryLinkingPageWasRead()
    {
        var site = new RuleHarness();
        var target = site.Fetched($"{site.Site}/main", FetchOutcome.Success, 200, isHtml: true);
        var listed = site.Fetched($"{site.Site}/listed", FetchOutcome.Success, 200, isHtml: true, inSitemap: true);
        var linked = site.Fetched($"{site.Site}/linked", FetchOutcome.Success, 200, isHtml: true);
        var html = (string canonical, string body) => RuleHarness.Html($"<link rel=\"canonical\" href=\"{site.Site}{canonical}\">", body);
        AnalyzedPage[] pages =
        [
            site.Page($"{site.Site}/copy-1", html("/main", string.Empty), IndexabilityReason.Canonicalised),
            site.Page($"{site.Site}/copy-2", html("/listed", string.Empty), IndexabilityReason.Canonicalised),
            site.Page($"{site.Site}/copy-3", html("/linked", string.Empty), IndexabilityReason.Canonicalised),
            site.Page($"{site.Site}/", html("/", "<a href=\"/linked\">Linked</a>"))
        ];

        List<IssueHit> Run(bool crawlComplete, bool everyLinkingPage)
        {
            var rule = new InlinkRules(site.Table(), crawlComplete, everyLinkingPage);
            Array.ForEach(pages, rule.Observe);
            return [.. rule.Finish()];
        }

        // A sitemap page nothing links to is an orphan when orphans are judged, and this check's when they aren't.
        Assert.Equal(
        [
            new IssueHit(IssueCatalog.ORPHAN_PAGE, listed, "Only found in a sitemap"),
            new IssueHit(IssueCatalog.CANONICAL_UNLINKED, target, "Only canonicals lead here")
        ], Run(crawlComplete: true, everyLinkingPage: true));
        Assert.Equal(
        [
            new IssueHit(IssueCatalog.CANONICAL_UNLINKED, target, "Only canonicals lead here"),
            new IssueHit(IssueCatalog.CANONICAL_UNLINKED, listed, "Only canonicals lead here")
        ], Run(crawlComplete: false, everyLinkingPage: true));
        Assert.Empty(Run(crawlComplete: false, everyLinkingPage: false));
        Assert.DoesNotContain(Run(crawlComplete: true, everyLinkingPage: true), hit => hit.UrlId == linked);
    }
}
