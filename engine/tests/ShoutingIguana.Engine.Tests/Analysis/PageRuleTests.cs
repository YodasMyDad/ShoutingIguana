using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing;
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

        // 55 characters but 65 UTF-16 units: each é is an e and a combining accent.
        var accented = string.Concat(Enumerable.Repeat("é", 10)) + new string('x', 45);
        Assert.True(accented.Length > SETTINGS.TitleMaxLength);
        Assert.Empty(Issues(rules, site.Page($"{site.Site}/f", RuleHarness.Html($"<title>{accented}</title>"))));
    }

    [Fact]
    public void FullWidthCharactersAndEmojiTakeTwiceTheRoomInTitles()
    {
        var site = new RuleHarness();
        var rules = new TitleRules(SETTINGS);
        List<IssueHit> Hits(string path, string title) => rules.Check(site.Page($"{site.Site}/{path}", RuleHarness.Html($"<title>{title}</title>"))).ToList();

        // 21 Japanese characters are a normal title; 48 run past what results show.
        Assert.Empty(Hits("a", "合成ストアの全角タイトル付き試験ページです"));
        Assert.Equal([new IssueHit(IssueCatalog.TITLE_TOO_LONG, site.Id($"{site.Site}/b"), "48 characters (96 counting full-width characters as two), over 60")],
            Hits("b", string.Concat(Enumerable.Repeat("日本語の題名", 8))));

        // An emoji with a skin tone is one character (4 UTF-16 units) taking two widths.
        Assert.Equal("31 characters (62 counting full-width characters as two), over 60",
            Hits("c", string.Concat(Enumerable.Repeat("\U0001F44D\U0001F3FD", 31))).Single(hit => hit.Issue == IssueCatalog.TITLE_TOO_LONG).Detail);
        Assert.Equal("14 characters, under 30", Hits("d", "Hello, Mädchen").Single(hit => hit.Issue == IssueCatalog.TITLE_TOO_SHORT).Detail);
    }

    [Fact]
    public void DescriptionLengthsCountFullWidthCharactersAsTwo()
    {
        var site = new RuleHarness();
        var rules = new DescriptionRules(SETTINGS);
        List<IssueDefinition> Issues(string path, string description) =>
            rules.Check(site.Page($"{site.Site}/{path}", RuleHarness.Html($"<meta name=\"description\" content=\"{description}\">"))).Select(hit => hit.Issue).ToList();

        // 40 full-width characters fill as much of a result as 80 Latin ones.
        Assert.Empty(Issues("a", string.Concat(Enumerable.Repeat("全角文字の説明", 6))[..40]));
        Assert.Equal([IssueCatalog.DESCRIPTION_TOO_SHORT], Issues("b", new string('x', 40)));
        Assert.Equal([IssueCatalog.DESCRIPTION_TOO_LONG], Issues("c", string.Concat(Enumerable.Repeat("説明", 40))));
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

        var hits = new ImageRules(SETTINGS, site.Table(), site.Find).Check(page).ToList();

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

        var issues = Issues(new ImageRules(SETTINGS, site.Table(), site.Find), page);

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
        Assert.Equal("Links to 1 URL", hits.Single(hit => hit.Issue == IssueCatalog.EMPTY_ANCHOR_TEXT).Detail);
        Assert.DoesNotContain(hits, hit => hit.Issue == IssueCatalog.BROKEN_INTERNAL_LINKS);
    }

    [Fact]
    public void LinksAndImagesAreJudgedByWhereTheirRedirectsEnd()
    {
        var site = new RuleHarness();
        site.Fetched($"{site.Site}/old", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/gone");
        site.Fetched($"{site.Site}/gone", FetchOutcome.ClientError, 404);
        site.Fetched($"{site.Site}/loop-a", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/loop-b");
        site.Fetched($"{site.Site}/loop-b", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/loop-a");
        site.Fetched($"{site.Site}/moved", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/fine");
        site.Ok($"{site.Site}/fine");
        site.Fetched("https://partner.example/old", FetchOutcome.Redirect, 301, redirectTo: "https://partner.example/gone");
        site.Fetched("https://partner.example/gone", FetchOutcome.ClientError, 404);
        site.Fetched("https://partner.example/login", FetchOutcome.Redirect, 302, redirectTo: "https://partner.example/members");
        site.Fetched("https://partner.example/members", FetchOutcome.ClientError, 403);
        site.Fetched("https://cdn.example/photo.jpg", FetchOutcome.Redirect, 301, redirectTo: "https://cdn.example/missing.jpg");
        site.Fetched("https://cdn.example/missing.jpg", FetchOutcome.ClientError, 404);
        site.Fetched($"{site.Site}/big.jpg", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/big-final.jpg");
        site.Fetched($"{site.Site}/big-final.jpg", FetchOutcome.Success, 200, size: 900_000);
        var page = site.Page($"{site.Site}/", RuleHarness.Html(body:
            "<a href=\"/old\">a</a><a href=\"/loop-a\">b</a><a href=\"/moved\">c</a>" +
            "<a href=\"https://partner.example/old\">d</a><a href=\"https://partner.example/login\">e</a>" +
            "<img src=\"https://cdn.example/photo.jpg\" alt=\"x\" width=\"1\" height=\"2\"><img src=\"/big.jpg\" alt=\"x\" width=\"1\" height=\"2\">"));
        var urls = site.Table();

        var links = new LinkRules(urls).Check(page).ToList();
        var images = new ImageRules(SETTINGS, urls, site.Find).Check(page).ToList();

        Assert.Equal(new IssueHit(IssueCatalog.BROKEN_INTERNAL_LINKS, page.UrlId, "2 broken URLs (the first redirects to a URL that returns 404 Not Found)",
            site.Id($"{site.Site}/old")) { Targets = [site.Id($"{site.Site}/old"), site.Id($"{site.Site}/loop-a")] },
            links.Single(hit => hit.Issue == IssueCatalog.BROKEN_INTERNAL_LINKS));
        Assert.Equal(new IssueHit(IssueCatalog.LINKS_TO_REDIRECTS, page.UrlId, "1 redirecting URL", site.Id($"{site.Site}/moved")),
            links.Single(hit => hit.Issue == IssueCatalog.LINKS_TO_REDIRECTS));

        // A partner's login redirect ending in a 403 may well work in a browser.
        Assert.Equal("1 broken URL (it redirects to a URL that returns 404 Not Found)", links.Single(hit => hit.Issue == IssueCatalog.BROKEN_EXTERNAL_LINKS).Detail);
        Assert.Equal("1 image (it redirects to a URL that returns 404 Not Found)", images.Single(hit => hit.Issue == IssueCatalog.IMAGE_BROKEN).Detail);
        Assert.Equal(site.Id($"{site.Site}/big.jpg"), images.Single(hit => hit.Issue == IssueCatalog.IMAGE_TOO_LARGE).RelatedUrlId);
    }

    /// <summary>
    /// A page with several targets of one kind names every one, in the order it links to them, each once, so each target's
    /// group on the Issues screen holds the page: not only its first, which the issue's related URL keeps.
    /// </summary>
    [Fact]
    public void AnIssueNamesEveryTargetNotJustTheFirst()
    {
        var site = new RuleHarness();
        foreach (var gone in new[] { "gone-1", "gone-2", "gone-3" })
        {
            site.Fetched($"{site.Site}/{gone}", FetchOutcome.ClientError, 404);
        }

        site.Fetched("https://other.example/a", FetchOutcome.ClientError, 410);
        site.Fetched("https://other.example/b", FetchOutcome.Timeout, 0);
        site.Fetched($"{site.Site}/moved-1", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/fine");
        site.Fetched($"{site.Site}/moved-2", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/fine");
        site.Ok($"{site.Site}/fine");
        site.Fetched($"{site.Site}/big-1.jpg", FetchOutcome.Success, 200, size: 900_000, contentType: "image/jpeg");
        site.Fetched($"{site.Site}/big-2.jpg", FetchOutcome.Success, 200, size: 300_000, contentType: "image/jpeg");
        site.Fetched($"{site.Site}/missing-1.jpg", FetchOutcome.ClientError, 404);
        site.Fetched($"{site.Site}/missing-2.jpg", FetchOutcome.ClientError, 404);
        site.Fetched("http://cdn.example/a.js", FetchOutcome.Success, 200, contentType: "text/javascript");
        site.Fetched("http://cdn.example/b.css", FetchOutcome.Success, 200, contentType: "text/css");
        var page = site.Page($"{site.Site}/", RuleHarness.Html(
            """<script src="http://cdn.example/a.js"></script><link rel="stylesheet" href="http://cdn.example/b.css">""",
            """
            <a href="/gone-2">a</a><a href="/gone-1">b</a><a href="/gone-2">c</a><a href="/gone-3">d</a>
            <a href="https://other.example/a">e</a><a href="https://other.example/b">f</a>
            <a href="/moved-2">g</a><a href="/moved-1">h</a>
            <a href="/fine" rel="nofollow">i</a><a href="/moved-1" rel="nofollow">j</a>
            <a href="/fine"></a><a href="/gone-3"><img src="/missing-1.jpg"></a>
            <img src="/big-2.jpg" alt="x" width="1" height="2"><img src="/big-1.jpg" alt="x" width="1" height="2"><img src="/big-2.jpg" alt="y" width="1" height="2">
            <img src="/missing-2.jpg" alt="x" width="1" height="2"><img src="/missing-1.jpg" alt="x" width="1" height="2">
            """));
        var urls = site.Table();
        int Id(string path) => site.Id(path.StartsWith("http", StringComparison.Ordinal) ? path : $"{site.Site}{path}");
        var hits = new LinkRules(urls).Check(page).Concat(new ImageRules(SETTINGS, urls, site.Find).Check(page)).Concat(new SecurityRules(urls).Check(page))
            .ToDictionary(hit => hit.Issue);

        (IssueDefinition Check, string[] Targets)[] expected =
        [
            (IssueCatalog.BROKEN_INTERNAL_LINKS, ["/gone-2", "/gone-1", "/gone-3"]),
            (IssueCatalog.BROKEN_EXTERNAL_LINKS, ["https://other.example/a", "https://other.example/b"]),
            (IssueCatalog.LINKS_TO_REDIRECTS, ["/moved-2", "/moved-1"]),
            (IssueCatalog.INTERNAL_NOFOLLOW_LINKS, ["/fine", "/moved-1"]),
            (IssueCatalog.EMPTY_ANCHOR_TEXT, ["/fine", "/gone-3"]),
            (IssueCatalog.IMAGE_BROKEN, ["/missing-1.jpg", "/missing-2.jpg"]),
            (IssueCatalog.IMAGE_TOO_LARGE, ["/big-2.jpg", "/big-1.jpg"]),
            (IssueCatalog.MIXED_CONTENT, ["http://cdn.example/a.js", "http://cdn.example/b.css"])
        ];
        foreach (var (check, targets) in expected)
        {
            var hit = hits[check];
            Assert.Equal([.. targets.Select(Id)], hit.AllTargets);
            Assert.Equal(Id(targets[0]), hit.RelatedUrlId);
            Assert.StartsWith($"{targets.Length} ", hit.Detail!.Replace("Links to ", string.Empty, StringComparison.Ordinal));
        }

        // Hreflang alternates, broken and not indexable.
        site.Fetched($"{site.Site}/fr", FetchOutcome.ClientError, 404);
        site.Fetched($"{site.Site}/de", FetchOutcome.ServerError, 503);
        site.Fetched($"{site.Site}/es", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/fine");
        site.Ok($"{site.Site}/it", IndexabilityReason.Noindex);
        string Alternate(string code, string path) => $"<link rel=\"alternate\" hreflang=\"{code}\" href=\"{site.Site}{path}\">";
        var en = site.Page($"{site.Site}/en", RuleHarness.Html(
            Alternate("en", "/en") + Alternate("x-default", "/en") + Alternate("de", "/de") + Alternate("es", "/es") + Alternate("fr", "/fr") + Alternate("it", "/it")));
        var hreflang = new HreflangRules(site.Table()).Check(en).ToDictionary(hit => hit.Issue);
        Assert.Equal([Id("/de"), Id("/fr")], hreflang[IssueCatalog.HREFLANG_BROKEN_TARGET].AllTargets);
        Assert.Equal([Id("/es"), Id("/it")], hreflang[IssueCatalog.HREFLANG_TO_NON_INDEXABLE].AllTargets);
    }

    [Fact]
    public void NofollowLinksToImagesAndOtherFilesAreHarmless()
    {
        var site = new RuleHarness();
        site.Fetched($"{site.Site}/gallery/large", FetchOutcome.Success, 200, contentType: "image/jpeg");
        site.Fetched($"{site.Site}/style", FetchOutcome.Success, 200, contentType: "text/css; charset=utf-8");
        site.Fetched($"{site.Site}/pages/a.jpg", FetchOutcome.Success, 200, isHtml: true);
        var page = site.Page($"{site.Site}/", RuleHarness.Html(body:
            // A lightbox link to the full-size photo is never fetched, so its extension decides.
            "<a href=\"/media/2474/mon-2.jpg?width=1400&upscale=false\" rel=\"nofollow\" class=\"lightbox\"><img src=\"/media/2474/mon-2.jpg\" alt=\"x\"></a>" +
            "<a href=\"/clip.MP4\" rel=\"nofollow\">Clip</a><a href=\"/gallery/large\" rel=\"nofollow\">Large</a><a href=\"/style\" rel=\"nofollow\">CSS</a>" +
            "<a href=\"/guide.pdf\" rel=\"nofollow\">Guide</a><a href=\"/download?file=a.jpg\" rel=\"nofollow\">Download</a>" +
            "<a href=\"/pages/a.jpg\" rel=\"nofollow\">A page despite its name</a><a href=\"/private\" rel=\"nofollow\">Private</a>"));

        var hit = new LinkRules(site.Table()).Check(page).Single(found => found.Issue == IssueCatalog.INTERNAL_NOFOLLOW_LINKS);

        // PDFs can be indexed, a query isn't a path, and a fetched URL is judged by what it returned.
        Assert.Equal(new IssueHit(IssueCatalog.INTERNAL_NOFOLLOW_LINKS, page.UrlId, "Links to 4 URLs", site.Id($"{site.Site}/guide.pdf"))
        {
            Targets = [site.Id($"{site.Site}/guide.pdf"), site.Id($"{site.Site}/download?file=a.jpg"), site.Id($"{site.Site}/pages/a.jpg"), site.Id($"{site.Site}/private")]
        }, hit);
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

        // A header that names the parameter and gives it no value declares nothing, as the fetch reads it.
        Assert.All(new[] { "text/html; charset=", "text/html; charset=\"\"", "text/html; x-charset=utf-8", "text/html;" }, (contentType, index) =>
            Assert.Equal([IssueCatalog.CHARSET_MISSING],
                Issues(rules, site.Page($"{site.Site}/empty-{index}", html, IndexabilityReason.Noindex, [new ResponseHeader("content-type", contentType)]))));
        Assert.Empty(Issues(rules, site.Page($"{site.Site}/quoted", html, IndexabilityReason.Noindex, [new ResponseHeader("content-type", "text/html;charset=\"utf-8\"")])));
        Assert.Contains(IssueCatalog.LANG_MISSING, Issues(rules, site.Page($"{site.Site}/c", RuleHarness.Html(lang: string.Empty), IndexabilityReason.Noindex)));
    }

    /// <summary>
    /// A byte-order mark at the start of the file declares the encoding, and beats a meta tag and the header: a page with
    /// one, UTF-8 or UTF-16, isn't missing its character encoding, though it has no meta tag and no charset in its header.
    /// </summary>
    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16le")]
    [InlineData("utf-16be")]
    public void AByteOrderMarkDeclaresTheCharacterEncoding(string byteOrderMark)
    {
        var site = new RuleHarness();
        var html = "<!DOCTYPE html><html lang=\"en\"><head><meta name=\"viewport\" content=\"width=device-width\"></head><body></body></html>";
        var rules = new HtmlRules();

        var marked = Issues(rules, site.Page($"{site.Site}/marked", html, IndexabilityReason.Noindex, [new ResponseHeader("content-type", "text/html")], byteOrderMark: byteOrderMark));
        var unmarked = Issues(rules, site.Page($"{site.Site}/unmarked", html, IndexabilityReason.Noindex, [new ResponseHeader("content-type", "text/html")]));

        Assert.Empty(marked);
        Assert.Equal([IssueCatalog.CHARSET_MISSING], unmarked);
    }

    [Fact]
    public void StructuredDataRequiresValidJsonAndThePropertiesGoogleStillRequires()
    {
        var site = new RuleHarness();
        var rules = new StructuredDataRules();
        string Script(string json) => $"<script type=\"application/ld+json\">{json}</script>";

        // Google stopped showing FAQ rich results, so a FAQPage needs nothing.
        var faq = site.Page($"{site.Site}/a", RuleHarness.Html(Script("""{"@context":"https://schema.org","@type":"FAQPage"}""")));
        var graph = site.Page($"{site.Site}/b", RuleHarness.Html(Script("""{"@context":"https://schema.org","@graph":[{"@type":"Organization","name":"Acme"},{"@type":"schema:Product","name":"Widget"}]}""")));
        var broken = site.Page($"{site.Site}/c", RuleHarness.Html(Script("""{"@type": "Product" "name": "x"}""")));
        var noindex = site.Page($"{site.Site}/d", RuleHarness.Html(Script("""{"@type":"Product"}""")), IndexabilityReason.Noindex);
        var trailingComma = site.Page($"{site.Site}/e", RuleHarness.Html(Script("""{"@type":"Organization","name":"Acme",}""")));
        var comment = site.Page($"{site.Site}/f", RuleHarness.Html(Script("""{"@type":"Organization","name":"Acme" /* note */}""")));
        var remoteJob = site.Page($"{site.Site}/g", RuleHarness.Html(Script(
            """{"@context":"https://schema.org","@type":"JobPosting","title":"Engineer","description":"Build things","datePosted":"2026-01-01","hiringOrganization":{"@type":"Organization","name":"Acme"},"jobLocationType":"TELECOMMUTE"}""")));
        var jobWithoutLocation = site.Page($"{site.Site}/h", RuleHarness.Html(Script(
            """{"@context":"https://schema.org","@type":"JobPosting","title":"Engineer","description":"Build things","datePosted":"2026-01-01","hiringOrganization":{"@type":"Organization","name":"Acme"}}""")));

        Assert.Empty(Issues(rules, faq));
        var missing = rules.Check(graph).Single();
        Assert.Equal(IssueCatalog.STRUCTURED_DATA_MISSING_REQUIRED, missing.Issue);
        Assert.Equal("Product: needs offers or review or aggregateRating", missing.Detail);
        Assert.Equal([IssueCatalog.STRUCTURED_DATA_INVALID], Issues(rules, broken));
        Assert.Empty(Issues(rules, noindex));
        Assert.Equal([IssueCatalog.STRUCTURED_DATA_INVALID], Issues(rules, trailingComma));
        Assert.Equal([IssueCatalog.STRUCTURED_DATA_INVALID], Issues(rules, comment));
        Assert.Empty(Issues(rules, remoteJob));
        Assert.Equal("JobPosting: needs jobLocation or jobLocationType", rules.Check(jobWithoutLocation).Single().Detail);
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
        var rules = new CanonicalRules(site.Table(), site.UrlOf);

        Assert.Empty(Issues(rules, sameTwice));
        Assert.Equal([IssueCatalog.CANONICAL_MULTIPLE], Issues(rules, conflicting));
        Assert.Equal([IssueCatalog.CANONICAL_RELATIVE], Issues(rules, relative));
        Assert.Equal([IssueCatalog.CANONICAL_INVALID], Issues(rules, invalid));
        Assert.Equal([IssueCatalog.CANONICAL_MISSING], Issues(rules, none));
        Assert.Empty(Issues(rules, noneButNoindex));
    }

    [Fact]
    public void NoindexWithACanonicalElsewhereIsAConflict()
    {
        var site = new RuleHarness();
        var other = $"<link rel=\"canonical\" href=\"{site.Site}/other\">";

        var conflict = site.Page($"{site.Site}/a", RuleHarness.Html(other + "<meta name=\"robots\" content=\"noindex\">"), IndexabilityReason.Noindex);
        var fromHeaders = site.Page($"{site.Site}/b", RuleHarness.Html(), IndexabilityReason.Noindex,
            [new ResponseHeader("x-robots-tag", "noindex"), new ResponseHeader("link", $"<{site.Site}/other>; rel=\"canonical\"")]);
        var selfCanonical = site.Page($"{site.Site}/c", RuleHarness.Html($"<link rel=\"canonical\" href=\"{site.Site}/c\"><meta name=\"robots\" content=\"noindex\">"),
            IndexabilityReason.Noindex);
        var canonicalOnly = site.Page($"{site.Site}/d", RuleHarness.Html(other), IndexabilityReason.Canonicalised);
        var rules = new CanonicalRules(site.Table(), site.UrlOf);

        Assert.Equal(new IssueHit(IssueCatalog.NOINDEX_WITH_CANONICAL, conflict.UrlId, null, site.Id($"{site.Site}/other")), rules.Check(conflict).Single());
        Assert.Equal([IssueCatalog.NOINDEX_WITH_CANONICAL], Issues(rules, fromHeaders));
        Assert.Empty(Issues(rules, selfCanonical));
        Assert.Equal([IssueCatalog.CANONICALISED_PAGE], Issues(rules, canonicalOnly));
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

    [Fact]
    public void HeadLinksAndBotRefusalsAreNotLinkProblems()
    {
        var site = new RuleHarness();
        site.Fetched("https://shop.example/dp/123", FetchOutcome.ServerError, 503);
        site.Fetched($"{site.Site}/amp/page", FetchOutcome.Success, 200, isHtml: true);
        var page = site.Page($"{site.Site}/page", RuleHarness.Html($"<link rel=\"amphtml\" href=\"{site.Site}/amp/page\">",
            "<a href=\"https://shop.example/dp/123\">Buy on the shop</a>"));

        Assert.Empty(Issues(new LinkRules(site.Table()), page));
    }

    [Fact]
    public void StructuredDataSkipsTruncatedBlocksAndReferencedRatings()
    {
        var site = new RuleHarness();
        var truncated = site.Page($"{site.Site}/a", RuleHarness.Html(
            $"<script type=\"application/ld+json\">{{\"@type\":\"ItemList\",\"x\":\"{new string('x', PageWalker.MAX_JSON_LD_CHARS)}\"}}</script>"));
        var graph = site.Page($"{site.Site}/b", RuleHarness.Html("""
            <script type="application/ld+json">{"@context":"https://schema.org","@graph":[
              {"@type":"Product","@id":"#product","name":"Widget","aggregateRating":{"@id":"#rating"}},
              {"@type":"AggregateRating","@id":"#rating","ratingValue":4.5,"reviewCount":12}]}</script>
            """));
        var rules = new StructuredDataRules();

        Assert.Empty(Issues(rules, truncated));
        Assert.Empty(Issues(rules, graph));
    }

    [Fact]
    public void StructuredDataWithUnreadableStringsCountsThemAsPresent()
    {
        var site = new RuleHarness();
        var rules = new StructuredDataRules();
        string Script(string json) => $"<script type=\"application/ld+json\">{json}</script>";

        // JSON.stringify of text cut mid-emoji leaves half a surrogate pair, which System.Text.Json can't read as a string.
        var name = site.Page($"{site.Site}/a", RuleHarness.Html(Script("""{"@type":"Product","name":"\uDC00x","offers":{"price":"1"}}""")));
        var nameOnly = site.Page($"{site.Site}/b", RuleHarness.Html(Script("""{"@type":"Product","name":"\uD83D"}""")));
        var type = site.Page($"{site.Site}/c", RuleHarness.Html(Script("""{"@type":["Product\uDC00","Recipe"],"name":"x"}""")));
        var id = site.Page($"{site.Site}/d", RuleHarness.Html(Script(
            """{"@graph":[{"@type":"AggregateRating","@id":"\uDC00","ratingValue":4,"reviewCount":2},{"@type":"Product","name":"x","aggregateRating":{"@id":"\uDC00"}}]}""")));

        Assert.Empty(Issues(rules, name));
        Assert.Equal("Product: needs offers or review or aggregateRating", rules.Check(nameOnly).Single().Detail);
        Assert.Equal("Recipe: needs image", rules.Check(type).Single().Detail);
        Assert.Equal("AggregateRating: needs itemReviewed", rules.Check(id).Single().Detail);

        // A property name can't be read past the same way, so the block is reported as invalid instead of failing the page.
        foreach (var json in new[] { """{"@type":"Product","\uDC00":"1","name":"x"}""", """{"@type":"Product","name":"x","offers":{"\uD83D":"1"}}""" })
        {
            var badName = site.Page($"{site.Site}/{Guid.NewGuid():N}", RuleHarness.Html(Script(json)));
            Assert.Equal([IssueCatalog.STRUCTURED_DATA_INVALID], rules.Check(badName).Select(hit => hit.Issue));

            // Like any syntax fault, on a noindex page too, which the rich-result checks don't look at.
            var noindex = site.Page($"{site.Site}/{Guid.NewGuid():N}", RuleHarness.Html(Script(json)), IndexabilityReason.Noindex);
            Assert.Equal([IssueCatalog.STRUCTURED_DATA_INVALID], rules.Check(noindex).Select(hit => hit.Issue));
        }
    }

    [Fact]
    public void DeeplyNestedJsonLdIsReportedNotOverflowed()
    {
        var site = new RuleHarness();
        var nested = string.Concat(Enumerable.Repeat("""{"a":""", 10_000)) + "1" + new string('}', 10_000);
        var page = site.Page($"{site.Site}/", RuleHarness.Html($"<script type=\"application/ld+json\">{nested}</script>"));

        Assert.Equal([IssueCatalog.STRUCTURED_DATA_INVALID], Issues(new StructuredDataRules(), page));
    }

    [Fact]
    public void InvalidPunycodeHostsAreMeasuredAsWritten()
    {
        // DNS accepts any xn-- label, but IDNA rejects this one, so it can't be shown in Unicode.
        var site = new RuleHarness("https://xn--55555577.example");
        var page = site.Page($"{site.Site}/{new string('a', 120)}", RuleHarness.Html());

        var hit = new UrlRules(SETTINGS).Check(page).Single();

        Assert.Equal(IssueCatalog.URL_TOO_LONG, hit.Issue);
        Assert.Equal($"{RuleText.Characters($"{site.Site}/{new string('a', 120)}".Length)}, over {SETTINGS.MaxUrlLength}", hit.Detail);
    }

    [Fact]
    public void UrlLengthIsMeasuredAsPeopleReadIt()
    {
        var site = new RuleHarness("https://пример.рф");
        var cyrillic = site.Page($"{site.Site}/{string.Concat(Enumerable.Repeat("каталог/", 10))}", RuleHarness.Html());

        // 80 readable characters of path, but 480 once percent-encoded.
        Assert.True(cyrillic.Url.Length > 400);
        Assert.Empty(Issues(new UrlRules(SETTINGS), cyrillic));
    }

    private static List<IssueDefinition> Issues(IPageRule rule, AnalyzedPage page) => rule.Check(page).Select(hit => hit.Issue).ToList();

    private static string Words(int count) => string.Join(' ', Enumerable.Range(0, count).Select(index => $"word{index % 50}"));
}
