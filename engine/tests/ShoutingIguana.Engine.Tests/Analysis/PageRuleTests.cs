using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>Page rules against real parsed HTML, with an eye on the false positives that make checks untrustworthy.</summary>
public sealed class PageRuleTests
{
    private static readonly AnalysisSettings SETTINGS = new();

    [Fact]
    public void TitleLengthsAreMeasuredInCharactersAndOnlyOnIndexablePages()
    {
        var site = new RuleHarness();
        var rules = new TitleRules(SETTINGS);

        Assert.Equal([IssueCatalog.TITLE_MISSING], Issues(rules, site.Page($"{site.Site}/a", RuleHarness.Html())));
        Assert.Equal([IssueCatalog.TITLE_TOO_SHORT], Issues(rules, site.Page($"{site.Site}/b", RuleHarness.Html("<title>Café</title>"))));
        Assert.Equal([IssueCatalog.TITLE_TOO_LONG],
            Issues(rules, site.Page($"{site.Site}/c", RuleHarness.Html($"<title>{new string('x', 61)}</title>"))));
        Assert.Equal([IssueCatalog.TITLE_MULTIPLE],
            Issues(rules, site.Page($"{site.Site}/d", RuleHarness.Html("<title>A perfectly sized page title here</title><title>Second one</title>"))));
        Assert.Empty(Issues(rules, site.Page($"{site.Site}/e", RuleHarness.Html(), IndexabilityReason.Noindex)));
    }

    [Theory]
    [InlineData("Page not found | Example", true)]
    [InlineData("404 | Example", true)]
    [InlineData("Error 404 - Example", true)]
    [InlineData("Sorry, this product is no longer available", true)]
    [InlineData("How to fix a 404 on your site", false)]
    [InlineData("Model 404 desk lamp", false)]
    public void SoftNotFoundPagesNeedErrorWording(string title, bool flagged)
    {
        var site = new RuleHarness();
        var page = site.Page($"{site.Site}/", RuleHarness.Html($"<title>{title}</title>", $"<h1>{title}</h1><p>{Words(250)}</p>"));

        Assert.Equal(flagged, Issues(new ContentRules(SETTINGS), page).Contains(IssueCatalog.SOFT_404));
    }

    [Fact]
    public void ImagesNeedAltAttributesButEmptyAltAndTrackingPixelsAreFine()
    {
        var site = new RuleHarness();
        var page = site.Page($"{site.Site}/", RuleHarness.Html(body:
            "<img src=\"/a.jpg\" width=\"10\" height=\"10\">" +
            "<img src=\"/decorative.jpg\" alt=\"\" width=\"10\" height=\"10\">" +
            "<img src=\"/pixel.gif\" width=\"1\" height=\"1\">" +
            "<img src=\"/no-size.jpg\" alt=\"A photo\">"));

        var hits = new ImageRules(SETTINGS, site.Table()).Check(page).ToList();

        Assert.Equal("1 image", hits.Single(hit => hit.Issue == IssueCatalog.IMAGE_MISSING_ALT).Detail);
        Assert.Equal("1 image", hits.Single(hit => hit.Issue == IssueCatalog.IMAGE_MISSING_DIMENSIONS).Detail);
    }

    [Fact]
    public void BrokenAndOversizedImagesComeFromTheCrawledStatus()
    {
        var site = new RuleHarness();
        site.Fetched($"{site.Site}/gone.jpg", FetchOutcome.ClientError, 404);
        site.Fetched($"{site.Site}/huge.jpg", FetchOutcome.Success, 200, size: 900_000);
        site.Fetched($"{site.Site}/small.jpg", FetchOutcome.Success, 200, size: 20_000);
        var page = site.Page($"{site.Site}/", RuleHarness.Html(body:
            "<img src=\"/gone.jpg\" alt=\"x\" width=\"1\" height=\"2\"><img src=\"/huge.jpg\" alt=\"x\" width=\"1\" height=\"2\"><img src=\"/small.jpg\" alt=\"x\" width=\"1\" height=\"2\">"));

        var issues = Issues(new ImageRules(SETTINGS, site.Table()), page);

        Assert.Equal([IssueCatalog.IMAGE_BROKEN, IssueCatalog.IMAGE_TOO_LARGE], issues);
    }

    [Fact]
    public void ExternalLinksThatRefuseBotsAreNotBroken()
    {
        var site = new RuleHarness();
        site.Fetched("https://other.example/forbidden", FetchOutcome.ClientError, 403);
        site.Fetched("https://linkedin.example/in/someone", FetchOutcome.ServerError, 999);
        site.Fetched("https://other.example/gone", FetchOutcome.ClientError, 404);
        site.Fetched($"{site.Site}/old", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/new");
        var page = site.Page($"{site.Site}/", RuleHarness.Html(body:
            "<a href=\"https://other.example/forbidden\">a</a><a href=\"https://linkedin.example/in/someone\">b</a>" +
            "<a href=\"https://other.example/gone\">c</a><a href=\"/old\">d</a><a href=\"/private\" rel=\"nofollow\">e</a><a href=\"/x\"></a>"));

        var hits = new LinkRules(site.Table()).Check(page).ToList();

        Assert.Equal("1 broken URL", hits.Single(hit => hit.Issue == IssueCatalog.BROKEN_EXTERNAL_LINKS).Detail);
        Assert.Contains(hits, hit => hit.Issue == IssueCatalog.LINKS_TO_REDIRECTS);
        Assert.Contains(hits, hit => hit.Issue == IssueCatalog.INTERNAL_NOFOLLOW_LINKS);
        Assert.Contains(hits, hit => hit.Issue == IssueCatalog.EMPTY_ANCHOR_TEXT);
        Assert.DoesNotContain(hits, hit => hit.Issue == IssueCatalog.BROKEN_INTERNAL_LINKS);
    }

    [Fact]
    public void UppercaseIgnoresPercentEscapes()
    {
        var site = new RuleHarness();
        var rules = new UrlRules(SETTINGS);

        Assert.Empty(Issues(rules, site.Page($"{site.Site}/caf%C3%A9/a%2Fb", RuleHarness.Html())));
        Assert.Equal([IssueCatalog.URL_UPPERCASE, IssueCatalog.URL_UNDERSCORES], Issues(rules, site.Page($"{site.Site}/About_Us", RuleHarness.Html())));
        Assert.Equal([IssueCatalog.URL_TOO_LONG], Issues(rules, site.Page($"{site.Site}/{new string('a', 120)}", RuleHarness.Html())));
    }

    [Fact]
    public void CharsetCanComeFromTheContentTypeHeader()
    {
        var site = new RuleHarness();
        var html = "<!DOCTYPE html><html lang=\"en\"><head><meta name=\"viewport\" content=\"width=device-width\"></head><body></body></html>";
        var rules = new HtmlRules();

        var withoutHeader = Issues(rules, site.Page($"{site.Site}/a", html, IndexabilityReason.Noindex));
        var withHeader = Issues(rules, site.Page($"{site.Site}/b", html, IndexabilityReason.Noindex, [new ResponseHeader("content-type", "text/html; charset=utf-8")]));

        Assert.Equal([IssueCatalog.CHARSET_MISSING], withoutHeader);
        Assert.Empty(withHeader);
        Assert.Contains(IssueCatalog.LANG_MISSING, Issues(rules, site.Page($"{site.Site}/c", RuleHarness.Html(lang: string.Empty), IndexabilityReason.Noindex)));
    }

    [Fact]
    public void StructuredDataHandlesTypeArraysGraphsAndLenientJson()
    {
        var site = new RuleHarness();
        var rules = new StructuredDataRules();
        string Script(string json) => $"<script type=\"application/ld+json\">{json}</script>";

        var faq = site.Page($"{site.Site}/a", RuleHarness.Html(Script("""{"@context":"https://schema.org","@type":["WebPage","FAQPage"],"mainEntity":[{"@type":"Question"}],}""")));
        var graph = site.Page($"{site.Site}/b", RuleHarness.Html(Script("""{"@context":"https://schema.org","@graph":[{"@type":"Organization","name":"Acme"},{"@type":"schema:Product","name":"Widget"}]}""")));
        var broken = site.Page($"{site.Site}/c", RuleHarness.Html(Script("""{"@type": "Product" "name": "x"}""")));
        var noindex = site.Page($"{site.Site}/d", RuleHarness.Html(Script("""{"@type":"Product"}""")), IndexabilityReason.Noindex);

        Assert.Empty(Issues(rules, faq));
        var missing = rules.Check(graph).Single();
        Assert.Equal(IssueCatalog.STRUCTURED_DATA_MISSING_REQUIRED, missing.Issue);
        Assert.Equal("Product: needs offers or review or aggregateRating", missing.Detail);
        Assert.Equal([IssueCatalog.STRUCTURED_DATA_INVALID], Issues(rules, broken));
        Assert.Empty(Issues(rules, noindex));
    }

    [Fact]
    public void CanonicalChecksTellRelativeInvalidAndConflictingApart()
    {
        var site = new RuleHarness();
        var sameTwice = site.Page($"{site.Site}/a", RuleHarness.Html($"<link rel=\"canonical\" href=\"{site.Site}/a\"><link rel=\"canonical\" href=\"{site.Site}/a\">"));
        var conflicting = site.Page($"{site.Site}/b", RuleHarness.Html($"<link rel=\"canonical\" href=\"{site.Site}/b\"><link rel=\"canonical\" href=\"{site.Site}/z\">"));
        var relative = site.Page($"{site.Site}/c", RuleHarness.Html("<link rel=\"canonical\" href=\"/c\">"));
        var invalid = site.Page($"{site.Site}/d", RuleHarness.Html("<link rel=\"canonical\" href=\"http://[nope\">"));
        var none = site.Page($"{site.Site}/e", RuleHarness.Html());
        var noneButNoindex = site.Page($"{site.Site}/f", RuleHarness.Html(), IndexabilityReason.Noindex);
        var rules = new CanonicalRules(site.Table());

        Assert.Empty(Issues(rules, sameTwice));
        Assert.Equal([IssueCatalog.CANONICAL_MULTIPLE], Issues(rules, conflicting));
        Assert.Equal([IssueCatalog.CANONICAL_RELATIVE], Issues(rules, relative));
        Assert.Equal([IssueCatalog.CANONICAL_INVALID], Issues(rules, invalid));
        Assert.Equal([IssueCatalog.CANONICAL_MISSING], Issues(rules, none));
        Assert.Empty(Issues(rules, noneButNoindex));
    }

    [Theory]
    [InlineData("nosnippet", true)]
    [InlineData("max-snippet:0", true)]
    [InlineData("max-snippet: 0", true)]
    [InlineData("max-snippet:50", false)]
    [InlineData("noarchive", false)]
    public void SnippetDirectives(string directive, bool flagged)
    {
        var site = new RuleHarness();
        var page = site.Page($"{site.Site}/", RuleHarness.Html($"<meta name=\"robots\" content=\"{directive}\">"));

        Assert.Equal(flagged, Issues(new IndexabilityRules(), page).Contains(IssueCatalog.SNIPPET_BLOCKED));
    }

    [Fact]
    public void HttpsSitesAreCheckedForHeadersMixedContentAndStrayHttpPages()
    {
        var site = new RuleHarness();
        var home = site.Page($"{site.Site}/", RuleHarness.Html(body: "<img src=\"http://cdn.example/a.jpg\" alt=\"x\" width=\"1\" height=\"2\"><script src=\"https://cdn.example/app.js\"></script>"),
            headers: [new ResponseHeader("x-content-type-options", "nosniff"), new ResponseHeader("content-security-policy", "frame-ancestors 'none'")]);
        var stray = site.Page("http://example.com/old-page", RuleHarness.Html());
        var rules = new SecurityRules(site.Table());

        var homeHits = rules.Check(home).ToList();

        Assert.Equal("Missing Strict-Transport-Security, Referrer-Policy", homeHits.Single(hit => hit.Issue == IssueCatalog.SECURITY_HEADERS_MISSING).Detail);
        Assert.Equal("1 HTTP resource", homeHits.Single(hit => hit.Issue == IssueCatalog.MIXED_CONTENT).Detail);
        Assert.DoesNotContain(homeHits, hit => hit.Issue == IssueCatalog.NO_HTTPS);
        Assert.Equal([IssueCatalog.HTTP_PAGE], Issues(rules, stray));
    }

    [Fact]
    public void PlainHttpSitesGetOneSiteLevelIssue()
    {
        var site = new RuleHarness("http://example.com");
        var home = site.Page($"{site.Site}/", RuleHarness.Html(), headers:
        [
            new ResponseHeader("x-content-type-options", "nosniff"), new ResponseHeader("x-frame-options", "DENY"),
            new ResponseHeader("content-security-policy", "default-src 'self'"), new ResponseHeader("referrer-policy", "no-referrer")
        ]);
        var other = site.Page($"{site.Site}/about", RuleHarness.Html());
        var rules = new SecurityRules(site.Table());

        Assert.Equal([IssueCatalog.NO_HTTPS], Issues(rules, home));
        Assert.Empty(Issues(rules, other));
    }

    private static List<IssueDefinition> Issues(IPageRule rule, AnalyzedPage page) => rule.Check(page).Select(hit => hit.Issue).ToList();

    private static string Words(int count) => string.Join(' ', Enumerable.Range(0, count).Select(index => $"word{index % 50}"));
}
