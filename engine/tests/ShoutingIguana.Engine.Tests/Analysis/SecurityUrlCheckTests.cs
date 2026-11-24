using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>
/// Step 3b's security and URL checks (batch 7 of docs/v2-new-checks.md): security headers set too weakly, cookies without
/// protection, forms sending over HTTP, // URLs, and URLs with tracking parameters, double slashes, spaces, parameters,
/// non-ASCII characters, repeating paths, or that are the site's own search.
/// </summary>
public sealed class SecurityUrlCheckTests
{
    private static readonly AnalysisSettings SETTINGS = new();

    [Theory]
    [InlineData("max-age=86400; includeSubDomains", "nosniff", "HSTS max-age 86400 (1 day), under a year")]
    [InlineData("max-age=0", "none", "HSTS max-age 0 (0 seconds), under a year; X-Content-Type-Options \"none\"")]
    [InlineData("includeSubDomains", "nosniff", "HSTS without max-age")]
    [InlineData("max-age=abc", "nosniff", "HSTS max-age \"abc\" unreadable")]
    [InlineData("max-age=\"31536000\"; preload", "NoSniff", null)]
    [InlineData(null, null, null)]
    public void SecurityHeadersThereButTooWeakAreReportedOnTheHomePage(string? hsts, string? options, string? detail)
    {
        var site = new RuleHarness();
        List<ResponseHeader> headers = [];
        if (hsts is not null)
        {
            headers.Add(new ResponseHeader("strict-transport-security", hsts));
        }

        if (options is not null)
        {
            headers.Add(new ResponseHeader("x-content-type-options", options));
        }

        var home = site.Page($"{site.Site}/", RuleHarness.Html(), headers: headers);
        Assert.Equal(detail, Detail(new SecurityRules(site.Table()).Check(home), IssueCatalog.SECURITY_HEADERS_WEAK));
    }

    /// <summary>
    /// A security header sent twice, or with its values joined by commas (as a proxy or CDN that combines repeats sends
    /// it), is judged as browsers read it. X-Content-Type-Options: its values, every line's split at commas, and the first
    /// says whether it's nosniff (the Fetch standard's "determine nosniff"). Strict-Transport-Security: only the first
    /// line counts (RFC 6797, 8.1), and a line holding two policies joined by a comma is no policy browsers can read, so
    /// stays flagged. Content-Security-Policy: every line is enforced, so frame-ancestors in any of them stands in for
    /// X-Frame-Options. Referrer-Policy and X-Frame-Options are only judged missing or not.
    /// </summary>
    [Theory]
    [InlineData("x-content-type-options", "nosniff, nosniff", null, null)]
    [InlineData("x-content-type-options", "NOSNIFF,nosniff", null, null)]
    [InlineData("x-content-type-options", "nosniff", "nosniff", null)]
    [InlineData("x-content-type-options", "sniff, nosniff", null, "X-Content-Type-Options \"sniff, nosniff\"")]
    [InlineData("x-content-type-options", "sniff", "nosniff", "X-Content-Type-Options \"sniff, nosniff\"")]
    [InlineData("strict-transport-security", "max-age=31536000", "max-age=31536000", null)]
    [InlineData("strict-transport-security", "max-age=31536000; preload", "max-age=60", null)]
    [InlineData("strict-transport-security", "max-age=31536000, max-age=31536000", null, "HSTS max-age \"31536000, max-age=3…\" unreadable")]
    public void ASecurityHeaderSentTwiceOrJoinedByCommasIsReadAsBrowsersReadIt(string name, string first, string? second, string? weak)
    {
        var site = new RuleHarness();
        List<ResponseHeader> headers =
        [
            new("strict-transport-security", "max-age=31536000"), new("x-content-type-options", "nosniff"),
            new("content-security-policy", "default-src 'self'"), new("x-frame-options", "DENY"), new("referrer-policy", "no-referrer")
        ];
        headers.RemoveAll(header => header.Name == name);
        headers.Add(new ResponseHeader(name, first));
        if (second is not null)
        {
            headers.Add(new ResponseHeader(name, second));
        }

        var hits = new SecurityRules(site.Table()).Check(site.Page($"{site.Site}/", RuleHarness.Html(), headers: headers)).ToList();

        Assert.Equal(weak, Detail(hits, IssueCatalog.SECURITY_HEADERS_WEAK));
        Assert.Null(Detail(hits, IssueCatalog.SECURITY_HEADERS_MISSING));
    }

    [Theory]
    [InlineData("default-src 'self'", "frame-ancestors 'none'", null)]
    [InlineData("frame-ancestors 'none', default-src 'self'", null, null)]
    [InlineData("default-src 'self'", "default-src 'self'", "Missing X-Frame-Options")]
    public void FrameAncestorsInAnyContentSecurityPolicyLineStandsInForXFrameOptions(string first, string? second, string? missing)
    {
        var site = new RuleHarness();
        List<ResponseHeader> headers =
        [
            new("strict-transport-security", "max-age=31536000"), new("x-content-type-options", "nosniff"),
            new("referrer-policy", "no-referrer, strict-origin-when-cross-origin"), new("content-security-policy", first)
        ];
        if (second is not null)
        {
            headers.Add(new ResponseHeader("content-security-policy", second));
        }

        var hits = new SecurityRules(site.Table()).Check(site.Page($"{site.Site}/", RuleHarness.Html(), headers: headers)).ToList();

        Assert.Equal(missing, Detail(hits, IssueCatalog.SECURITY_HEADERS_MISSING));
        Assert.Null(Detail(hits, IssueCatalog.SECURITY_HEADERS_WEAK));
    }

    [Fact]
    public void CookiesWithoutProtectionAreNamedWithWhatTheyLack()
    {
        string? Cookies(string site, params string[] cookies)
        {
            var harness = new RuleHarness(site);
            harness.Ok($"{harness.Site}/");
            var page = harness.Page($"{harness.Site}/a", RuleHarness.Html(), headers: [.. cookies.Select(cookie => new ResponseHeader("set-cookie", cookie))]);
            return Detail(new SecurityRules(harness.Table()).Check(page), IssueCatalog.COOKIES_INSECURE);
        }

        Assert.Equal("sessionid: no Secure, no HttpOnly; theme: no Secure",
            Cookies("https://example.com", "sessionid=x; Path=/", "theme=dark; Path=/", "auth_token=y; Secure; HttpOnly"));
        Assert.Equal("PHPSESSID: no HttpOnly; pref: SameSite=None without Secure",
            Cookies("http://example.com", "PHPSESSID=x; path=/", "pref=1; SameSite = None", "theme=dark"));

        // A CSRF token cookie is read by the page's own script, which sends it back in a header: HttpOnly would break it.
        Assert.Null(Cookies("https://example.com", "XSRF-TOKEN=x; Path=/; Secure; SameSite=Lax", "csrftoken=y; Secure", "_csrf_token=z; Secure"));
        Assert.Null(Cookies("https://example.com", "sid=1; Secure; HttpOnly; SameSite=Lax"));
    }

    [Fact]
    public void AFormSendingOverHttpIsFlagged()
    {
        string? Form(string site, string body)
        {
            var harness = new RuleHarness(site);
            harness.Ok($"{harness.Site}/");
            var page = harness.Page($"{harness.Site}/a", RuleHarness.Html(body: body));
            return Detail(new SecurityRules(harness.Table()).Check(page), IssueCatalog.FORM_NOT_SECURE);
        }

        Assert.Equal("Sends to http://example.com/subscribe",
            Form("https://example.com", "<form action=\"/search\"></form><form action=\"http://example.com/subscribe\" method=\"POST\"><input type=\"email\"></form>"));
        Assert.Null(Form("https://example.com", "<form action=\"/subscribe\" method=\"post\"><input type=\"password\"></form>"));
        Assert.Equal("A password form on an http:// page", Form("http://example.com", "<form><input type=\"PASSWORD\" name=\"p\"></form>"));
        Assert.Null(Form("http://example.com", "<form action=\"/search\"><input name=\"q\"></form>"));

        var forms = HtmlPageParser.Parse(RuleHarness.Html(body: "<form method=\"Post\"><input type=\"password\"></form><form action=\"javascript:void(0)\"></form>"),
            new Uri("https://example.com/shop/"), [], "ShoutingIguana").Facts.Forms;
        Assert.Equal([new FormFact("https://example.com/shop/", "post", true), new FormFact(null, "get", false)], forms);
    }

    [Fact]
    public void UrlsWrittenWithoutTheirSchemeAreCounted()
    {
        var site = new RuleHarness();
        site.Ok($"{site.Site}/");
        var page = site.Page($"{site.Site}/a", RuleHarness.Html("<script src=\"//cdn.example.com/app.js\"></script><link rel=\"stylesheet\" href=\"//cdn.example.com/a.css\">",
            "<a href=\"//example.com/b\">B</a><a href=\"/c\">C</a><a href=\"//\">Nothing</a>"));

        Assert.Equal("3 URLs, such as //cdn.example.com/app.js", Detail(new SecurityRules(site.Table()).Check(page), IssueCatalog.PROTOCOL_RELATIVE_LINKS));
    }

    /// <summary>
    /// Each URL written without its scheme is named where the page links to it or loads it, so the views and the export
    /// list every one: the issue's targets, in the page's order, and its link is the one written that way, not a link
    /// to the same URL written with its scheme. One in no link or file (a resource hint) is counted and named in the
    /// detail, and puts the page in the group naming no URL too.
    /// </summary>
    [Fact]
    public void UrlsWrittenWithoutTheirSchemeAreEachNamed()
    {
        var site = new RuleHarness("http://example.com");
        site.Ok($"{site.Site}/");
        var page = site.Page($"{site.Site}/a", RuleHarness.Html(
            "<script src=\"//cdn.example.com/app.js\"></script><link rel=\"preconnect\" href=\"//fonts.example.net\"><link rel=\"icon\" href=\"//example.com/icon.png\">",
            "<a href=\"//example.com/b\">B</a><a href=\"http://example.com/b\">B again</a><a href=\"/c\">C</a>" +
            "<img src=\"//cdn.example.com/p.jpg\" alt=\"P\" width=\"1\" height=\"1\">"));

        var hit = Assert.Single(new SecurityRules(site.Table()).Check(page), hit => hit.Issue == IssueCatalog.PROTOCOL_RELATIVE_LINKS);
        Assert.Equal("5 URLs, such as //cdn.example.com/app.js; 1 in a tag that isn't a link or file, such as //fonts.example.net", hit.Detail);
        int[] named = [site.Find("http://cdn.example.com/app.js"), site.Find("http://example.com/icon.png"), site.Find("http://example.com/b"), site.Find("http://cdn.example.com/p.jpg")];
        Assert.Equal(named[0], hit.RelatedUrlId);
        Assert.Equal([.. named, IssueHit.NO_OTHER_URL], hit.Targets);
        Assert.Equal([LinkFlags.ProtocolRelative, LinkFlags.None], page.Links.Where(link => link.ToId == named[2]).Select(link => link.Flags & LinkFlags.ProtocolRelative));
        Assert.Equal((5, 1, "//fonts.example.net"), (page.Facts.ProtocolRelativeUrls!.Count, page.Facts.ProtocolRelativeUrls.Unlinked, page.Facts.ProtocolRelativeUrls.FirstUnlinked));
    }

    /// <summary>
    /// A page parsed before links noted how they were written names none: the issue counts them, as it did, in the group
    /// naming no URL.
    /// </summary>
    [Fact]
    public void APageParsedBeforeLinksNotedTheirSchemeNamesNone()
    {
        var site = new RuleHarness("http://example.com");
        site.Ok($"{site.Site}/");
        var page = site.Page($"{site.Site}/a", RuleHarness.Html("<script src=\"//cdn.example.com/app.js\"></script>"));
        page = page with
        {
            Facts = page.Facts with { CollectedVersion = 5, ProtocolRelativeUrls = new ProtocolRelativeFact(1, "//cdn.example.com/app.js") },
            Links = [.. page.Links.Select(link => link with { Flags = link.Flags & ~LinkFlags.ProtocolRelative })]
        };

        var hit = Assert.Single(new SecurityRules(site.Table()).Check(page), hit => hit.Issue == IssueCatalog.PROTOCOL_RELATIVE_LINKS);
        Assert.Equal(("1 URL, such as //cdn.example.com/app.js", null, null), (hit.Detail, hit.RelatedUrlId, hit.Targets));
    }

    /// <summary>
    /// A site whose home page sends HSTS over HTTPS has no HTTP copy of its pages for a browser that has been there, which
    /// is the check's risk: its HTTPS pages aren't flagged. A max-age of 0 turns HSTS off; it covers the home page's host,
    /// and its subdomains only with includeSubDomains; and a plain-HTTP page is flagged whatever the home page sends.
    /// </summary>
    [Theory]
    [InlineData("max-age=31536000", "https://example.com/a", false)]
    [InlineData("max-age=86400", "https://example.com/a", false)]
    [InlineData("max-age=\"31536000\"; preload", "https://example.com/", false)]
    [InlineData(null, "https://example.com/a", true)]
    [InlineData("max-age=0", "https://example.com/a", true)]
    [InlineData("includeSubDomains", "https://example.com/a", true)]
    [InlineData("max-age=31536000", "https://shop.example.com/a", true)]
    [InlineData("max-age=31536000; includeSubDomains", "https://shop.example.com/a", false)]
    [InlineData("max-age=31536000; includeSubDomains", "https://notexample.com/a", true)]
    [InlineData("max-age=31536000", "http://example.com/a", true)]
    public void AnHttpsPageOnASiteSendingHstsIsntFlagged(string? hsts, string url, bool flagged)
    {
        var site = new RuleHarness();
        var html = RuleHarness.Html("<script src=\"//cdn.example.com/app.js\"></script>");
        var home = site.Page($"{site.Site}/", html, headers: hsts is null ? [] : [new ResponseHeader("strict-transport-security", hsts)]);
        var page = url == $"{site.Site}/" ? home : site.Page(url, html);

        var rules = new SecurityRules(site.Table(), HstsPolicy.Of(home.Uri, home.Headers));
        Assert.Equal(flagged, rules.Check(page).Any(hit => hit.Issue == IssueCatalog.PROTOCOL_RELATIVE_LINKS));
    }

    /// <summary>
    /// On a page Shopify served (a header says so), the security headers and the cookies Shopify sets (its cookie policy's
    /// names) are Shopify's: the store's owner can't change them. A cookie of any other name on the page, or a page no
    /// header says Shopify served, leaves the issue the site's own.
    /// </summary>
    [Fact]
    public void WhatShopifySetsOnAPageItServedIsShopifys()
    {
        PlatformOwner? Owner(IssueDefinition check, bool servedByShopify, params string[] cookies)
        {
            var site = new RuleHarness("https://shop.example");
            List<ResponseHeader> headers = [new("strict-transport-security", "max-age=7889238"), .. cookies.Select(cookie => new ResponseHeader("set-cookie", cookie))];
            if (servedByShopify)
            {
                headers.Add(new ResponseHeader("powered-by", "Shopify"));
            }

            var home = site.Page($"{site.Site}/", RuleHarness.Html(), headers: headers);
            return Assert.Single(new SecurityRules(site.Table()).Check(home), hit => hit.Issue == check).Platform;
        }

        string[] shopifys = ["localization=GB; Path=/", "cart_currency=GBP; Path=/", "_shopify_essential=x; Path=/"];
        Assert.Equal(ShopifySignals.OWNER, Owner(IssueCatalog.SECURITY_HEADERS_MISSING, servedByShopify: true));
        Assert.Equal(ShopifySignals.OWNER, Owner(IssueCatalog.SECURITY_HEADERS_WEAK, servedByShopify: true));
        Assert.Equal(ShopifySignals.OWNER, Owner(IssueCatalog.COOKIES_INSECURE, servedByShopify: true, shopifys));
        Assert.Equal(new PlatformOwner("Shopify", "Set by Shopify. You can't change this on a Shopify store."), ShopifySignals.OWNER);

        // A cookie of the store's own, or of a name Shopify's policy doesn't give, is something its owner can see to.
        Assert.Null(Owner(IssueCatalog.COOKIES_INSECURE, servedByShopify: true, [.. shopifys, "sessionid=x; Path=/"]));
        Assert.Null(Owner(IssueCatalog.COOKIES_INSECURE, servedByShopify: true, "Localization=GB; Path=/"));

        // A page no header says Shopify served is the site's, whatever its cookies are called.
        Assert.Null(Owner(IssueCatalog.SECURITY_HEADERS_MISSING, servedByShopify: false));
        Assert.Null(Owner(IssueCatalog.SECURITY_HEADERS_WEAK, servedByShopify: false));
        Assert.Null(Owner(IssueCatalog.COOKIES_INSECURE, servedByShopify: false, shopifys));
    }

    [Theory]
    [InlineData("/a//b", "url-multiple-slashes", null)]
    [InlineData("/with%20space", "url-space", null)]
    [InlineData("/caf%C3%A9", "url-non-ascii", null)]
    [InlineData("/list?colour=bl%C3%A5", "url-non-ascii", null)]
    [InlineData("/list?sort=price&page=2&sort=name", "url-parameters", "sort, page")]
    [InlineData("/search?q=sofa", "url-internal-search", "q")]
    [InlineData("/shop?s=sofa&page=2", "url-internal-search", "s, page")]
    [InlineData("/search/sofas", "url-internal-search", null)]
    public void AnIndexablePagesUrlIsJudgedByItsText(string path, string check, string? detail)
    {
        var site = new RuleHarness();
        var page = site.Page($"{site.Site}{path}", RuleHarness.Html());
        var hits = new UrlRules(SETTINGS).Check(page).ToList();

        Assert.Equal(detail, Assert.Single(hits, hit => hit.Issue.Id == check).Detail);
        Assert.DoesNotContain(hits, hit => hit.Issue == IssueCatalog.URL_PARAMETERS && check == "url-internal-search");
        Assert.Empty(new UrlRules(SETTINGS).Check(site.Page($"{site.Site}{path}", RuleHarness.Html(), IndexabilityReason.Noindex)));
    }

    [Theory]
    [InlineData("/shop/shop/", true)]
    [InlineData("/a/b/a/b/", true)]
    [InlineData("/x/a/b/c/a/b/c", true)]
    [InlineData("/x/a/x/b/x", true)]
    [InlineData("/Shop/shop", true)]
    [InlineData("/blog/2024/blog-post", false)]
    [InlineData("/2025/11/11/singles-day/", false)]
    [InlineData("/archive/2024/12/12/12-days-of-offers/", false)]
    [InlineData("/a/b/c/d/", false)]
    [InlineData("/", false)]
    public void APathThatRepeatsItselfIsRepetitive(string path, bool repetitive)
    {
        Assert.Equal(repetitive, UrlTextRules.IsRepetitive(path));
    }

    [Fact]
    public void AnInternalLinkTaggedForTrackingIsTheTargetsFault()
    {
        var site = new RuleHarness();
        var home = site.Ok($"{site.Site}/");
        var tagged = site.Ok($"{site.Site}/a?utm_source=news&utm_medium=email&id=4");
        var unlinked = site.Ok($"{site.Site}/b?gclid=1");
        var newer = site.Ok($"{site.Site}/c?utm_source_platform=app&UTM_Creative_Format=banner");
        var urls = site.Table();
        urls.CountInlinks(site.Page($"{site.Site}/", RuleHarness.Html(body:
            "<a href=\"/a?utm_source=news&utm_medium=email&id=4\">A</a><a href=\"/c?utm_source_platform=app&UTM_Creative_Format=banner\">C</a>")));

        var hits = new UrlTextRules(urls,
            [
                (home, $"{site.Site}/"), (tagged, $"{site.Site}/a?utm_source=news&utm_medium=email&id=4"), (unlinked, $"{site.Site}/b?gclid=1"),
                (newer, $"{site.Site}/c?utm_source_platform=app&UTM_Creative_Format=banner")
            ])
            .Finish().Where(hit => hit.Issue == IssueCatalog.URL_TRACKING_PARAMETERS).ToList();

        // Every utm_ parameter is Google Analytics', the newer ones (GA4's) too.
        Assert.Equal([(tagged, "utm_source, utm_medium"), (newer, "utm_source_platform, UTM_Creative_Format")], hits.Select(hit => (hit.UrlId, hit.Detail)));
    }

    private static string? Detail(IEnumerable<IssueHit> hits, IssueDefinition check) =>
        hits.Where(hit => hit.Issue == check).Select(hit => hit.Detail).SingleOrDefault();
}
