using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite.Models;
using static ShoutingIguana.Engine.Tests.Support.GridQueries;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>
/// The Issues screen's groups of a crawled and analysed site where pages share faults the way templates make them: a page
/// in the group of every URL its issue names, not just the first; a site-wide link to a section the template forgot; a
/// cluster of duplicates as one group; and faults whose detail differs on every page (a relative canonical's path, where
/// a JSON parser stopped) as one group each, likely the template's.
/// </summary>
public sealed class IssueTargetsTests : IAsyncLifetime
{
    private const string HTML = "text/html; charset=utf-8";

    /// <summary>Pages that write the same relative canonical and broken JSON-LD from one template.</summary>
    private const int PRODUCTS = 12;

    private readonly CrawlFiles _files = new();
    private TestWebHost _site = null!;
    private string _path = string.Empty;
    private Dictionary<string, int> _ids = [];

    public async Task InitializeAsync()
    {
        Dictionary<string, SyntheticResponse> responses = new(StringComparer.Ordinal)
        {
            ["/robots.txt"] = new(200, "text/plain; charset=utf-8", "User-agent: *\nDisallow:\n", null, null),
            ["/"] = Page("Home", body: string.Join(string.Empty,
                new[] { "/a", "/b", "/c", "/d", "/dup1", "/dup2", "/dup3" }.Concat(Enumerable.Range(0, PRODUCTS).Select(index => $"/p/{index}"))
                    .Select(path => $"<a href=\"{path}\">{path}</a> "))),
            ["/a"] = Page("A", body: """
                <a href="/gone-1">One</a> <a href="/gone-2">Two</a> <a href="/gone-3">Three</a>
                <img src="/big-1.jpg" alt="One" width="10" height="10"> <img src="/big-2.jpg" alt="Two" width="10" height="10">
                """),
            ["/b"] = Page("B", body: """<a href="/gone-2">Two</a> <img src="/big-2.jpg" alt="Two" width="10" height="10">"""),
            ["/c"] = Page("C", body: """<a href="/d#nowhere">A section of D that isn't there</a>"""),
            ["/d"] = Page("D"),
            ["/dup1"] = Page("Same title"),
            ["/dup2"] = Page("Same title"),
            ["/dup3"] = Page("Same title"),
            ["/big-1.jpg"] = new(200, "image/jpeg", null, new byte[300_000], null),
            ["/big-2.jpg"] = new(200, "image/jpeg", null, new byte[250_000], null)
        };
        for (var index = 0; index < PRODUCTS; index++)
        {
            // A comma missing after the name, which is longer on each page, so the parser stops at a different byte.
            responses[$"/p/{index}"] = Page($"Product {index}", $"Product {index}", head:
                $$"""<link rel="canonical" href="/p/{{index}}"><script type="application/ld+json">{"@context":"https://schema.org","@type":"Product","name":"Widget{{new string('x', index)}}" "sku":"{{index}}"}</script>""");
        }

        _site = await TestWebHost.StartAsync((_, _) => (path, _) =>
            responses.TryGetValue(path, out var response) ? response : new SyntheticResponse(404, HTML, "<p>Not found</p>", null, null));
        _path = _files.NewPath();
        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(new CrawlOptions { StartUrl = _site.BaseUri.AbsoluteUri, Concurrency = 2 }, _path)).Status);
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(_path);
        var origin = _site.BaseUri.GetLeftPart(UriPartial.Authority) + "/";
        _ids = TestCrawls.Rows(_path, "SELECT id, url FROM urls").Select(row => row.Split(" | ", 2)).Where(row => row[1].StartsWith(origin, StringComparison.Ordinal))
            .ToDictionary(row => new Uri(row[1]).PathAndQuery, row => int.Parse(row[0], System.Globalization.CultureInfo.InvariantCulture));
    }

    public async Task DisposeAsync()
    {
        await _site.DisposeAsync();
        _files.Dispose();
    }

    /// <summary>
    /// A page linking to three broken URLs is in the group of each, so a URL linked only after another broken one has its
    /// group, and names the pages linking to it; the footer's broken link is on every page, a likely template fault.
    /// </summary>
    [Fact]
    public void APageIsInTheGroupOfEveryBrokenUrlItLinksTo()
    {
        var pages = Pages();
        var groups = Groups("broken-internal-links");
        Assert.Equal(pages.Count, groups.Total);
        Assert.Equal(Paths(pages), Rows("broken-internal-links", "/gone-footer"));
        Assert.Equal(["/a", "/b"], Rows("broken-internal-links", "/gone-2"));
        Assert.Equal(["/a"], Rows("broken-internal-links", "/gone-3"));
        Assert.Equal(4, groups.GroupCount);
        Assert.True(Group(groups, "/gone-footer") is { LikelyTemplate: true, Share: 1 });
        Assert.False(Group(groups, "/gone-2").LikelyTemplate);

        // A page counts in each of its groups, so the shares add up to more than the whole.
        Assert.True(groups.Groups.Sum(group => group.Share) > 1);

        // The URL grid's check filter opens a group as its rows do.
        var opened = UrlGridReader.Query(_path, new UrlQuery
        {
            Filters = [Names("check", FilterOperator.In, "broken-internal-links") with { Group = new IssueGroupKey(null, _ids["/gone-3"]) }]
        });
        Assert.Equal([_ids["/a"]], opened.Rows.Select(row => row.Id));

        // Each broken URL is named by every page linking to it: the inspector's "named by".
        Assert.Equal([new CheckCount("broken-internal-links", 1)], NamedBy("/gone-3"));
        Assert.Equal([new CheckCount("broken-internal-links", 2)], NamedBy("/gone-2"));
        Assert.Equal([new CheckCount("broken-internal-links", pages.Count)], NamedBy("/gone-footer"));

        // The issue itself still counts them, and names the first.
        var a = UrlInspectorReader.Read(_path, _ids["/a"])!.Issues!.Single(issue => issue.CheckId == "broken-internal-links");
        Assert.Equal(("4 broken URLs", _ids["/gone-1"]), (a.Detail, a.Related!.Id));
    }

    [Fact]
    public void APageIsInTheGroupOfEveryLargeImageOnIt()
    {
        Assert.Equal(["/a", "/b"], Rows("image-too-large", "/big-2.jpg"));
        Assert.Equal(["/a"], Rows("image-too-large", "/big-1.jpg"));
        Assert.Equal([new CheckCount("image-too-large", 2)], NamedBy("/big-2.jpg"));
    }

    /// <summary>
    /// The template's "skip to content" link to a #main no page has is one group of every page, no other URL involved, and
    /// likely the template's; a page also missing a section on another page is in that page's group too.
    /// </summary>
    [Fact]
    public void ASectionMissingOnEveryPageIsOneGroup()
    {
        var pages = Pages();
        var groups = Groups("broken-fragment-links");
        var itself = Assert.Single(groups.Groups, group => group.RelatedUrlId is null);
        Assert.True(itself is { Pages: var count, LikelyTemplate: true } && count == pages.Count);
        Assert.Equal(Paths(pages), Rows("broken-fragment-links", null));
        Assert.Equal(["/c"], Rows("broken-fragment-links", "/d"));
        Assert.Equal(2, groups.GroupCount);

        // A page's issue names the other page first, and no row names the page itself.
        var c = UrlInspectorReader.Read(_path, _ids["/c"])!.Issues!.Single(issue => issue.CheckId == "broken-fragment-links");
        Assert.Equal(("2 links to missing sections: #main, /d#nowhere", _ids["/d"]), (c.Detail, c.Related!.Id));
        Assert.Null(UrlInspectorReader.Read(_path, _ids["/d"])!.Issues!.Single(issue => issue.CheckId == "broken-fragment-links").Related);
        Assert.DoesNotContain(NamedBy("/c"), check => check.CheckId == "broken-fragment-links");
    }

    /// <summary>Pages sharing a title are one group, named after the first of them, which is in it too.</summary>
    [Fact]
    public void DuplicatesAreOneGroupNamedAfterTheFirst()
    {
        foreach (var check in new[] { "title-duplicate", "h1-duplicate" })
        {
            var group = Assert.Single(Groups(check).Groups);
            Assert.Equal((_ids["/dup1"], 3L), (group.RelatedUrlId!.Value, group.Pages));
            Assert.Equal(["/dup1", "/dup2", "/dup3"], Rows(check, "/dup1"));
            Assert.Contains(new CheckCount(check, 2), NamedBy("/dup1"));
        }
    }

    /// <summary>
    /// A relative canonical (each page's own path) and JSON-LD missing a comma (the parser stopping at a different byte on
    /// each page) are one fault each: one group, the template's, whose rows keep each page's own detail.
    /// </summary>
    [Fact]
    public void AFaultEveryPageWritesItsOwnWayIsOneGroup()
    {
        var relative = Assert.Single(Groups("canonical-relative").Groups);
        Assert.Equal(("Root-relative: starts with /", (long)PRODUCTS, true), (relative.Detail, relative.Pages, relative.LikelyTemplate));
        var rows = IssueGroupReader.ReadRows(_path, "canonical-relative", new UrlQuery { Columns = ["detail"] }, new IssueGroupKey(relative.Detail, null))!.Page!;
        Assert.Equal(Enumerable.Range(0, PRODUCTS).Select(index => $"/p/{index}").Order(StringComparer.Ordinal),
            rows.Rows.Select(row => (string)row.Values[1]!).Order(StringComparer.Ordinal));

        var json = Assert.Single(Groups("structured-data-invalid").Groups);
        Assert.Equal(("'…' is invalid after a value. Expected either ',', '}', or ']'.", (long)PRODUCTS, true), (json.Detail, json.Pages, json.LikelyTemplate));
        var details = IssueGroupReader.ReadRows(_path, "structured-data-invalid", new UrlQuery { Columns = ["detail"] }, new IssueGroupKey(json.Detail, null))!.Page!
            .Rows.Select(row => (string)row.Values[1]!).ToList();
        Assert.Equal(PRODUCTS, details.Count);
        Assert.True(details.Distinct().Count() > 1, "Each page's own message says where its parser stopped");
        Assert.All(details, detail => Assert.Contains("BytePositionInLine", detail, StringComparison.Ordinal));

        // A group with another URL holds none of a fault's issues.
        Assert.Empty(IssueGroupReader.ReadRows(_path, "canonical-relative", new UrlQuery(), new IssueGroupKey(relative.Detail, _ids["/"]))!.Page!.Rows);
    }

    private static SyntheticResponse Page(string title, string? h1 = null, string body = "", string head = "") => new(200, HTML, $"""
        <!DOCTYPE html><html lang="en"><head><title>{title}</title>{head}</head>
        <body><a href="#main">Skip to content</a><nav><a href="/">Home</a></nav><h1>{h1 ?? title}</h1>{body}
        <footer><a href="/gone-footer">Old offers</a></footer></body></html>
        """, null, null);

    /// <summary>Every HTML page the crawl read.</summary>
    private List<int> Pages() => [.. TestCrawls.Rows(_path, "SELECT url_id FROM responses WHERE word_count IS NOT NULL").Select(int.Parse)];

    private List<string> Paths(IEnumerable<int> ids) => [.. ids.Select(id => _ids.Single(entry => entry.Value == id).Key).Order(StringComparer.Ordinal)];

    private IssueGroupPage Groups(string check) => IssueGroupReader.ReadGroups(_path, check, 0, 1_000)!;

    private IssueGroup Group(IssueGroupPage page, string path) => page.Groups.Single(group => group.RelatedUrlId == _ids[path]);

    /// <summary>The paths of a group's rows: the group of the URL at a path, or the group without another URL.</summary>
    private List<string> Rows(string check, string? path)
    {
        var page = IssueGroupReader.ReadRows(_path, check, new UrlQuery { Limit = 1_000 }, new IssueGroupKey(null, path is null ? null : _ids[path]))!.Page!;
        Assert.Equal(page.Rows.Count, page.Total);
        return Paths(page.Rows.Select(row => row.Id));
    }

    private IReadOnlyList<CheckCount> NamedBy(string path) => UrlInspectorReader.Read(_path, _ids[path])!.NamedBy!.Checks;
}
