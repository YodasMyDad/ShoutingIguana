using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>
/// Step 3b's evidence: what a check's detail names (each title, each canonical with where it's declared, the element that
/// ended the head, a loop's hops, the sections a page links to that are missing), the limit a measurement is over, and the
/// images the image checks name as their targets, so a view can say what to fix without opening the page.
/// </summary>
public sealed class EvidenceDetailTests
{
    private static readonly AnalysisSettings SETTINGS = new();

    [Fact]
    public void MultipleTitlesDescriptionsAndH1sAreEachQuoted()
    {
        var site = new RuleHarness();
        var page = site.Page($"{site.Site}/a", RuleHarness.Html(
            "<title>Garden sofas and chairs | Example Store</title><title>Example</title>" +
            "<meta name=\"description\" content=\"Sofas for every garden, made to last for years outdoors.\"><meta name=\"description\" content=\"Shop online\">",
            "<h1>Sofas</h1><h1>Example</h1><h1>Sign up</h1>"));

        Assert.Equal("2 titles: \"Garden sofas and chairs | Example Store\" · \"Example\"",
            Detail(new TitleRules(SETTINGS), page, IssueCatalog.TITLE_MULTIPLE));
        Assert.Equal("2 meta descriptions: \"Sofas for every garden, made to last fo…\" · \"Shop online\"",
            Detail(new DescriptionRules(SETTINGS), page, IssueCatalog.DESCRIPTION_MULTIPLE));
        Assert.Equal("3 H1s: \"Sofas\" · \"Example\" · \"Sign up\"", Detail(new HeadingRules(), page, IssueCatalog.H1_MULTIPLE));
    }

    [Fact]
    public void AMeasurementSaysTheLimitItIsOver()
    {
        var site = new RuleHarness();
        var longTitle = site.Page($"{site.Site}/{new string('a', 120)}", RuleHarness.Html($"<title>{new string('x', 78)}</title>"), responseMs: 1_250,
            bodyBytes: 3 * 1024 * 1024);
        var shortTitle = site.Page($"{site.Site}/b", RuleHarness.Html("<title>Short page</title>", "<p>A few words.</p>"));

        Assert.Equal("780 px, over 580 (78 characters)", Detail(new TitleRules(SETTINGS), longTitle, IssueCatalog.TITLE_TOO_LONG));
        Assert.Equal("98 px, under 200 (10 characters)", Detail(new TitleRules(SETTINGS), shortTitle, IssueCatalog.TITLE_TOO_SHORT));
        Assert.Matches(@"^\d+ words, under 200$", Detail(new ContentRules(SETTINGS), shortTitle, IssueCatalog.LOW_WORD_COUNT));
        Assert.Equal("140 characters, over 115", Detail(new UrlRules(SETTINGS), longTitle, IssueCatalog.URL_TOO_LONG));
        Assert.StartsWith("1,250 ms, over 1,000 ms (first byte after ", Detail(new PerformanceRules(SETTINGS), longTitle, IssueCatalog.SLOW_RESPONSE));
        Assert.Equal("3.0 MB, over 1 MB", Detail(new PerformanceRules(SETTINGS), longTitle, IssueCatalog.LARGE_PAGE));
        Assert.Equal("3.0 MB, past the 2 MB Google reads", Detail(new PerformanceRules(SETTINGS), longTitle, IssueCatalog.HTML_PAST_CRAWL_LIMIT));

        // The limit is the analysis's, not the default.
        Assert.Equal("780 px, over 700 (78 characters)", Detail(new TitleRules(SETTINGS with { TitleMaxPixels = 700 }), longTitle, IssueCatalog.TITLE_TOO_LONG));
    }

    [Fact]
    public void ASoft404SaysWhichTextReadsAsAnError()
    {
        var site = new RuleHarness();
        var byTitle = site.Page($"{site.Site}/a", RuleHarness.Html("<title>Page not found – Example</title>", "<h1>Sorry</h1>"));
        var byHeading = site.Page($"{site.Site}/b", RuleHarness.Html("<title>Example</title>", "<h1>This product is no longer available</h1>"));

        Assert.Matches("^The title reads as an error: \"Page not found – Example\" · \\d+ words?, under 300$",
            Detail(new ContentRules(SETTINGS), byTitle, IssueCatalog.SOFT_404));
        Assert.Matches("^The H1 reads as an error: \"This product is no longer available\" · \\d+ words?, under 300$",
            Detail(new ContentRules(SETTINGS), byHeading, IssueCatalog.SOFT_404));
    }

    [Fact]
    public void MultipleCanonicalsAreNamedWithWhereEachIsDeclared()
    {
        var site = new RuleHarness();
        var page = site.Page($"{site.Site}/a", RuleHarness.Html($"<link rel=\"canonical\" href=\"{site.Site}/a/\">"),
            headers: [new ResponseHeader("link", $"<{site.Site}/b>; rel=\"canonical\"")]);

        // One on the page's own host is its path, so that two long ones both fit the detail.
        Assert.Equal("2 URLs: /a/ (head) · /b (HTTP header)",
            Detail(new CanonicalRules(site.Table(), site.UrlOf), page, IssueCatalog.CANONICAL_MULTIPLE));
    }

    [Fact]
    public void ALinkOutsideTheHeadNamesWhatEndedItAndWhere()
    {
        var site = new RuleHarness();
        var pixel = site.Page($"{site.Site}/a",
            "<!doctype html>\n<html lang=\"en\">\n<head>\n<title>A page</title>\n<img src=\"/t.gif\">\n" +
            $"<link rel=\"canonical\" href=\"{site.Site}/a\">\n<link rel=\"alternate\" hreflang=\"en\" href=\"{site.Site}/a\">\n</head>\n<body><p>Words</p></body></html>");
        var text = site.Page($"{site.Site}/b",
            $"<!doctype html>\n<html lang=\"en\">\n<head>\n<title>B page</title>\nWelcome\n<link rel=\"canonical\" href=\"{site.Site}/b\">\n</head>\n<body></body></html>");
        var inBody = site.Page($"{site.Site}/c",
            $"<!doctype html>\n<html lang=\"en\">\n<head>\n<title>C page</title>\n</head>\n<body>\n<div><link rel=\"canonical\" href=\"{site.Site}/c\"></div>\n</body></html>");
        var urls = site.Table();
        var canonicals = new CanonicalRules(urls, site.UrlOf);

        Assert.Equal(new HeadEndFact("<img src=\"/t.gif\">", 5, Implicit: true), pixel.Facts.HeadEnd);
        Assert.Equal("<img src=\"/t.gif\"> on line 5 ended the head early", Detail(canonicals, pixel, IssueCatalog.CANONICAL_OUTSIDE_HEAD));
        Assert.Equal("1 entry in the body: <img src=\"/t.gif\"> on line 5 ended the head early",
            Detail(new HreflangRules(urls), pixel, IssueCatalog.HREFLANG_OUTSIDE_HEAD));
        Assert.Equal("Text \"Welcome\" ended the head early", Detail(canonicals, text, IssueCatalog.CANONICAL_OUTSIDE_HEAD));
        Assert.Equal("It's in the body (after <body> on line 6)", Detail(canonicals, inBody, IssueCatalog.CANONICAL_OUTSIDE_HEAD));

        // A page whose links are all in the head notes nothing, so its stored facts are as they were.
        Assert.Null(site.Page($"{site.Site}/d", RuleHarness.Html($"<link rel=\"canonical\" href=\"{site.Site}/d\">")).Facts.HeadEnd);
    }

    [Fact]
    public void HreflangDetailsNameTheEntriesAndConflictsGroupByTheirCodes()
    {
        var site = new RuleHarness();
        var nearly = site.Page($"{site.Site}/en/", RuleHarness.Html(
            "<link rel=\"alternate\" hreflang=\"en\" href=\"http://www.example.com/en\"><link rel=\"alternate\" hreflang=\"de\" href=\"https://example.de/\">"));
        var conflicting = site.Page($"{site.Site}/uk/", RuleHarness.Html(
            $"<link rel=\"alternate\" hreflang=\"en-gb\" href=\"{site.Site}/uk/\"><link rel=\"alternate\" hreflang=\"en-gb\" href=\"{site.Site}/gb/\">" +
            "<link rel=\"alternate\" hreflang=\"x-default\" href=\"https://example.com/uk/\">"));
        var rules = new HreflangRules(site.Table());

        Assert.Equal("None of its 2 entries names this URL; \"en\" names http://www.example.com/en",
            Detail(rules, nearly, IssueCatalog.HREFLANG_MISSING_SELF));
        var duplicate = Detail(rules, conflicting, IssueCatalog.HREFLANG_DUPLICATE_CODE);
        Assert.Equal("en-gb: /uk/ · /gb/", duplicate);
        Assert.Equal("en-gb", IssueCatalog.HREFLANG_DUPLICATE_CODE.FaultOf!(duplicate));
        Assert.Equal("en-gb, de", IssueCatalog.HREFLANG_DUPLICATE_CODE.FaultOf!("en-gb: /uk/ · /gb/; de: /de/ · /a…"));

        // An earlier engine's detail was the codes alone, and groups as it did.
        Assert.Equal("en-gb, de", IssueCatalog.HREFLANG_DUPLICATE_CODE.FaultOf!("en-gb, de"));
    }

    [Fact]
    public void ARedirectLoopNamesItsHops()
    {
        var site = new RuleHarness();
        site.Fetched($"{site.Site}/a/", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/a");
        site.Fetched($"{site.Site}/a", FetchOutcome.Redirect, 302, redirectTo: $"{site.Site}/a/");

        var loop = new RedirectRules(site.Table(), site.UrlOf).Finish().Single(hit => hit.Issue == IssueCatalog.REDIRECT_LOOP && hit.UrlId == site.Find($"{site.Site}/a/"));

        Assert.Equal("2 hops: /a/ (301) → /a (302) → back to /a/", loop.Detail);
    }

    [Fact]
    public void BrokenFragmentLinksNameTheMissingSections()
    {
        var site = new RuleHarness();
        var page = site.Page($"{site.Site}/a", RuleHarness.Html(body:
            "<a href=\"#nowhere\">1</a><a href=\"/b#specs\">2</a><a href=\"/b#specs\">3</a><a href=\"/b#there\">4</a>"));
        var target = site.Page($"{site.Site}/b", RuleHarness.Html(body: "<h2 id=\"there\">There</h2>"));
        var urls = site.Table();
        var links = page.Links.Where(link => link.Fragment is not null && link.ToId != page.UrlId).Select(link => (page.UrlId, link.ToId, link.Fragment!)).ToList();
        var rules = new FragmentRules(urls, id => id == target.UrlId ? target.Facts : null, () => links, site.UrlOf);

        rules.Observe(page);
        rules.Observe(target);

        Assert.Equal("3 links to missing sections: #nowhere, /b#specs", rules.Finish().Single().Detail);
    }

    [Fact]
    public void TheImageChecksNameEachImageAtFault()
    {
        var site = new RuleHarness();
        var page = site.Page($"{site.Site}/", RuleHarness.Html(body:
            "<img src=\"/logo.png\" width=\"100\" height=\"40\">" +
            $"<img src=\"/hero.jpg\" alt=\"{new string('a', 130)}\">" +
            "<img src=\"/logo.png\" width=\"100\" height=\"40\">" +
            "<img src=\"/fine.jpg\" alt=\"Fine\" width=\"1\" height=\"2\">"));
        var urls = site.Table();
        var (logo, hero) = (site.Find($"{site.Site}/logo.png"), site.Find($"{site.Site}/hero.jpg"));

        var hits = new ImageRules(SETTINGS, urls, site.Find).Check(page).ToList();

        // The logo is shown twice: counted twice, named once.
        var withoutAlt = hits.Single(hit => hit.Issue == IssueCatalog.IMAGE_MISSING_ALT);
        Assert.Equal(("2 images", logo), (withoutAlt.Detail, withoutAlt.RelatedUrlId));
        Assert.Equal([logo], withoutAlt.AllTargets);
        Assert.Equal([hero], hits.Single(hit => hit.Issue == IssueCatalog.IMAGE_ALT_TOO_LONG).AllTargets);
        Assert.Equal([hero], hits.Single(hit => hit.Issue == IssueCatalog.IMAGE_MISSING_DIMENSIONS).AllTargets);

        // An image the crawl has no URL for is counted, and puts the page in the group of issues naming no image.
        var unnamed = new ImageRules(SETTINGS, urls, url => url.EndsWith("logo.png", StringComparison.Ordinal) ? 0 : site.Find(url)).Check(page).ToList();
        var withoutId = unnamed.Single(hit => hit.Issue == IssueCatalog.IMAGE_MISSING_ALT);
        Assert.Equal(("2 images", (int?)null), (withoutId.Detail, withoutId.RelatedUrlId));
        Assert.Equal([IssueHit.NO_OTHER_URL], withoutId.AllTargets);
    }

    [Fact]
    public void AnOversizedImagesDetailSaysTheLargest()
    {
        var site = new RuleHarness();
        site.Fetched($"{site.Site}/huge.jpg", FetchOutcome.Success, 200, size: 1_887_436);
        site.Fetched($"{site.Site}/big.jpg", FetchOutcome.Success, 200, size: 300_000);
        var two = site.Page($"{site.Site}/a", RuleHarness.Html(body: "<img src=\"/big.jpg\" alt=\"x\" width=\"1\" height=\"2\"><img src=\"/huge.jpg\" alt=\"x\" width=\"1\" height=\"2\">"));
        var one = site.Page($"{site.Site}/b", RuleHarness.Html(body: "<img src=\"/big.jpg\" alt=\"x\" width=\"1\" height=\"2\">"));
        var rules = new ImageRules(SETTINGS, site.Table(), site.Find);

        Assert.Equal("2 images over 200 KB, the largest 1.8 MB", Detail(rules, two, IssueCatalog.IMAGE_TOO_LARGE));
        Assert.Equal("1 image over 200 KB: 293 KB", Detail(rules, one, IssueCatalog.IMAGE_TOO_LARGE));
    }

    [Fact]
    public void ADetailThatQuotesTheSiteIsCutToOneLine()
    {
        var site = new RuleHarness();
        var titles = string.Concat(Enumerable.Range(1, 6).Select(index => $"<title>Title number {index} of a page with far too many titles in it</title>"));
        var detail = Detail(new TitleRules(SETTINGS), site.Page($"{site.Site}/a", RuleHarness.Html(titles)), IssueCatalog.TITLE_MULTIPLE);

        Assert.Equal(RuleText.DETAIL_MAX, detail.Length);
        Assert.EndsWith("…", detail, StringComparison.Ordinal);
    }

    private static string Detail(IPageRule rule, AnalyzedPage page, IssueDefinition check) => rule.Check(page).Single(hit => hit.Issue == check).Detail!;
}
