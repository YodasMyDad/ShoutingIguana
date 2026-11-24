using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>
/// What each check's flagged URLs are (<see cref="IssueDefinition.Subject"/>), held against what its rule can flag. A
/// check that says <see cref="IssueSubject.Url"/> is shown flagging something that isn't a page, by its own rule; one that
/// says <see cref="IssueSubject.Page"/> flags nothing else on any crawl of the synthetic site (every end-to-end analysis
/// checks that too, <see cref="IssueSubjectAgreement"/>), and the rules that can reach a URL that isn't a page are all
/// accounted for here.
/// </summary>
[Collection(GridCrawlCollection.NAME)]
public sealed class IssueSubjectTests(GridCrawl crawl)
{
    /// <summary>
    /// The rules that report on URLs by id rather than on the page being audited, with the checks of theirs that test for a
    /// page before they flag one (an indexable page, a page with hreflang): the rest of their checks can flag any URL.
    /// </summary>
    private static readonly IssueDefinition[] PAGES_BY_TEST =
    [
        IssueCatalog.ORPHAN_PAGE, IssueCatalog.NOT_IN_SITEMAP, IssueCatalog.TITLE_DUPLICATE, IssueCatalog.DESCRIPTION_DUPLICATE, IssueCatalog.H1_DUPLICATE,
        IssueCatalog.DUPLICATE_CONTENT, IssueCatalog.NEAR_DUPLICATE_CONTENT, IssueCatalog.BROKEN_FRAGMENT_LINKS, IssueCatalog.HREFLANG_MISSING_RETURN
    ];

    [Fact]
    public void EveryCheckSaysWhatItFlags()
    {
        Assert.All(IssueCatalog.All, check => Assert.True(Enum.IsDefined(check.Subject)));

        // The fingerprint is the checks' ids, categories and severities: wording a count differently expires no analysis.
        Assert.Equal(AnalysisFreshness.ChecksFingerprint,
            AnalysisFreshness.Fingerprint(IssueCatalog.All.Select(check => check with { Subject = check.Subject == IssueSubject.Page ? IssueSubject.Url : IssueSubject.Page })));
    }

    [Fact]
    public void ACheckThatSaysPagesFlagsNothingElse()
    {
        IssueSubjectAgreement.AssertPageChecksFlagOnlyPages(crawl.Path);

        // Not for want of anything flagged: the crawl has files, redirects, errors and refused URLs flagged beside its pages.
        var nonPages = IssueSubjectAgreement.FlaggedNonPages(crawl.Path);
        Assert.Contains(IssueCatalog.INTERNAL_CLIENT_ERROR.Id, nonPages.Keys);
        Assert.Contains(nonPages[IssueCatalog.INTERNAL_CLIENT_ERROR.Id], url => url.EndsWith(".jpg", StringComparison.Ordinal));
        Assert.All(nonPages.Keys, id => Assert.Equal(IssueSubject.Url, IssueCatalog.Find(id)!.Subject));
    }

    /// <summary>
    /// Each check that says it can flag any URL does, by its own rule, on a site built by hand: a missing image, URLs that
    /// fail, are refused or redirect, a sitemap listing what isn't there, another version of the site that answers, and a
    /// start URL that is no page, which the checks reported once for the whole site are reported on.
    /// </summary>
    private static IEnumerable<IssueHit> FilesUsed(UrlTable urls, AnalyzedPage page)
    {
        var rule = new ResourceRules(urls, _ => "text/html");
        rule.Observe(page);
        return rule.Finish();
    }

    [Fact]
    public void ACheckThatSaysAnyUrlCanFlagWhatIsNotAPage()
    {
        var site = new RuleHarness();

        // The start URL refuses the crawler, so the site has no home page to report on.
        var start = site.Fetched($"{site.Site}/", FetchOutcome.ClientError, 403);
        site.Fetched($"{site.Site}/img/missing.jpg", FetchOutcome.ClientError, 404);
        site.Fetched($"{site.Site}/error", FetchOutcome.ServerError, 500);
        site.Fetched($"{site.Site}/slow", FetchOutcome.Timeout, 0);
        var blocked = site.Fetched($"{site.Site}/guarded", FetchOutcome.Blocked, 403);
        site.Fetched($"{site.Site}/private", FetchOutcome.RobotsDisallowed, 0);

        // A chain is reported where it's entered: a sitemap lists this one.
        site.Fetched($"{site.Site}/promo", FetchOutcome.Redirect, 302, redirectTo: $"{site.Site}/hop", inSitemap: true);
        site.Fetched($"{site.Site}/hop", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/end");
        site.Ok($"{site.Site}/end");
        site.Fetched($"{site.Site}/secure", FetchOutcome.Redirect, 301, redirectTo: "http://example.com/insecure");
        site.Ok("http://example.com/insecure");
        site.Fetched($"{site.Site}/moved", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/img/missing.jpg");
        site.Fetched($"{site.Site}/loop-a", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/loop-b");
        site.Fetched($"{site.Site}/loop-b", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/loop-a");
        site.Fetched($"{site.Site}/gone", FetchOutcome.ClientError, 410, inSitemap: true);
        site.Fetched("https://partner.example/listed", FetchOutcome.Success, 200, inSitemap: true);

        // A page that loads a script sent as HTML, and a stylesheet past Google's limit: both files, not pages.
        site.Fetched($"{site.Site}/app.js", FetchOutcome.Success, 200, contentType: "text/html", isParsed: false);
        site.Fetched($"{site.Site}/huge.css", FetchOutcome.Success, 200, contentType: "text/css", size: 3_000_000);
        var fileUser = site.Page($"{site.Site}/uses-files", RuleHarness.Html("<script src=\"/app.js\"></script><link rel=\"stylesheet\" href=\"/huge.css\">"));

        // The one page the crawl read (a sitemap led to it) is noindex. A site hidden from search is one whose home page
        // is noindex, and this one has no home page, so that check reports nothing here: what it flags is always a page.
        var hidden = site.Page($"{site.Site}/listed", RuleHarness.Html("<meta name=\"robots\" content=\"noindex\">"), IndexabilityReason.Noindex);

        // A file linked with ad tracking parameters, and one at a path that repeats: URLs, not pages (batch 7).
        var tracked = site.Fetched($"{site.Site}/brochure.pdf?utm_source=news", FetchOutcome.Success, 200, contentType: "application/pdf");
        var repeated = site.Fetched($"{site.Site}/files/files/a.pdf", FetchOutcome.Success, 200, contentType: "application/pdf");
        var urls = site.Table();
        urls.CountInlinks(site.Page($"{site.Site}/links", RuleHarness.Html(body: "<a href=\"/brochure.pdf?utm_source=news\">Brochure</a><a href=\"/files/files/a.pdf\">A</a>")));
        Assert.Equal(start, urls.HomeId);
        var visibility = new SiteVisibilityRules(urls);
        visibility.Observe(hidden);
        Assert.Empty(visibility.Finish());
        Assert.Equal(IssueSubject.Page, IssueCatalog.SITE_HIDDEN_FROM_SEARCH.Subject);

        var scope = new CrawlScope(new Uri($"{site.Site}/"), includeSubdomains: false, stayInStartFolder: false);
        var options = new CrawlOptions { StartUrl = site.Site };
        List<IssueHit> hits =
        [
            .. new ResponseCodeRules(urls, new Dictionary<int, string> { [blocked] = "Cloudflare challenge" }).Finish(),
            .. new RedirectRules(urls, site.UrlOf).Finish(),
            .. new RedirectHeaderRules(urls, () =>
                [
                    (site.Find($"{site.Site}/promo"),
                        [new ResponseHeader("link", "<https://example.com/elsewhere>; rel=\"canonical\""), new ResponseHeader("cache-control", "max-age=2592000")])
                ], site.UrlOf).Finish(),
            .. FilesUsed(urls, fileUser),
            .. new RobotsTxtRules(urls, [new("https://example.com", 503, null, null, 0), new("http://example.com", null, null, "Timed out", 0)], scope,
                new HashSet<string> { "https://example.com" }).Finish(),
            .. new RobotsTxtRules(urls, [new("https://example.com", 404, null, null, 0)], scope, new HashSet<string> { "https://example.com" }).Finish(),
            .. new SitemapRules(urls, [new($"{site.Site}/sitemap.xml", 200, false, 2, null), new($"{site.Site}/old.xml", 500, false, 0, "HTTP 500")], options,
                crawlComplete: true).Finish(),
            .. new SitemapRules(urls, [new($"{site.Site}/sitemap.xml", 404, false, 0, "HTTP 404", IsProbe: true)], options, crawlComplete: true).Finish(),
            .. new SitemapRules(urls,
                [
                    new($"{site.Site}/big.xml", 200, false, 60_000, null), new($"{site.Site}/near.xml", 200, false, 46_000, null),
                    new($"{site.Site}/empty.xml", 200, false, 0, null) { ListedSitemaps = 0 }
                ], options, crawlComplete: true,
                entries: new SitemapEntryFacts { Repeated = new Dictionary<int, IReadOnlyList<string>> { [blocked] = [$"{site.Site}/big.xml", $"{site.Site}/near.xml"] }, Lastmods = [new($"{site.Site}/big.xml", 1, 0)] }).Finish(),
            .. new RobotsTxtRules(urls, [new("https://example.com", 200, string.Empty, null, 0)], scope, new HashSet<string> { "https://example.com" },
                sitemaps: [new($"{site.Site}/sitemap.xml", 200, false, 2, null, IsProbe: true)]).Finish(),
            .. new UrlTextRules(urls, [(tracked, $"{site.Site}/brochure.pdf?utm_source=news"), (repeated, $"{site.Site}/files/files/a.pdf")]).Finish()
        ];

        var onNonPages = hits.Where(hit => !urls[hit.UrlId].IsPage).Select(hit => hit.Issue).Distinct().ToList();

        // Another version of the site answering is judged against a home page, so on a site that has one: the version is
        // only ever status-checked, which makes it no page.
        var versions = new RuleHarness();
        versions.Ok($"{versions.Site}/");
        var www = versions.Probed("https://www.example.com/", FetchOutcome.Success, 200);
        versions.Probed("http://www.example.com/", FetchOutcome.Redirect, 301, redirectTo: $"{versions.Site}/");
        versions.Probed("http://example.com/", FetchOutcome.Redirect, 301, redirectTo: $"{versions.Site}/");
        var versionUrls = versions.Table();
        var duplicate = Assert.Single(new SiteVersionRules(versionUrls, new Uri($"{versions.Site}/"), versions.Find, versions.UrlOf).Finish());
        Assert.Equal((IssueCatalog.DUPLICATE_SITE_VERSION, www), (duplicate.Issue, duplicate.UrlId));
        Assert.False(versionUrls[www].IsPage);
        onNonPages.Add(duplicate.Issue);

        var anyUrl = IssueCatalog.All.Where(check => check.Subject == IssueSubject.Url).ToList();
        Assert.Equal(anyUrl.Select(check => check.Id).Order(StringComparer.Ordinal), onNonPages.Select(check => check.Id).Order(StringComparer.Ordinal));

        // The same rules' other checks test for a page first, and so do the cross-page rules that gather theirs from the
        // pages audited: nothing but these and the checks above is reported on a URL by its id.
        Assert.All(PAGES_BY_TEST, check => Assert.Equal(IssueSubject.Page, check.Subject));
        Assert.DoesNotContain(hits, hit => PAGES_BY_TEST.Contains(hit.Issue) && !urls[hit.UrlId].IsPage);
    }
}
