using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>
/// The new checks of step 3b's second batch (docs/v2-new-checks.md, "Links and crawlability"): how a page links out, how
/// deep it is and what links to it, and the rel="next" and rel="prev" links of a paginated series.
/// </summary>
public sealed class LinkCheckTests
{
    private const string TOKEN = "ShoutingIguana";
    private static readonly AnalysisSettings SETTINGS = new();

    [Fact]
    public void TheParserCountsLinksSearchEnginesCantFollowAndKeepsTheFirstThree()
    {
        var parsed = HtmlPageParser.Parse(RuleHarness.Html(body:
            "<a onclick=\"go()\">Shop</a><a href=\"javascript:void(0)\">Open menu</a><a href=\"\">Empty</a><a href=\" # \" onclick=\"next()\">Next</a>" +
            "<a routerLink=\"/basket\">Basket</a><span href=\"/sale\">Sale</span>" +
            "<a name=\"top\">Named anchor</a><a href=\"#\">Toggle</a><a href=\"/fine\">Fine</a><svg><a href=\"/in-svg\">In an SVG</a></svg>"),
            new Uri("https://example.com/"), [], TOKEN);

        Assert.Equal(6, parsed.Facts.UncrawlableLinks!.Count);
        Assert.Equal([new UncrawlableLinkFact("Shop", "onclick"), new UncrawlableLinkFact("Open menu", "javascript:"), new UncrawlableLinkFact("Empty", "empty href")],
            parsed.Facts.UncrawlableLinks.Examples);
        Assert.Null(HtmlPageParser.Parse(RuleHarness.Html(body: "<a href=\"/a\">A</a><a name=\"b\"></a>"), new Uri("https://example.com/"), [], TOKEN).Facts.UncrawlableLinks);

        var site = new RuleHarness();
        var page = site.Page($"{site.Site}/", RuleHarness.Html(body:
            "<a onclick=\"go()\">Shop</a><span href=\"/sale\"></span><a href=\"javascript:void(0)\">Open menu</a><a routerLink=\"/x\">Basket</a>"));
        var hit = new LinkStructureRules(SETTINGS, site.Table(), site.UrlOf).Check(page).Single(hit => hit.Issue == IssueCatalog.UNCRAWLABLE_LINKS);
        Assert.Equal("4 links: \"Shop\" (onclick), no text (<span href>), \"Open menu\" (javascript:) …", hit.Detail);
    }

    [Fact]
    public void ABrokenScriptStylesheetFrameOrMediaFileIsFlaggedButNotAnImage()
    {
        var site = new RuleHarness();
        site.Fetched($"{site.Site}/app.js", FetchOutcome.ClientError, 404);
        site.Fetched($"{site.Site}/old.css", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/gone.css");
        site.Fetched($"{site.Site}/gone.css", FetchOutcome.ClientError, 404);
        site.Fetched($"{site.Site}/missing.jpg", FetchOutcome.ClientError, 404);
        site.Fetched("https://cdn.example.org/tag.js", FetchOutcome.ClientError, 403);
        List<IssueHit> Hits(string path, string head, string body = "")
        {
            var page = site.Page($"{site.Site}/{path}", RuleHarness.Html(head, body));
            return [.. new LinkStructureRules(SETTINGS, site.Table(), site.UrlOf).Check(page).Where(hit => hit.Issue == IssueCatalog.BROKEN_RESOURCES)];
        }

        var both = Assert.Single(Hits("a", "<link rel=\"stylesheet\" href=\"/old.css\"><script src=\"/app.js\"></script>"));
        Assert.Equal(("2 broken files (the first redirects to a URL that returns 404 Not Found)", site.Find($"{site.Site}/old.css")), (both.Detail, both.RelatedUrlId));
        Assert.Equal([site.Find($"{site.Site}/old.css"), site.Find($"{site.Site}/app.js")], both.AllTargets);
        Assert.Empty(Hits("b", string.Empty, "<img src=\"/missing.jpg\" alt=\"Missing\" width=\"1\" height=\"1\">"));

        // Another site refusing the crawler says nothing about visitors, as with broken links.
        Assert.Empty(Hits("c", "<script src=\"https://cdn.example.org/tag.js\"></script>"));
    }

    [Fact]
    public void LinksToPrivateAddressesAreFlagged()
    {
        var site = new RuleHarness();
        var page = site.Page($"{site.Site}/", RuleHarness.Html(body:
            "<a href=\"http://localhost:3000/shop\">Shop</a><a href=\"http://localhost:3000/shop\">Shop again</a><img src=\"http://192.168.1.10/a.png\" alt=\"A\">" +
            "<a href=\"https://shop.localhost/\">Dev</a><a href=\"https://example.org/\">Public</a>"));
        var hit = new LinkStructureRules(SETTINGS, site.Table(), site.UrlOf).Check(page).Single(hit => hit.Issue == IssueCatalog.LINKS_TO_LOCALHOST);

        Assert.Equal("4 links, such as http://localhost:3000/shop", hit.Detail);
        Assert.Equal(["http://localhost:3000/shop", "http://192.168.1.10/a.png", "https://shop.localhost/"], hit.AllTargets.Select(site.UrlOf));
    }

    /// <summary>On a development copy at a loopback address, the machine's own addresses are its neighbours; another machine's aren't.</summary>
    [Fact]
    public void OnACrawlOfAPrivateAddressThatMachinesOwnAddressesArentFlagged()
    {
        var site = new RuleHarness("http://127.0.0.1:8080");
        var page = site.Page($"{site.Site}/", RuleHarness.Html(body:
            "<a href=\"http://localhost:3000/api\">API</a><a href=\"http://127.0.0.1:9000/\">Admin</a><a href=\"http://192.168.1.10/nas\">NAS</a>"));

        var hit = new LinkStructureRules(SETTINGS, site.Table(), site.UrlOf, new Uri(site.Site)).Check(page).Single(hit => hit.Issue == IssueCatalog.LINKS_TO_LOCALHOST);

        Assert.Equal("1 link, such as http://192.168.1.10/nas", hit.Detail);
        Assert.Equal([site.Find("http://192.168.1.10/nas")], hit.AllTargets);
    }

    [Fact]
    public void APageLinkingToFewerThanThreeOtherPagesOfTheSiteIsADeadEnd()
    {
        var site = new RuleHarness();
        string? Detail(string path, string body, IndexabilityReason reason = IndexabilityReason.None, AnalysisSettings? settings = null)
        {
            var page = site.Page($"{site.Site}/{path}", RuleHarness.Html(body: body), reason);
            return new LinkStructureRules(settings ?? SETTINGS, site.Table(), site.UrlOf).Check(page)
                .SingleOrDefault(hit => hit.Issue == IssueCatalog.FEW_INTERNAL_OUTLINKS).Detail;
        }

        Assert.Equal("No links to other pages", Detail("a", "<a href=\"/a\">Itself</a><a href=\"#top\">Top</a><a href=\"https://example.org/\">Elsewhere</a>"));
        Assert.Equal("2 links to other pages, under 3", Detail("b", "<a href=\"/x\">X</a><a href=\"/x#more\">X again</a><a href=\"/y\" rel=\"nofollow\">Y</a>"));
        Assert.Null(Detail("c", "<a href=\"/x\">X</a><a href=\"/y\">Y</a><a href=\"/z\">Z</a>"));
        Assert.Null(Detail("d", string.Empty, IndexabilityReason.Noindex));
        Assert.Null(Detail("e", string.Empty, settings: new AnalysisSettings { MinInternalOutlinks = 0 }));
    }

    [Fact]
    public void InternalLinksWhoseWholeTextIsGenericAreFlagged()
    {
        var site = new RuleHarness();
        var page = site.Page($"{site.Site}/", RuleHarness.Html(body:
            "<a href=\"/a\">Read more…</a><a href=\"/b\">read  MORE</a><a href=\"/c\">here</a><a href=\"/d\">Read more about sofas</a>" +
            "<a href=\"/e\"><img src=\"/e.jpg\" alt=\"Read more\"></a><a href=\"https://example.org/\">Click here</a>"));

        var hit = new LinkStructureRules(SETTINGS, site.Table(), site.UrlOf).Check(page).Single(hit => hit.Issue == IssueCatalog.GENERIC_ANCHOR_TEXT);

        Assert.Equal("3 links: \"Read more…\" ×2, \"here\"", hit.Detail);
        Assert.Equal([$"{site.Site}/a", $"{site.Site}/b", $"{site.Site}/c"], hit.AllTargets.Select(site.UrlOf));
    }

    [Fact]
    public void APageMoreThanFourClicksInIsDeep()
    {
        var site = new RuleHarness();
        string? Detail(string path, int depth, IndexabilityReason reason = IndexabilityReason.None)
        {
            var page = site.Page($"{site.Site}/{path}", RuleHarness.Html(body: "<a href=\"/x\">X</a><a href=\"/y\">Y</a><a href=\"/z\">Z</a>"), reason) with { Depth = depth };
            return new LinkStructureRules(SETTINGS, site.Table(), site.UrlOf).Check(page).SingleOrDefault(hit => hit.Issue == IssueCatalog.DEEP_PAGE).Detail;
        }

        Assert.Equal("5 clicks from the start, over 4", Detail("a", 5));
        Assert.Null(Detail("b", 4));
        Assert.Null(Detail("c", CrawlDepth.UNLINKED));
        Assert.Null(Detail("d", 7, IndexabilityReason.Noindex));
    }

    [Fact]
    public void APageWithMoreThanAThousandInternalOrAHundredExternalLinksHasTooMany()
    {
        var site = new RuleHarness();
        string? Detail(string path, string body)
        {
            var page = site.Page($"{site.Site}/{path}", RuleHarness.Html(body: body));
            return new LinkStructureRules(SETTINGS, site.Table(), site.UrlOf).Check(page).SingleOrDefault(hit => hit.Issue == IssueCatalog.TOO_MANY_LINKS).Detail;
        }
        static string Links(int count, string href) => string.Concat(Enumerable.Repeat($"<a href=\"{href}\">Link</a>", count));

        Assert.Equal("1,001 internal links, over 1,000", Detail("a", Links(1_001, "/x")));
        Assert.Null(Detail("b", Links(1_000, "/x")));
        Assert.Equal("101 links to other sites, over 100", Detail("c", Links(101, "https://example.org/")));
    }

    [Fact]
    public void LinksToOtherSitesThatAnsweredSlowlyAreFlagged()
    {
        var site = new RuleHarness();
        site.Fetched("https://slow.example.org/", FetchOutcome.Success, 200, responseMs: 7_400);
        site.Fetched("https://moving.example.org/", FetchOutcome.Redirect, 301, redirectTo: "https://moving.example.org/new", responseMs: 3_500);
        site.Fetched("https://quick.example.org/", FetchOutcome.Success, 200, responseMs: 2_900);
        site.Fetched("https://down.example.org/", FetchOutcome.Timeout, 0, responseMs: 20_000);
        site.Fetched($"{site.Site}/slow", FetchOutcome.Success, 200, isHtml: true, responseMs: 9_000);
        var page = site.Page($"{site.Site}/", RuleHarness.Html(body: string.Concat(
            new[] { "https://slow.example.org/", "https://moving.example.org/", "https://quick.example.org/", "https://down.example.org/", "/slow" }
                .Select(href => $"<a href=\"{href}\">Link</a>"))));

        var hit = new LinkStructureRules(SETTINGS, site.Table(), site.UrlOf).Check(page).Single(hit => hit.Issue == IssueCatalog.SLOW_EXTERNAL_LINKS);

        Assert.Equal("2 slow links, the slowest 7.4 s", hit.Detail);
        Assert.Equal(["https://slow.example.org/", "https://moving.example.org/"], hit.AllTargets.Select(site.UrlOf));
    }

    [Fact]
    public void ASitemapPageOnlyNofollowLinksLeadToIsAnOrphanAndAPageLinkedOnlyFromNonIndexablePagesIsFlagged()
    {
        var site = new RuleHarness();
        var listed = site.Fetched($"{site.Site}/listed", FetchOutcome.Success, 200, isHtml: true, inSitemap: true);
        var onlyHidden = site.Fetched($"{site.Site}/only-hidden", FetchOutcome.Success, 200, isHtml: true);
        var mixed = site.Fetched($"{site.Site}/mixed", FetchOutcome.Success, 200, isHtml: true);
        var viaRedirect = site.Fetched($"{site.Site}/via-redirect", FetchOutcome.Success, 200, isHtml: true);
        site.Fetched($"{site.Site}/old", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/via-redirect");
        AnalyzedPage[] pages =
        [
            site.Page($"{site.Site}/", RuleHarness.Html(body: "<a href=\"/listed\" rel=\"nofollow\">Listed</a><a href=\"/mixed\">Mixed</a>")),
            site.Page($"{site.Site}/hidden-a", RuleHarness.Html("<meta name=\"robots\" content=\"noindex\">",
                "<a href=\"/only-hidden\">Hidden</a><a href=\"/mixed\">Mixed</a>"), IndexabilityReason.Noindex),
            site.Page($"{site.Site}/copy", RuleHarness.Html($"<link rel=\"canonical\" href=\"{site.Site}/\">", "<a href=\"/only-hidden\">Hidden</a><a href=\"/old\">Old</a>"),
                IndexabilityReason.Canonicalised)
        ];
        var rule = new InlinkRules(site.Table(), crawlComplete: true);
        Array.ForEach(pages, rule.Observe);

        var hits = rule.Finish().ToList();

        Assert.Equal("Only nofollow links lead here", hits.Single(hit => hit.Issue == IssueCatalog.ORPHAN_PAGE && hit.UrlId == listed).Detail);
        Assert.Equal(
        [
            new IssueHit(IssueCatalog.NON_INDEXABLE_INLINKS_ONLY, onlyHidden, "Linked from 2 pages, none of them indexable"),
            new IssueHit(IssueCatalog.NON_INDEXABLE_INLINKS_ONLY, viaRedirect, "Linked from 1 page, which isn't indexable")
        ], hits.Where(hit => hit.Issue == IssueCatalog.NON_INDEXABLE_INLINKS_ONLY));
        Assert.DoesNotContain(hits, hit => hit.UrlId == mixed);
    }

    [Fact]
    public void PaginationLinksThatLeadNowhereUsefulAreFlagged()
    {
        var site = new RuleHarness();
        site.Fetched($"{site.Site}/gone", FetchOutcome.ClientError, 404);
        site.Fetched($"{site.Site}/moved", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/list/2");
        site.Ok($"{site.Site}/hidden", IndexabilityReason.Noindex);
        site.Ok($"{site.Site}/list/2");
        site.Ok($"{site.Site}/list/3");
        AnalyzedPage Page(string path, string head, string body = "") => site.Page($"{site.Site}{path}", RuleHarness.Html(head, body));
        AnalyzedPage[] pages =
        [
            Page("/tags-only", "<link rel=\"next\" href=\"/list/2\">"),
            Page("/to-gone", "<link rel=\"next\" href=\"/gone\"><link rel=\"prev\" href=\"/moved\">"),
            Page("/to-hidden", "<link rel=\"next\" href=\"/hidden\">", "<a href=\"/hidden\">Next</a>")
        ];
        var rule = new PaginationRules(site.Table(), site.Find, site.UrlOf);
        Array.ForEach(pages, rule.Observe);

        var hits = rule.Finish().Where(hit => hit.Issue != IssueCatalog.PAGINATION_SEQUENCE_ERROR).ToList();

        Assert.Equal(
        [
            new IssueHit(IssueCatalog.PAGINATION_NOT_LINKED, pages[0].UrlId, $"rel=\"next\" {site.Site}/list/2 has no link", site.Find($"{site.Site}/list/2")),
            new IssueHit(IssueCatalog.PAGINATION_BROKEN, pages[1].UrlId, "rel=\"next\" returns 404 Not Found", site.Find($"{site.Site}/gone"))
            {
                Targets = [site.Find($"{site.Site}/gone"), site.Find($"{site.Site}/moved")]
            },
            new IssueHit(IssueCatalog.PAGINATION_TO_NON_INDEXABLE, pages[2].UrlId, "rel=\"next\" is noindex", site.Find($"{site.Site}/hidden"))
        ], hits);
    }

    [Fact]
    public void PaginationLinksThatDontMakeASequenceAreFlagged()
    {
        var site = new RuleHarness();
        AnalyzedPage Page(string path, string head) => site.Page($"{site.Site}{path}", RuleHarness.Html(head));
        AnalyzedPage[] pages =
        [
            Page("/two-next", "<link rel=\"next\" href=\"/a\"><link rel=\"next\" href=\"/b\">"),
            Page("/self", "<link rel=\"prev\" href=\"/self\">"),
            Page("/one", "<link rel=\"next\" href=\"/two\">"),
            Page("/two", "<link rel=\"next\" href=\"/three\">"),
            Page("/three", "<link rel=\"prev\" href=\"/two\"><link rel=\"next\" href=\"/four\">"),
            Page("/four", "<link rel=\"prev\" href=\"/three\"><link rel=\"next\" href=\"/three\">"),
            Page("/fine-1", "<link rel=\"next\" href=\"/fine-2\">"),
            Page("/fine-2", "<link rel=\"prev\" href=\"/fine-1\">")
        ];
        site.Ok($"{site.Site}/a");
        site.Ok($"{site.Site}/b");
        var rule = new PaginationRules(site.Table(), site.Find, site.UrlOf);
        Array.ForEach(pages, rule.Observe);

        var faults = rule.Finish().Where(hit => hit.Issue == IssueCatalog.PAGINATION_SEQUENCE_ERROR).ToDictionary(hit => site.UrlOf(hit.UrlId)!, hit => hit.Detail);

        Assert.Equal(new Dictionary<string, string?>
        {
            [$"{site.Site}/two-next"] = "2 rel=\"next\" URLs",
            [$"{site.Site}/self"] = "rel=\"prev\" names the page itself",
            [$"{site.Site}/one"] = "/two doesn't point back",
            [$"{site.Site}/two"] = "rel=\"next\" leads back to /three",
            [$"{site.Site}/three"] = "rel=\"next\" leads back to /three",
            [$"{site.Site}/four"] = "/three doesn't point back"
        }, faults);
        Assert.Equal(["The next page doesn't point back", "rel=\"next\" leads back to an earlier page", "More than one rel=\"next\" or rel=\"prev\"",
            "rel=\"prev\" names the page itself"],
            new[] { "/two doesn't point back", "rel=\"next\" leads back to /three", "2 rel=\"next\" URLs", "rel=\"prev\" names the page itself" }.Select(IssueFaults.PaginationFault));
    }

    [Fact]
    public void TheNewLinkLimitsAreSettingsTheAnalysisValidates()
    {
        Assert.Equal((3, 4, 1_000, 100, 3_000), (SETTINGS.MinInternalOutlinks, SETTINGS.MaxClickDepth, SETTINGS.MaxInternalLinks, SETTINGS.MaxExternalLinks, SETTINGS.SlowExternalLinkMs));
        Assert.Empty(new AnalysisSettings { MinInternalOutlinks = 0 }.Validate());
        Assert.NotEmpty(new AnalysisSettings { MaxClickDepth = 0 }.Validate());
        Assert.NotEmpty(new AnalysisSettings { SlowExternalLinkMs = 0 }.Validate());
        Assert.Equal(("3 links", "4 clicks", "3 seconds"), (PlainLimits.Describe("minInternalOutlinks", SETTINGS), PlainLimits.Describe("maxClickDepth", SETTINGS),
            PlainLimits.Describe("slowExternalLinkMs", SETTINGS)));
    }
}
