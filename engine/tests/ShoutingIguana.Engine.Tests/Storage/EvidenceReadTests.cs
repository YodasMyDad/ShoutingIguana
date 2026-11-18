using System.Globalization;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Export;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.Engine.Urls;
using ShoutingIguana.TestSite;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>
/// Step 3b's evidence against the synthetic site's crawl: what a check's targets and groups say each URL is, the columns
/// that show a redirect's hops, robots.txt's rule, where a file is used and an image's pixels, and the order a check's rows
/// come in when none is asked for.
/// </summary>
[Collection(GridCrawlCollection.NAME)]
public sealed class EvidenceReadTests(GridCrawl crawl)
{
    [Fact]
    public void AnOversizedImageSaysItsSizeItsPixelsAndHowThePageShowsIt()
    {
        var page = Id("/plants/image-large");
        var target = FoundOnReader.ReadTargets(crawl.Path, IssueCatalog.IMAGE_TOO_LARGE.Id, [page])!.Single().Targets.Single();

        Assert.EndsWith("/img/large.jpg", target.Url, StringComparison.Ordinal);
        Assert.Equal(("image/jpeg", 300L * 1024, SyntheticSite.LARGE_IMAGE_WIDTH, SyntheticSite.LARGE_IMAGE_HEIGHT),
            (target.Facts!.ContentType, target.Facts.SizeBytes, target.Facts.PixelWidth, target.Facts.PixelHeight));
        Assert.Null(target.Facts.Final);
        Assert.Equal((LinkKind.Image, 1), (target.Kind, target.Links));
        Assert.Equal(new ImageOnPage("400", "300", "A very large photo", HasAlt: true, AltLength: 18, Srcset: false, Loading: null), target.Img);
    }

    [Fact]
    public void ARedirectSaysWhatItChangesInItsRowAndAsALinksTarget()
    {
        // In the grid: a plant's redirect to another page; the hub, which doesn't redirect, has none.
        var chain = UrlGridReader.Query(crawl.Path, new UrlQuery { Columns = ["redirectKind"], Filters = [GridQueries.Text("url", FilterOperator.Eq, Url(Id("/plants/chain-c")))] });
        Assert.Equal(RedirectKinds.OTHER, chain.Rows.Single().Values[1]);
        var hub = UrlGridReader.Query(crawl.Path, new UrlQuery { Columns = ["redirectKind"], Filters = [GridQueries.Text("url", FilterOperator.Eq, Url(Id("/plants/")))] });
        Assert.Null(hub.Rows.Single().Values[1]);
        Assert.Contains("redirectKind", IssueCatalog.REDIRECT_TEMPORARY.Evidence);

        // As a link's target: what the URL's own redirect changes, as its row says it.
        Assert.Equal(["statusCode", "final", "redirectKind", "text", "position", "links"], IssueCatalog.LINKS_TO_REDIRECTS.TargetFacts);
        var pages = crawl.Reference.Issues.Where(entry => entry.Value.Any(issue => issue.Issue == IssueCatalog.LINKS_TO_REDIRECTS.Id)).Select(entry => entry.Key).Take(20).ToList();
        var redirects = FoundOnReader.ReadTargets(crawl.Path, IssueCatalog.LINKS_TO_REDIRECTS.Id, pages)!.SelectMany(page => page.Targets)
            .Where(target => target.StatusCode is >= 300 and <= 399).ToList();
        Assert.NotEmpty(redirects);
        Assert.All(redirects, target => Assert.Equal(
            RedirectKinds.Of(target.Url, Query($"SELECT t.url FROM responses r JOIN urls t ON t.id = r.redirect_url_id WHERE r.url_id = {target.Id}").Single()),
            target.Facts!.RedirectKind));
    }

    [Fact]
    public void ASlowExternalLinkSaysHowLongThePageItLeadsToTookToAnswer()
    {
        var check = IssueCatalog.SLOW_EXTERNAL_LINKS;
        var target = FoundOnReader.ReadTargets(crawl.Path, check.Id, [Id("/plants/slow-external")])!.Single().Targets.Single();

        Assert.EndsWith("/ext/slow", target.Url, StringComparison.Ordinal);
        Assert.InRange(target.Facts!.ResponseMs ?? 0, 3_000, 60_000);
        Assert.Equal(["statusCode", "responseMs", "text", "position"], check.TargetFacts);
        Assert.Matches(@"^200; answered in 3\.[0-9] s; ", WhereFoundColumns.TargetFacts(check, target));
    }

    /// <summary>A URL written without its scheme says what the page loads or links there, and where, as mixed content's do.</summary>
    [Fact]
    public void AUrlWrittenWithoutItsSchemeSaysWhatThePageLoadsThere()
    {
        var check = IssueCatalog.PROTOCOL_RELATIVE_LINKS;
        var script = FoundOnReader.ReadTargets(crawl.Path, check.Id, [Id("/plants/protocol-relative")])!.Single().Targets[0];

        Assert.EndsWith("/static/app.js", script.Url, StringComparison.Ordinal);
        Assert.Matches("^[^;]+; script in the head$", WhereFoundColumns.TargetFacts(check, script));
    }

    [Fact]
    public void AnImageWithoutASizeSaysWhichAttributeIsMissingAndWhatToWrite()
    {
        var target = FoundOnReader.ReadTargets(crawl.Path, IssueCatalog.IMAGE_MISSING_DIMENSIONS.Id, [Id("/plants/image-no-size")])!.Single().Targets.Single();

        Assert.EndsWith("/img/2.jpg", target.Url, StringComparison.Ordinal);
        Assert.Equal((null, null), (target.Img!.Width, target.Img.Height));
        Assert.Equal((SyntheticSite.IMAGE_WIDTH, SyntheticSite.IMAGE_HEIGHT), (target.Facts!.PixelWidth, target.Facts.PixelHeight));
    }

    [Fact]
    public void ALinkToARedirectSaysWhereToLinkInstead()
    {
        var pages = crawl.Reference.Issues.Where(entry => entry.Value.Any(issue => issue.Issue == IssueCatalog.LINKS_TO_REDIRECTS.Id)).Select(entry => entry.Key).Take(20).ToList();
        var targets = FoundOnReader.ReadTargets(crawl.Path, IssueCatalog.LINKS_TO_REDIRECTS.Id, pages)!.SelectMany(page => page.Targets).ToList();

        Assert.NotEmpty(targets);
        // An HTTP redirect, or a page that moves on at once with a meta refresh (which answers 200), as the analysis follows them.
        Assert.Contains(targets, target => target.StatusCode is >= 300 and <= 399);
        Assert.All(targets, target =>
        {
            Assert.NotNull(target.Facts!.Final);
            Assert.True(target.Facts.Final!.Hops >= 1);
            Assert.Equal(target.Facts.Final.Url, Query($"SELECT f.url FROM url_summary s JOIN urls f ON f.id = s.final_url_id WHERE s.url_id = {target.Id}").Single());
        });
    }

    [Fact]
    public void ImageGroupsComeLargestFileFirstAndSayWhatTheImageIs()
    {
        var page = IssueGroupReader.ReadGroups(crawl.Path, IssueCatalog.IMAGE_TOO_LARGE.Id, 0, 100)!;

        Assert.Equal(IssueGroupSort.Size, page.Sort);
        var sizes = page.Groups.Select(group => group.Related!.SizeBytes ?? 0).ToList();
        Assert.Equal(sizes.OrderDescending(), sizes);
        Assert.Contains(page.Groups, group => group.Related is { PixelWidth: SyntheticSite.LARGE_IMAGE_WIDTH, ContentType: "image/jpeg" });

        // By pages when asked; by size only where the groups are URLs.
        Assert.Equal(IssueGroupSort.Pages, IssueGroupReader.ReadGroups(crawl.Path, IssueCatalog.IMAGE_TOO_LARGE.Id, 0, 100, IssueGroupSort.Pages)!.Sort);
        Assert.Throws<UrlQueryException>(() => IssueGroupReader.ReadGroups(crawl.Path, IssueCatalog.INTERNAL_CLIENT_ERROR.Id, 0, 100, IssueGroupSort.Size));
    }

    [Fact]
    public void ADuplicateClusterIsNamedByWhatItsPagesShare()
    {
        var descriptions = IssueGroupReader.ReadGroups(crawl.Path, IssueCatalog.DESCRIPTION_DUPLICATE.Id, 0, 100)!.Groups;
        var shared = descriptions.Single(group => group.SharedText?.StartsWith("Two planted pages share", StringComparison.Ordinal) == true);
        Assert.Equal(Query($"SELECT meta_description FROM responses WHERE url_id = {shared.RelatedUrlId}").Single(), shared.SharedText);

        var headings = IssueGroupReader.ReadGroups(crawl.Path, IssueCatalog.H1_DUPLICATE.Id, 0, 100)!.Groups;
        Assert.Contains(headings, group => group.SharedText == "Shared heading");

        // Only clusters of text are named by it.
        Assert.All(IssueGroupReader.ReadGroups(crawl.Path, IssueCatalog.BROKEN_INTERNAL_LINKS.Id, 0, 100)!.Groups, group => Assert.Null(group.SharedText));
    }

    [Fact]
    public void ACheckThatDoesntGroupSaysWhetherItIsOnNearlyEveryPage()
    {
        var pages = IssueReader.ReadAnalysis(crawl.Path)!.PagesAnalyzed;
        foreach (var check in IssueCatalog.All.Where(check => check.Grouping == IssueGrouping.None))
        {
            var page = IssueGroupReader.ReadGroups(crawl.Path, check.Id, 0, 1)!;
            Assert.Equal(page.Total is { } total && IssueGroupReader.IsLikelyTemplateCheck(check, total, pages), page.LikelyTemplate);
        }
    }

    [Fact]
    public void ACheckRowsComeMostImportantFirstWhenNoOrderIsAsked()
    {
        foreach (var check in new[] { IssueCatalog.TITLE_TOO_SHORT, IssueCatalog.INTERNAL_CLIENT_ERROR, IssueCatalog.IMAGE_MISSING_ALT })
        {
            var byDefault = IssueGroupReader.ReadRows(crawl.Path, check.Id, new UrlQuery { Limit = 1000 })!.Page!;
            var asked = IssueGroupReader.ReadRows(crawl.Path, check.Id, new UrlQuery
            {
                Limit = 1000,
                Sort = [new UrlSort(check.Subject == IssueSubject.Url ? "usedOn" : "inlinks", true), new UrlSort("depth"), new UrlSort("url")]
            })!.Page!;

            Assert.Equal(asked.Rows.Select(row => row.Id), byDefault.Rows.Select(row => row.Id));
            Assert.NotEqual(byDefault.Rows.Select(row => row.Id).Order(), byDefault.Rows.Select(row => row.Id));
        }
    }

    [Fact]
    public void TheColumnsShowARedirectsHopsAndTheRuleThatBlockedAUrl()
    {
        var loop = Id("/plants/loop-a");
        var rows = IssueGroupReader.ReadRows(crawl.Path, IssueCatalog.REDIRECT_LOOP.Id, new UrlQuery { Columns = ["url", "redirectChain"], Limit = 100 })!.Page!;
        var chain = (RedirectChain)rows.Rows.Single(row => row.Id == loop).Values[2]!;
        Assert.True(chain.Loop);
        Assert.Equal(["/plants/loop-b", "/plants/loop-a"], chain.Hops.Select(hop => new Uri(hop.Url).AbsolutePath));

        var blocked = IssueGroupReader.ReadRows(crawl.Path, IssueCatalog.BLOCKED_BY_ROBOTS.Id, new UrlQuery { Columns = ["robotsRule"], Limit = 100 })!.Page!;
        Assert.NotEmpty(blocked.Rows);
        Assert.All(blocked.Rows, row =>
        {
            var rule = Assert.IsType<RobotsRefusal>(row.Values[1]);
            Assert.Equal(RobotsBlockReason.Rule, rule.Reason);
            Assert.StartsWith("Disallow:", rule.Rule, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(rule.Line);
        });

        // A URL that doesn't redirect, or wasn't refused, has neither.
        var home = UrlGridReader.Query(crawl.Path, new UrlQuery { Columns = ["redirectChain", "robotsRule"], Filters = [GridQueries.Text("url", FilterOperator.Eq, Url(Id("/plants/")))] });
        Assert.Equal([null, null], home.Rows.Single().Values.Skip(1));
    }

    [Fact]
    public void AnImageSaysItsPixelsAndThePagesThatUseIt()
    {
        var large = Id("/img/large.jpg");
        var row = UrlGridReader.Query(crawl.Path, new UrlQuery
        {
            Columns = ["pixelWidth", "pixelHeight", "usedOn", "inlinks"], Filters = [GridQueries.Numbers("id", FilterOperator.In, large)]
        }).Rows.Single();
        Assert.Equal([(long)SyntheticSite.LARGE_IMAGE_WIDTH, (long)SyntheticSite.LARGE_IMAGE_HEIGHT], row.Values.Skip(1).Take(2));

        // Every page audited that names it, by any kind of link, once; hyperlinks alone say 0.
        var pages = Query($"""
            SELECT COUNT(DISTINCT l.from_id) FROM links l JOIN urls u ON u.id = l.from_id JOIN responses r ON r.url_id = l.from_id
            WHERE l.to_id = {large} AND l.from_id <> {large} AND {CrawlSchema.PAGE}
            """).Single();
        Assert.Equal(long.Parse(pages, CultureInfo.InvariantCulture), row.Values[3]);
        Assert.True((long)row.Values[3]! > 0);
        Assert.Equal(0L, row.Values[4]);
    }

    [Fact]
    public void APageWithAnInvalidLangShowsTheValueItWroteInItsOwnColumn()
    {
        Assert.Equal(["language"], IssueCatalog.LANG_INVALID.Evidence);
        var rows = IssueGroupReader.ReadRows(crawl.Path, IssueCatalog.LANG_INVALID.Id, new UrlQuery { Columns = ["language"], Limit = 10 })!.Page!;

        Assert.Equal("english", Assert.Single(rows.Rows).Values[1]);
        var hub = UrlGridReader.Query(crawl.Path, new UrlQuery { Columns = ["language"], Filters = [GridQueries.Text("url", FilterOperator.Eq, Url(Id("/plants/")))] });
        Assert.Equal("en", hub.Rows.Single().Values[1]);
    }

    [Fact]
    public void AUrlThatGaveNoAnswerSaysWhatWentWrong()
    {
        var rows = IssueGroupReader.ReadRows(crawl.Path, IssueCatalog.INTERNAL_UNREACHABLE.Id, new UrlQuery { Columns = ["fetchError"], Limit = 100 })!.Page!;

        Assert.NotEmpty(rows.Rows);
        Assert.All(rows.Rows, row => Assert.False(string.IsNullOrEmpty(row.Values[1] as string)));
    }

    private int Id(string path) => int.Parse(Query($"SELECT id FROM urls WHERE url LIKE '%{path}' ORDER BY length(url) LIMIT 1").Single(), CultureInfo.InvariantCulture);

    private string Url(int id) => Query($"SELECT url FROM urls WHERE id = {id}").Single();

    /// <summary>The first column of a query's rows, in its order.</summary>
    private List<string> Query(string sql)
    {
        using var connection = CrawlDatabase.OpenReader(crawl.Path);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        List<string> rows = [];
        while (reader.Read())
        {
            rows.Add(Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)!);
        }

        return rows;
    }
}
