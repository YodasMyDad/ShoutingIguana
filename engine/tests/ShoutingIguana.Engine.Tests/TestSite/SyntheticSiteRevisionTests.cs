using System.Net;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.TestSite;

/// <summary>
/// The synthetic site's revisions, which the fix loop's tests compare: switched on a running site, by a test in process
/// or through the control route by one in another process, and the comparison each pair of revisions must give.
/// </summary>
public sealed class SyntheticSiteRevisionTests
{
    private const int PAGES = 1_000;
    private static readonly Uri BASE = new("http://127.0.0.1:5000/");
    private static readonly Uri EXTERNAL = new("http://127.0.0.1:5001/");

    [Fact]
    public async Task TheControlRouteSwitchesTheRevisionOnlyWithTheToken()
    {
        await using var server = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 100 });
        using var client = new HttpClient { BaseAddress = server.BaseUri };
        Assert.Matches("^[0-9A-F]{32}$", server.ControlToken);

        async Task<HttpStatusCode> PostAsync(string path, string? token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path);
            if (token is not null)
            {
                request.Headers.Add(SyntheticSiteServer.CONTROL_TOKEN_HEADER, token);
            }

            using var response = await client.SendAsync(request);
            return response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.Forbidden, await PostAsync($"{SyntheticSiteServer.REVISION_ROUTE}1", null));
        Assert.Equal(HttpStatusCode.Forbidden, await PostAsync($"{SyntheticSiteServer.REVISION_ROUTE}1", new string('0', 32)));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.GetAsync($"{SyntheticSiteServer.REVISION_ROUTE}1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, await PostAsync($"{SyntheticSiteServer.REVISION_ROUTE}{SyntheticSite.LATEST_REVISION + 1}", server.ControlToken));
        Assert.Equal(0, server.Revision);

        // The robots.txt of revision 1 lets the crawler into one private URL; revision 0's doesn't.
        Assert.DoesNotContain("Allow:", await client.GetStringAsync("/robots.txt"));
        Assert.Equal(HttpStatusCode.NoContent, await PostAsync($"{SyntheticSiteServer.REVISION_ROUTE}1", server.ControlToken));
        Assert.Equal(1, server.Revision);
        Assert.Contains("Allow: /private/1", await client.GetStringAsync("/robots.txt"));

        Assert.Equal(HttpStatusCode.NoContent, await PostAsync($"{SyntheticSiteServer.REVISION_ROUTE}0", server.ControlToken));
        Assert.Equal(0, server.Revision);

        // The external site has no such route: it's a page it doesn't have.
        using var external = new HttpClient { BaseAddress = server.ExternalBaseUri };
        using var notThere = new HttpRequestMessage(HttpMethod.Post, $"{SyntheticSiteServer.REVISION_ROUTE}1");
        notThere.Headers.Add(SyntheticSiteServer.CONTROL_TOKEN_HEADER, server.ControlToken);
        Assert.Equal(HttpStatusCode.NotFound, (await external.SendAsync(notThere)).StatusCode);
        Assert.Equal(0, server.Revision);
    }

    [Fact]
    public void ARevisionTheSiteDoesntHaveIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SyntheticSite(new SyntheticSiteOptions { Revision = SyntheticSite.LATEST_REVISION + 1 }, BASE, EXTERNAL));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SyntheticSite(new SyntheticSiteOptions { Revision = -1 }, BASE, EXTERNAL));
    }

    /// <summary>
    /// Revision 1 plants every state a comparison can give between two complete crawls of the same options: a check
    /// new, fixed, fewer, more and the same with its rows moved; a row still, new, on a new URL, fixed, gone (no longer
    /// linked, and no longer found) and not checked. The two states only a crawl's options or a changed catalog give
    /// (a check new or not checked now, a row not compared then) are built from crawl files by the comparison's tests.
    /// </summary>
    [Fact]
    public void RevisionOnePlantsEveryStateOfAComparison()
    {
        var site = new SyntheticSite(new SyntheticSiteOptions { PageCount = PAGES }, BASE, EXTERNAL);
        var changes = site.GetChanges(0, 1, ExpectedJudging.Of);

        var states = changes.Checks.Values.Select(check => check.State).ToHashSet();
        Assert.Superset(new HashSet<SyntheticCheckState>
        {
            SyntheticCheckState.New, SyntheticCheckState.Fixed, SyntheticCheckState.Fewer, SyntheticCheckState.More, SyntheticCheckState.Same
        }, states);
        var rows = changes.Checks.Values.SelectMany(check => check.Rows.Values).ToHashSet();
        Assert.Superset(new HashSet<SyntheticRowState>
        {
            SyntheticRowState.Still, SyntheticRowState.New, SyntheticRowState.NewUrl, SyntheticRowState.Fixed, SyntheticRowState.Gone, SyntheticRowState.NotChecked
        }, rows);

        // The long titles: one shortened, one now an error page, one left, one new page.
        string Plant(string slug) => $"{BASE.AbsoluteUri.TrimEnd('/')}{SyntheticSite.PLANTS_PATH}{slug}";
        var long_ = changes.Checks["title-too-long"];
        Assert.Equal((SyntheticCheckState.Fewer, 3, 2), (long_.State, long_.Then, long_.Now));
        Assert.Equal(new Dictionary<string, SyntheticRowState>
        {
            [Plant("title-long")] = SyntheticRowState.Fixed,
            [Plant("title-caps")] = SyntheticRowState.NotChecked,
            [Plant("title-wide-long")] = SyntheticRowState.Still,
            [Plant("title-long-new")] = SyntheticRowState.NewUrl
        }, long_.Rows);

        // The page now an error is a new server error, judged then as a page that answered.
        Assert.Equal(SyntheticRowState.New, changes.Checks["internal-server-error"].Rows[Plant("title-caps")]);
        Assert.Equal(SyntheticCheckState.More, changes.Checks["internal-server-error"].State);

        // The short-title page is gone: removed, and nothing leads to it.
        var shortTitles = changes.Checks["title-too-short"];
        Assert.Equal((SyntheticCheckState.Fewer, SyntheticRowState.Gone), (shortTitles.State, shortTitles.Rows[Plant("title-short")]));

        // The second H1 moved: the same count, one fixed and one new.
        var h1 = changes.Checks["h1-multiple"];
        Assert.Equal((SyntheticCheckState.Same, 1, 1), (h1.State, h1.Count(SyntheticRowState.Fixed), h1.Count(SyntheticRowState.New)));

        // A check that found nothing finds something: the home page's missing header.
        Assert.Equal((SyntheticCheckState.New, 0, 1), (changes.Checks["security-headers-missing"].State, changes.Checks["security-headers-missing"].Then, changes.Checks["security-headers-missing"].Now));

        // The broken links: a quarter restored (fixed at the target), a quarter unlinked and a quarter moved (no longer
        // linked, so gone), and the moved ones' new targets are new URLs.
        var broken = changes.Checks["internal-client-error"];
        Assert.Equal(SyntheticCheckState.Fewer, broken.State);
        Assert.Equal(SyntheticRowState.Fixed, broken.Rows[$"{BASE.AbsoluteUri}broken/29"]);
        Assert.Equal(SyntheticRowState.Gone, broken.Rows[$"{BASE.AbsoluteUri}broken/58"]);
        Assert.Equal(SyntheticRowState.Gone, broken.Rows[$"{BASE.AbsoluteUri}broken/87"]);
        Assert.Equal(SyntheticRowState.NewUrl, broken.Rows[$"{BASE.AbsoluteUri}broken/87-relinked"]);
        Assert.Equal(SyntheticRowState.Still, broken.Rows[$"{BASE.AbsoluteUri}broken/116"]);
        Assert.Equal(SyntheticRowState.Fixed, changes.Checks["broken-internal-links"].Rows[$"{BASE.AbsoluteUri}p/29"]);
        Assert.Equal(SyntheticRowState.Fixed, changes.Checks["broken-internal-links"].Rows[$"{BASE.AbsoluteUri}p/58"]);
        Assert.Equal(SyntheticRowState.Still, changes.Checks["broken-internal-links"].Rows[$"{BASE.AbsoluteUri}p/87"]);

        // A duplicate-title pair whose first page was renamed and whose title another page took: one cluster, by the
        // page it keeps. One whose copy took its own title: both fixed.
        var titles = changes.Checks["title-duplicate"];
        Assert.Equal(SyntheticCheckState.Fewer, titles.State);
        Assert.Equal((SyntheticRowState.Fixed, SyntheticRowState.Fixed), (titles.Rows[$"{BASE.AbsoluteUri}p/36"], titles.Rows[$"{BASE.AbsoluteUri}p/37"]));
        Assert.Equal((SyntheticRowState.Fixed, SyntheticRowState.Still, SyntheticRowState.New),
            (titles.Rows[$"{BASE.AbsoluteUri}p/184"], titles.Rows[$"{BASE.AbsoluteUri}p/185"], titles.Rows[$"{BASE.AbsoluteUri}p/186"]));

        // Fixed whole: the large image, the sitemap index's missing file. One sitemap-only private URL now allowed.
        Assert.Equal(SyntheticCheckState.Fixed, changes.Checks["image-too-large"].State);
        Assert.Equal(SyntheticCheckState.Fixed, changes.Checks["sitemap-error"].State);
        Assert.Equal(SyntheticRowState.Fixed, changes.Checks["blocked-by-robots"].Rows[$"{BASE.AbsoluteUri}private/1"]);
        Assert.Equal(SyntheticRowState.Fixed, changes.Checks["not-in-sitemap"].Rows[$"{BASE.AbsoluteUri}c/0"]);
    }

    /// <summary>The same revision compared with itself changes nothing: every check the same, every row still.</summary>
    [Fact]
    public void ARevisionComparedWithItselfIsTheSame()
    {
        var changes = new SyntheticSite(new SyntheticSiteOptions { PageCount = PAGES }, BASE, EXTERNAL).GetChanges(1, 1, ExpectedJudging.Of);

        Assert.All(changes.Checks.Values, check => Assert.Equal(SyntheticCheckState.Same, check.State));
        Assert.All(changes.Checks.Values.SelectMany(check => check.Rows.Values), row => Assert.Equal(SyntheticRowState.Still, row));
    }

    /// <summary>A URL the site can't place fails the expected comparison rather than passing it.</summary>
    [Fact]
    public void AUrlTheSiteCantPlaceIsAnError()
    {
        var site = new SyntheticSite(new SyntheticSiteOptions { PageCount = PAGES }, BASE, EXTERNAL);

        Assert.Throws<ArgumentException>(() => site.Fate("http://elsewhere.example/"));
        Assert.Equal(SyntheticUrlFate.NotFound, site.AtRevision(1).Fate($"{BASE.AbsoluteUri}plants/title-short"));
        Assert.Equal(SyntheticUrlFate.NotFound, site.Fate($"{BASE.AbsoluteUri}plants/title-long-new"));
        Assert.Equal(SyntheticUrlFate.NotRead, site.Fate($"{BASE.AbsoluteUri}private/1"));
        Assert.Equal(SyntheticUrlFate.Page, site.AtRevision(1).Fate($"{BASE.AbsoluteUri}private/1"));
        Assert.Equal(SyntheticUrlFate.Answered, site.AtRevision(1).Fate($"{BASE.AbsoluteUri}plants/title-caps"));
    }
}
