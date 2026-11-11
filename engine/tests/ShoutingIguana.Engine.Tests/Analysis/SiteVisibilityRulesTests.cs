using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>
/// <c>site-hidden-from-search</c>: more than half of the pages read keep themselves out of search results, by noindex or
/// by a canonical to another URL. Its edges, one a test: what a page is, a site of one page, exactly half, a canonical
/// that doesn't count, and where it is reported.
/// </summary>
public sealed class SiteVisibilityRulesTests
{
    private const string NOINDEX = "<meta name=\"robots\" content=\"noindex\">";

    [Fact]
    public void MoreThanHalfOfThePagesHiddenIsReportedOnceOnTheHomePage()
    {
        var site = new RuleHarness();
        var home = site.Page($"{site.Site}/", RuleHarness.Html());
        AnalyzedPage[] pages =
        [
            home,
            site.Page($"{site.Site}/a", RuleHarness.Html(NOINDEX), IndexabilityReason.Noindex),
            site.Page($"{site.Site}/b", RuleHarness.Html(NOINDEX), IndexabilityReason.Noindex),
            site.Page($"{site.Site}/c", RuleHarness.Html(Canonical($"{site.Site}/")), IndexabilityReason.Canonicalised),
            site.Page($"{site.Site}/d", RuleHarness.Html())
        ];

        var hit = Assert.Single(Hits(site.Table(), pages));

        Assert.Equal(new IssueHit(IssueCatalog.SITE_HIDDEN_FROM_SEARCH, home.UrlId, "3 of 5 pages: 2 noindex, 1 canonicalised"), hit);
        Assert.Equal((IssueSeverity.Error, IssueCategory.Indexability, true), (hit.Issue.Severity, hit.Issue.Category, hit.Issue.SiteWide));
    }

    /// <summary>More than half is a strict majority: a site half of whose pages are hidden isn't one hidden from search.</summary>
    [Fact]
    public void ExactlyHalfIsNotMoreThanHalf()
    {
        var site = new RuleHarness();
        AnalyzedPage[] pages =
        [
            site.Page($"{site.Site}/", RuleHarness.Html()),
            site.Page($"{site.Site}/a", RuleHarness.Html(NOINDEX), IndexabilityReason.Noindex),
            site.Page($"{site.Site}/b", RuleHarness.Html(NOINDEX), IndexabilityReason.Noindex),
            site.Page($"{site.Site}/c", RuleHarness.Html())
        ];

        Assert.Empty(Hits(site.Table(), pages));

        // One more hidden page tips it.
        var fifth = site.Page($"{site.Site}/e", RuleHarness.Html(NOINDEX), IndexabilityReason.Noindex);
        Assert.Equal("3 of 5 pages: 3 noindex", Assert.Single(Hits(site.Table(), [.. pages, fifth])).Detail);
    }

    /// <summary>A site of one page is hidden when that page is: it is every page the crawl read.</summary>
    [Fact]
    public void ASiteOfOnePageIsHiddenWhenThatPageIs()
    {
        var hidden = new RuleHarness();
        var only = hidden.Page($"{hidden.Site}/", RuleHarness.Html(NOINDEX), IndexabilityReason.Noindex);
        Assert.Equal(new IssueHit(IssueCatalog.SITE_HIDDEN_FROM_SEARCH, only.UrlId, "1 of 1 page: 1 noindex"), Assert.Single(Hits(hidden.Table(), only)));

        var open = new RuleHarness();
        Assert.Empty(Hits(open.Table(), open.Page($"{open.Site}/", RuleHarness.Html())));
    }

    /// <summary>No page read, nothing to say: the URLs robots.txt kept the crawler out of weren't read, so they are no pages here.</summary>
    [Fact]
    public void UrlsRobotsTxtBlockedAreNeitherPagesNorHidden()
    {
        var site = new RuleHarness();
        site.Fetched($"{site.Site}/", FetchOutcome.RobotsDisallowed, 0);
        site.Fetched($"{site.Site}/private", FetchOutcome.RobotsDisallowed, 0);
        Assert.Empty(Hits(site.Table()));

        // Two blocked URLs beside two pages, one of them noindex: one of two pages, not three of four URLs.
        var mixed = new RuleHarness();
        mixed.Fetched($"{mixed.Site}/private/1", FetchOutcome.RobotsDisallowed, 0);
        mixed.Fetched($"{mixed.Site}/private/2", FetchOutcome.RobotsDisallowed, 0);
        AnalyzedPage[] pages = [mixed.Page($"{mixed.Site}/", RuleHarness.Html()), mixed.Page($"{mixed.Site}/a", RuleHarness.Html(NOINDEX), IndexabilityReason.Noindex)];
        Assert.Empty(Hits(mixed.Table(), pages));
    }

    /// <summary>
    /// A canonical to the same page on the site's other version (www or not, http or https) doesn't hide the page: it can
    /// appear in search, at its other address. A canonical to any other URL does.
    /// </summary>
    [Theory]
    [InlineData("https://www.example.com", false)]
    [InlineData("http://example.com", false)]
    [InlineData("http://www.example.com", false)]
    [InlineData("https://shop.example.com", true)]
    [InlineData("https://example.org", true)]
    public void ACanonicalToTheSamePageOnAnotherVersionOfTheSiteDoesNotCount(string canonicalOrigin, bool hidden)
    {
        var site = new RuleHarness();
        AnalyzedPage[] pages =
        [
            site.Page($"{site.Site}/", RuleHarness.Html(Canonical($"{canonicalOrigin}/")), IndexabilityReason.Canonicalised),
            site.Page($"{site.Site}/a?page=2", RuleHarness.Html(Canonical($"{canonicalOrigin}/a?page=2")), IndexabilityReason.Canonicalised),
            site.Page($"{site.Site}/b", RuleHarness.Html())
        ];

        var hits = Hits(site.Table(), pages);

        string[] expected = hidden ? ["2 of 3 pages: 2 canonicalised"] : [];
        Assert.Equal(expected, hits.Select(hit => hit.Detail));
    }

    /// <summary>The same host and scheme but another path is another page, whatever the version.</summary>
    [Fact]
    public void ACanonicalToAnotherPageOnTheOtherVersionCounts()
    {
        var site = new RuleHarness();
        AnalyzedPage[] pages =
        [
            site.Page($"{site.Site}/", RuleHarness.Html(Canonical("https://www.example.com/home")), IndexabilityReason.Canonicalised),
            site.Page($"{site.Site}/a", RuleHarness.Html(Canonical("https://www.example.com/")), IndexabilityReason.Canonicalised),
            site.Page($"{site.Site}/b", RuleHarness.Html())
        ];

        Assert.Equal("2 of 3 pages: 2 canonicalised", Assert.Single(Hits(site.Table(), pages)).Detail);
    }

    /// <summary>
    /// A canonical counts where the analysis has the page as canonicalised: one that leads back to the page through a
    /// redirect, or to a broken URL, leaves the page indexable, so it hides nothing.
    /// </summary>
    [Fact]
    public void ACanonicalThatLeadsBackOrNowhereDoesNotCount()
    {
        var site = new RuleHarness();
        var back = site.Page($"{site.Site}/back/", RuleHarness.Html(Canonical($"{site.Site}/back")), IndexabilityReason.Canonicalised);
        var nowhere = site.Page($"{site.Site}/nowhere", RuleHarness.Html(Canonical($"{site.Site}/gone")), IndexabilityReason.Canonicalised);
        var plain = site.Page($"{site.Site}/", RuleHarness.Html());
        var toBack = site.Fetched($"{site.Site}/back", FetchOutcome.Redirect, 301, redirectTo: $"{site.Site}/back/");
        var gone = site.Fetched($"{site.Site}/gone", FetchOutcome.ClientError, 404);
        var urls = site.Table(canonicals: [(back.UrlId, toBack), (nowhere.UrlId, gone)]);

        Assert.Equal((IndexabilityReason.None, IndexabilityReason.None), (urls[back.UrlId].IndexabilityReason, urls[nowhere.UrlId].IndexabilityReason));
        Assert.Empty(Hits(urls, back, nowhere, plain));

        // The same two pages naming a working page elsewhere are canonicalised, and the site is hidden.
        var working = site.Ok($"{site.Site}/main");
        var elsewhere = site.Table(canonicals: [(back.UrlId, working), (nowhere.UrlId, working)]);
        Assert.Equal("2 of 3 pages: 2 canonicalised", Assert.Single(Hits(elsewhere, back, nowhere, plain)).Detail);
    }

    /// <summary>
    /// A page that moves on with an instant meta refresh is read, and counts as a page, but search engines treat it as a
    /// redirect: its noindex hides nothing, as the noindex check doesn't flag it either.
    /// </summary>
    [Fact]
    public void APageThatMovesOnAtOnceIsAPageButNeverHidden()
    {
        var site = new RuleHarness();
        var moving = site.Page($"{site.Site}/old", RuleHarness.Html(NOINDEX + $"<meta http-equiv=\"refresh\" content=\"0; url={site.Site}/\">"), IndexabilityReason.Noindex);
        var hidden = site.Page($"{site.Site}/a", RuleHarness.Html(NOINDEX), IndexabilityReason.Noindex);
        var home = site.Page($"{site.Site}/", RuleHarness.Html());
        var urls = site.Table(instantRefreshes: [(moving.UrlId, home.UrlId)]);
        Assert.Equal(IndexabilityReason.Redirected, urls[moving.UrlId].IndexabilityReason);

        // One of three pages hidden; were the refresh page hidden too it would be two of three.
        Assert.Empty(Hits(urls, moving, hidden, home));
        Assert.DoesNotContain(new IndexabilityRules().Check(RuleHarness.AsAnalysed(moving, urls)), hit => hit.Issue == IssueCatalog.NOINDEX_PAGE);

        // Two of four: with the refresh page hidden too it would be three of four, and reported.
        var second = site.Page($"{site.Site}/b", RuleHarness.Html(NOINDEX), IndexabilityReason.Noindex);
        Assert.Empty(Hits(site.Table(instantRefreshes: [(moving.UrlId, home.UrlId)]), moving, hidden, home, second));
        var third = site.Page($"{site.Site}/c", RuleHarness.Html(NOINDEX), IndexabilityReason.Noindex);
        Assert.Equal("3 of 5 pages: 3 noindex", Assert.Single(Hits(site.Table(instantRefreshes: [(moving.UrlId, home.UrlId)]), moving, hidden, home, second, third)).Detail);
    }

    /// <summary>With no home page read (the start URL refuses the crawler, and a sitemap led to the pages), it is reported on the start URL.</summary>
    [Fact]
    public void WithoutAHomePageItIsReportedOnTheStartUrl()
    {
        var site = new RuleHarness();
        var start = site.Fetched($"{site.Site}/", FetchOutcome.ClientError, 403);
        var page = site.Page($"{site.Site}/listed", RuleHarness.Html(NOINDEX), IndexabilityReason.Noindex);
        var urls = site.Table();
        Assert.Equal(start, urls.HomeId);

        var hit = Assert.Single(Hits(urls, page));

        Assert.Equal((start, "1 of 1 page: 1 noindex"), (hit.UrlId, hit.Detail));
        Assert.False(urls[hit.UrlId].IsPage);
        Assert.Equal(IssueSubject.Url, hit.Issue.Subject);
    }

    /// <summary>The start URL redirects to the home page: that is where the site's faults are reported.</summary>
    [Fact]
    public void ItIsReportedOnThePageTheStartUrlLeadsTo()
    {
        var site = new RuleHarness("https://example.com");
        var home = site.Page("https://www.example.com/", RuleHarness.Html(NOINDEX), IndexabilityReason.Noindex);
        site.Fetched("https://example.com/", FetchOutcome.Redirect, 301, redirectTo: "https://www.example.com/");

        Assert.Equal(home.UrlId, Assert.Single(Hits(site.Table(), home)).UrlId);
    }

    /// <summary>The numbers read as numbers do everywhere in the details: thousands grouped, whatever the machine's culture.</summary>
    [Fact]
    public void TheDetailGroupsThousands()
    {
        var site = new RuleHarness();
        List<AnalyzedPage> pages = [site.Page($"{site.Site}/", RuleHarness.Html())];
        pages.AddRange(Enumerable.Range(1, 1_200).Select(number => site.Page($"{site.Site}/p/{number}", RuleHarness.Html(NOINDEX), IndexabilityReason.Noindex)));
        pages.AddRange(Enumerable.Range(1, 1_050).Select(number =>
            site.Page($"{site.Site}/v/{number}", RuleHarness.Html(Canonical($"{site.Site}/")), IndexabilityReason.Canonicalised)));

        Assert.Equal("2,250 of 2,251 pages: 1,200 noindex, 1,050 canonicalised", Assert.Single(Hits(site.Table(), [.. pages])).Detail);
    }

    /// <summary>The rule declares its one check, which a failure would skip rather than leave looking clean.</summary>
    [Fact]
    public void TheRuleDeclaresItsCheck() =>
        Assert.Equal([IssueCatalog.SITE_HIDDEN_FROM_SEARCH], new SiteVisibilityRules(new RuleHarness().Table()).Checks);

    private static string Canonical(string url) => $"<link rel=\"canonical\" href=\"{url}\">";

    /// <summary>The rule's hits once it has seen every page, each with the indexability the table settled on, as the analysis hands them over.</summary>
    private static List<IssueHit> Hits(UrlTable urls, params AnalyzedPage[] pages)
    {
        var rule = new SiteVisibilityRules(urls);
        foreach (var page in pages)
        {
            rule.Observe(RuleHarness.AsAnalysed(page, urls));
        }

        return [.. rule.Finish()];
    }
}
