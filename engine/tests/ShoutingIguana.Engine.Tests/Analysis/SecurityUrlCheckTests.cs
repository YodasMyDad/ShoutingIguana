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
        var urls = site.Table();
        urls.CountInlinks(site.Page($"{site.Site}/", RuleHarness.Html(body: "<a href=\"/a?utm_source=news&utm_medium=email&id=4\">A</a>")));

        var hits = new UrlTextRules(urls, [(home, $"{site.Site}/"), (tagged, $"{site.Site}/a?utm_source=news&utm_medium=email&id=4"), (unlinked, $"{site.Site}/b?gclid=1")])
            .Finish().ToList();
        var hit = Assert.Single(hits, hit => hit.Issue == IssueCatalog.URL_TRACKING_PARAMETERS);
        Assert.Equal((tagged, "utm_source, utm_medium"), (hit.UrlId, hit.Detail));
    }

    private static string? Detail(IEnumerable<IssueHit> hits, IssueDefinition check) =>
        hits.Where(hit => hit.Issue == check).Select(hit => hit.Detail).SingleOrDefault();
}
