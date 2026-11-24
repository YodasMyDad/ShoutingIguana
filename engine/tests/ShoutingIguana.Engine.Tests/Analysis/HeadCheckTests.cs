using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>
/// The new checks of step 3b's third batch (docs/v2-new-checks.md, "The head and served files"): tags the head should
/// hold, the document's structure, viewport, encoding and language, a separate mobile site, and the files pages load.
/// </summary>
public sealed class HeadCheckTests
{
    private const string TOKEN = "ShoutingIguana";

    private static List<IssueHit> Html(string head = "", string body = "", string lang = " lang=\"en\"", IReadOnlyList<ResponseHeader>? headers = null,
        string? byteOrderMark = null, string? document = null)
    {
        var site = new RuleHarness();
        return [.. new HtmlRules().Check(site.Page($"{site.Site}/", document ?? RuleHarness.Html(head, body, lang), headers: headers, byteOrderMark: byteOrderMark))];
    }

    private static string? Detail(IssueDefinition check, List<IssueHit> hits) => hits.SingleOrDefault(hit => hit.Issue == check).Detail;

    [Fact]
    public void HeadTagsAStrayElementPushedIntoTheBodyAreFlaggedWithWhatEndedTheHead()
    {
        var document = "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">\n<div id=\"promo\"></div>\n<title>A page</title>" +
                       "<meta name=\"description\" content=\"About it\"><meta property=\"og:title\" content=\"A page\"><meta property=\"og:image\" content=\"/a.jpg\">" +
                       "<link rel=\"canonical\" href=\"/\"><base href=\"/\"><script src=\"/a.js\"></script></head><body><p>Text</p></body></html>";

        var hits = Html(document: document);

        Assert.Equal("title, meta description and 2 Open Graph tags are in the body: <div id=\"promo\"> on line 2 ended the head early",
            Detail(IssueCatalog.HEAD_TAGS_IN_BODY, hits));
        Assert.Equal("<div id=\"promo\"> ended the head early", IssueFaults.HeadEndCause(Detail(IssueCatalog.HEAD_TAGS_IN_BODY, hits)!));
        Assert.Null(Detail(IssueCatalog.HTML_STRUCTURE_ERRORS, hits));

        // Written in the body on purpose, as a theme might.
        var written = Html(body: "<meta name=\"robots\" content=\"noindex\">");
        Assert.Equal("meta robots is in the body, after <body> on line 1", Detail(IssueCatalog.HEAD_TAGS_IN_BODY, written));
        Assert.Equal("After <body>", IssueFaults.HeadEndCause(Detail(IssueCatalog.HEAD_TAGS_IN_BODY, written)!));

        // A canonical, hreflang, base, script or style in the body is no head tag of this check's.
        Assert.Null(Detail(IssueCatalog.HEAD_TAGS_IN_BODY, Html(body: "<link rel=\"canonical\" href=\"/\"><link rel=\"alternate\" hreflang=\"fr\" href=\"/fr\"><base href=\"/\"><style>p{}</style>")));
        Assert.Null(Detail(IssueCatalog.HEAD_TAGS_IN_BODY, Html("<title>Fine</title><meta name=\"description\" content=\"Fine\">")));
    }

    [Fact]
    public void ASecondHeadOrBodyOrAnHtmlTagAfterContentIsAStructureError()
    {
        Assert.Equal("2 <head> tags (the second on line 1); 2 <body> tags (the second on line 1)", Detail(IssueCatalog.HTML_STRUCTURE_ERRORS,
            Html(document: "<!DOCTYPE html><html lang=\"en\"><head><title>A</title></head><head></head><body><body class=\"second\"><p>Text</p></body></html>")));
        Assert.Equal("<html> after content (on line 2); <head> after content (on line 2)", Detail(IssueCatalog.HTML_STRUCTURE_ERRORS,
            Html(document: "Warning: something failed\n<!DOCTYPE html><html lang=\"en\"><head><title>A</title></head><body><p>Text</p></body></html>")));

        // Missing head and body tags are allowed, and a body the parser made up for a stray element in the head is the page's one body.
        Assert.Null(Detail(IssueCatalog.HTML_STRUCTURE_ERRORS, Html(document: "<!DOCTYPE html><html lang=\"en\"><title>A</title><p>Text</p></html>")));
        Assert.Null(Detail(IssueCatalog.HTML_STRUCTURE_ERRORS,
            Html(document: "<!DOCTYPE html><html lang=\"en\"><head><img src=\"/t.gif\" alt=\"\"><title>A</title></head><body><p>Text</p></body></html>")));
    }

    /// <summary>
    /// The real-site audit's case: the detail named the fault but not where. Each says the line of the tag at fault, which
    /// differs from page to page, so the group is the fault without it; an earlier engine's detail had no lines.
    /// </summary>
    [Fact]
    public void AStructureErrorSaysOnWhichLine()
    {
        var detail = Detail(IssueCatalog.HTML_STRUCTURE_ERRORS, Html(document:
            "<!DOCTYPE html>\n<html lang=\"en\">\n<head><title>A</title></head>\n<head>\n<meta name=\"x\" content=\"y\">\n</head>\n<body>\n<p>Text</p>\n" +
            "<body class=\"second\">\n<p>More</p>\n<body class=\"third\">\n</body></html>"));
        Assert.Equal("2 <head> tags (the second on line 4); 3 <body> tags (the second on line 9)", detail);
        Assert.Equal("2 <head> tags; 3 <body> tags", IssueCatalog.HTML_STRUCTURE_ERRORS.FaultOf!(detail!));
        Assert.Equal("<html> after content; <head> after content", IssueCatalog.HTML_STRUCTURE_ERRORS.FaultOf!("<html> after content (on line 2); <head> after content (on line 2)"));
        Assert.Equal("2 <head> tags; 2 <body> tags", IssueCatalog.HTML_STRUCTURE_ERRORS.FaultOf!("2 <head> tags; 2 <body> tags"));
        Assert.Equal(18, IssueCatalog.HTML_STRUCTURE_ERRORS.FaultsSince);

        // Content before the page's own head: its <head> tag is the first, after content; a second is the page's second.
        Assert.Equal("2 <head> tags (the second on line 3)", Detail(IssueCatalog.HTML_STRUCTURE_ERRORS, Html(document:
            "<p>Warning</p>\n<head><title>A</title></head>\n<head></head><body><p>Text</p></body>")));
    }

    [Theory]
    [InlineData("width=device-width, initial-scale=1", null)]
    [InlineData("width=device-width; initial-scale=1; maximum-scale=5", null)]
    [InlineData("width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no", "user-scalable=no; maximum-scale=1")]
    [InlineData("width=device-width, user-scalable=0", "user-scalable=0")]
    [InlineData("minimum-scale=1", "no width or initial-scale")]
    public void AViewportThatStopsZoomOrSetsNoWidthIsFlagged(string content, string? detail) =>
        Assert.Equal(detail, Detail(IssueCatalog.VIEWPORT_INVALID, Html($"<meta name=\"viewport\" content=\"{content}\">")));

    [Fact]
    public void TwoViewportsThatDifferAreFlaggedAndNoneIsTheMissingChecks()
    {
        Assert.Equal("2 viewport tags that differ", Detail(IssueCatalog.VIEWPORT_INVALID,
            Html("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><meta name=\"viewport\" content=\"width=1024\">")));
        Assert.Null(Detail(IssueCatalog.VIEWPORT_INVALID, Html()));
    }

    [Fact]
    public void AnEncodingDeclaredWronglyIsFlaggedWithWhatsWrong()
    {
        static IReadOnlyList<ResponseHeader> Header(string value) => [new ResponseHeader("content-type", value)];
        string? Charset(string head, string? header = null, string? byteOrderMark = null) =>
            Detail(IssueCatalog.CHARSET_INVALID, Html(head.Length == 0 ? string.Empty : head, headers: header is null ? null : Header(header), byteOrderMark: byteOrderMark,
                document: $"<!DOCTYPE html><html lang=\"en\"><head>{head}<title>A</title></head><body><p>Text</p></body></html>"));

        Assert.Equal("The header says iso-8859-1; the page says utf-8", Charset("<meta charset=\"utf-8\">", "text/html; charset=iso-8859-1"));
        Assert.Equal("Declares windows-1252, not UTF-8", Charset("<meta charset=\"windows-1252\">"));
        Assert.Equal("Declares utf-16le, not UTF-8", Charset(string.Empty, "text/html", "utf-16le"));
        Assert.Equal("2 declarations: utf-8, iso-8859-1", Charset("<meta charset=\"utf-8\"><meta http-equiv=\"content-type\" content=\"text/html; charset=iso-8859-1\">"));
        Assert.Null(Charset("<meta charset=\"UTF8\">", "text/html; charset=utf-8"));
        Assert.Null(Charset(string.Empty));

        // A meta tag past the first 1024 bytes declares nothing. With the header declaring the encoding, the late one changes
        // nothing a browser does (the real-site audit found it on every page of two of three sites, injected by the
        // platform), so it isn't flagged; with nothing else the encoding is missing, which is another check's.
        var late = $"<style>{new string('x', 1_100)}</style><meta charset=\"utf-8\">";
        Assert.Null(Charset(late, "text/html; charset=utf-8"));
        Assert.Null(Charset($"<style>{new string('x', 1_100)}</style><meta charset=\"iso-8859-1\">", "text/html; charset=utf-8"));
        Assert.Null(Charset(late));
    }

    [Theory]
    [InlineData("en", true)]
    [InlineData("en-GB", true)]
    [InlineData("zh-Hant-TW", true)]
    [InlineData("es-419", true)]
    [InlineData("sr-Latn-RS", true)]
    [InlineData("haw", true)]
    [InlineData("de-CH-1996", true)]
    [InlineData("en-US-x-twain", true)]
    [InlineData("zh-cmn-Hans", true)]
    [InlineData("zh-yue-HK", true)]
    [InlineData("iw", true)]
    [InlineData("en-abc-def-ghi-jkl", false)]
    [InlineData("english", false)]
    [InlineData("en_GB", false)]
    [InlineData("uk-en", false)]
    [InlineData("en-", false)]
    [InlineData("xx", false)]
    public void ALangMustBeAWellFormedLanguageTag(string lang, bool valid)
    {
        Assert.Equal(valid, HtmlRules.IsLanguageTag(lang));
        Assert.Equal(valid ? null : lang, Detail(IssueCatalog.LANG_INVALID, Html(lang: $" lang=\"{lang}\"")));
    }

    [Fact]
    public void ASeparateMobileVersionIsFlagged()
    {
        Assert.Equal("Mobile version: https://m.example.com/a", Detail(IssueCatalog.MOBILE_ALTERNATE,
            Html("<link rel=\"alternate\" media=\"only screen and (max-width: 640px)\" href=\"https://m.example.com/a\">")));
        Assert.Null(Detail(IssueCatalog.MOBILE_ALTERNATE, Html("<link rel=\"alternate\" type=\"application/rss+xml\" href=\"/feed\">")));
        Assert.Null(Detail(IssueCatalog.MOBILE_ALTERNATE, Html("<link rel=\"alternate\" hreflang=\"fr\" media=\"print\" href=\"/fr\">")));
    }

    [Fact]
    public void AFileServedAsAnotherTypeThanItsUsedAsIsFlaggedOnceForEachUse()
    {
        var site = new RuleHarness();
        var html = site.Fetched($"{site.Site}/app.js", FetchOutcome.Success, 200, contentType: "text/html");
        var plain = site.Fetched($"{site.Site}/style.css", FetchOutcome.Success, 200, contentType: "text/plain");
        var none = site.Fetched($"{site.Site}/pic", FetchOutcome.Success, 200, contentType: null);
        site.Fetched($"{site.Site}/fine.js", FetchOutcome.Success, 200, contentType: "text/javascript");
        site.Fetched($"{site.Site}/fine.png", FetchOutcome.Success, 200, contentType: "image/png");
        site.Fetched($"{site.Site}/gone.js", FetchOutcome.ClientError, 404, contentType: "text/html");
        AnalyzedPage[] pages =
        [
            site.Page($"{site.Site}/a", RuleHarness.Html("<script src=\"/app.js\"></script><link rel=\"stylesheet\" href=\"/style.css\"><script src=\"/fine.js\"></script>",
                "<img src=\"/pic\" alt=\"A\"><img src=\"/app.js\" alt=\"B\"><img src=\"/fine.png\" alt=\"C\"><script src=\"/gone.js\"></script>")),
            site.Page($"{site.Site}/b", RuleHarness.Html("<script src=\"/app.js\"></script>"))
        ];
        Dictionary<int, string> types = new() { [html] = "text/html", [plain] = "text/plain" };
        var rule = new ResourceRules(site.Table(), id => types.GetValueOrDefault(id));
        Array.ForEach(pages, rule.Observe);

        Assert.Equal(
        [
            new IssueHit(IssueCatalog.CONTENT_TYPE_MISMATCH, html, "Used as a script and an image, sent as text/html"),
            new IssueHit(IssueCatalog.CONTENT_TYPE_MISMATCH, plain, "Used as a stylesheet, sent as text/plain"),
            new IssueHit(IssueCatalog.CONTENT_TYPE_MISMATCH, none, "Used as an image, sent with no Content-Type")
        ], rule.Finish().Where(hit => hit.Issue == IssueCatalog.CONTENT_TYPE_MISMATCH));
    }

    [Fact]
    public void AScriptOrStylesheetPastTwoMegabytesIsFlagged()
    {
        var site = new RuleHarness();
        var big = site.Fetched($"{site.Site}/big.js", FetchOutcome.Success, 200, contentType: "text/javascript", size: 2_400_000);
        site.Fetched($"{site.Site}/fine.css", FetchOutcome.Success, 200, contentType: "text/css", size: 2_097_152);
        site.Fetched($"{site.Site}/big.jpg", FetchOutcome.Success, 200, contentType: "image/jpeg", size: 5_000_000);
        var page = site.Page($"{site.Site}/", RuleHarness.Html("<script src=\"/big.js\"></script><link rel=\"stylesheet\" href=\"/fine.css\">",
            "<img src=\"/big.jpg\" alt=\"Big\">"));
        var rule = new ResourceRules(site.Table(), _ => null);
        rule.Observe(page);

        Assert.Equal([new IssueHit(IssueCatalog.RESOURCE_PAST_CRAWL_LIMIT, big, "2.3 MB, past the 2 MB Google reads")],
            rule.Finish().Where(hit => hit.Issue == IssueCatalog.RESOURCE_PAST_CRAWL_LIMIT));
    }

    /// <summary>
    /// Google's 2 MB is of the file once decompressed. A compressed file is judged by that size where the crawl counted
    /// it, and the detail gives both, because the Size column beside it is the bytes sent.
    /// </summary>
    [Fact]
    public void ACompressedScriptIsJudgedByItsSizeOnceDecompressed()
    {
        var site = new RuleHarness();
        var packed = site.Fetched($"{site.Site}/packed.js", FetchOutcome.Success, 200, contentType: "text/javascript", size: 600_000, bodyBytes: 2_400_000);
        site.Fetched($"{site.Site}/small.css", FetchOutcome.Success, 200, contentType: "text/css", size: 300_000, bodyBytes: 1_900_000);
        var plain = site.Fetched($"{site.Site}/plain.js", FetchOutcome.Success, 200, contentType: "text/javascript", size: 2_400_000, bodyBytes: 2_400_000);
        var close = site.Fetched($"{site.Site}/close.js", FetchOutcome.Success, 200, contentType: "text/javascript", size: 700_000, bodyBytes: 2_097_400);
        var cut = site.Fetched($"{site.Site}/cut.js", FetchOutcome.Success, 200, contentType: "text/javascript", bodyBytes: 16_777_216);
        var page = site.Page($"{site.Site}/", RuleHarness.Html(
            "<script src=\"/packed.js\"></script><link rel=\"stylesheet\" href=\"/small.css\"><script src=\"/plain.js\"></script>" +
            "<script src=\"/close.js\"></script><script src=\"/cut.js\"></script>"));
        var rule = new ResourceRules(site.Table(), _ => null);
        rule.Observe(page);

        Assert.Equal(
            [
                new IssueHit(IssueCatalog.RESOURCE_PAST_CRAWL_LIMIT, packed, "2.3 MB once decompressed (586 KB as sent), past the 2 MB Google reads"),
                new IssueHit(IssueCatalog.RESOURCE_PAST_CRAWL_LIMIT, plain, "2.3 MB, past the 2 MB Google reads"),
                new IssueHit(IssueCatalog.RESOURCE_PAST_CRAWL_LIMIT, close, "2,097,400 bytes once decompressed (684 KB as sent), past the 2 MB Google reads"),
                new IssueHit(IssueCatalog.RESOURCE_PAST_CRAWL_LIMIT, cut, "16.0 MB once decompressed, past the 2 MB Google reads")
            ],
            rule.Finish().Where(hit => hit.Issue == IssueCatalog.RESOURCE_PAST_CRAWL_LIMIT));
    }

    [Theory]
    [InlineData("max-age=2592000", "302 Found, cached for 30 days")]
    [InlineData("public, s-maxage=172800, max-age=60", "302 Found, cached for 2 days")]
    [InlineData("max-age=129600", "302 Found, cached for 36 hours")]
    [InlineData("max-age=86400", null)]
    [InlineData("max-age=2592000, no-store", null)]
    [InlineData("no-cache, max-age=2592000", null)]
    public void ATemporaryRedirectCachedForMoreThanADayIsFlagged(string cacheControl, string? detail)
    {
        var site = new RuleHarness();
        site.Ok($"{site.Site}/new");
        var temporary = site.Fetched($"{site.Site}/sale", FetchOutcome.Redirect, 302, redirectTo: $"{site.Site}/new");
        (int, IReadOnlyList<ResponseHeader>)[] redirects = [(temporary, [new ResponseHeader("cache-control", cacheControl)])];

        var hit = new RedirectHeaderRules(site.Table(), () => redirects, site.UrlOf).Finish().SingleOrDefault(hit => hit.Issue == IssueCatalog.REDIRECT_TEMPORARY_CACHED);

        Assert.Equal(detail, hit.Detail);
    }

    [Fact]
    public void AnExpiresAfterItsDateIsALifetimeAndOnlyTemporaryRedirectsWithinTheSiteAreJudged()
    {
        var site = new RuleHarness();
        site.Ok($"{site.Site}/new");
        var expiring = site.Fetched($"{site.Site}/expiring", FetchOutcome.Redirect, 307, redirectTo: $"{site.Site}/new");
        var permanent = site.Fetched($"{site.Site}/moved", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/new");
        var away = site.Fetched($"{site.Site}/login", FetchOutcome.Redirect, 302, redirectTo: "https://accounts.example.org/");
        IReadOnlyList<ResponseHeader> longLived = [new("cache-control", "max-age=2592000")];
        (int, IReadOnlyList<ResponseHeader>)[] redirects =
        [
            (expiring, [new("date", "Tue, 17 Nov 2026 12:00:00 GMT"), new("expires", "Fri, 20 Nov 2026 12:00:00 GMT")]), (permanent, longLived), (away, longLived)
        ];

        var hits = new RedirectHeaderRules(site.Table(), () => redirects, site.UrlOf).Finish().ToList();

        Assert.Equal([new IssueHit(IssueCatalog.REDIRECT_TEMPORARY_CACHED, expiring, "307 Temporary Redirect, cached for 3 days")], hits);
    }

    [Fact]
    public void TheParserNotesHeadTagsInTheBodyEveryCharsetAndAMobileVersion()
    {
        var parsed = HtmlPageParser.Parse("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><img src=\"/t.gif\"><meta charset=\"latin1\">" +
            "<link rel=\"alternate\" media=\"handheld\" href=\"https://m.example.com/\"><link rel=\"amphtml\" href=\"/amp\"></head><body></body></html>",
            new Uri("https://example.com/"), [], TOKEN);

        Assert.Equal(["link alternate", "link amphtml"], parsed.Facts.HeadTagsInBody);
        Assert.Equal(["utf-8", "latin1"], parsed.Facts.CharsetDeclarations);
        Assert.Equal("https://m.example.com/", parsed.Facts.MobileAlternate);

        // An alternate stylesheet, a feed or a print version with a media query is no mobile version of the page.
        foreach (var link in (string[])
                 [
                     "<link rel=\"alternate stylesheet\" media=\"screen\" title=\"High contrast\" href=\"/contrast.css\">",
                     "<link rel=\"alternate\" type=\"application/rss+xml\" media=\"all\" href=\"/feed.xml\">",
                     "<link rel=\"alternate\" media=\"print\" href=\"/print/page\">"
                 ])
        {
            Assert.Null(HtmlPageParser.Parse(RuleHarness.Html(link), new Uri("https://example.com/"), [], TOKEN).Facts.MobileAlternate);
        }

        // A charset in an http-equiv ends at a semicolon or a space, and may have spaces round its equals sign (the HTML
        // standard's algorithm for extracting a character encoding from a meta element).
        string? Declared(string content) =>
            HtmlPageParser.Parse(RuleHarness.Html($"<meta http-equiv=\"Content-Type\" content=\"{content}\">"), new Uri("https://example.com/"), [], TOKEN).Facts.CharsetDeclarations[^1];
        Assert.Equal("utf-8", Declared("text/html; charset=utf-8;"));
        Assert.Equal("utf-8", Declared("text/html; charset = utf-8"));
        Assert.Equal("iso-8859-1", Declared("text/html; charset='iso-8859-1' ; x=y"));
        Assert.Equal("<img src=\"/t.gif\">", parsed.Facts.HeadEnd!.Element);
        Assert.Empty(parsed.Facts.StructureErrors);
    }
}
