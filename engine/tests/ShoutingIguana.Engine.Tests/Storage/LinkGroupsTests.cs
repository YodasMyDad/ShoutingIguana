using System.Collections.Concurrent;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using static ShoutingIguana.Engine.Tests.Support.GridQueries;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>
/// The links grouped by target: each target of a view's links once, with its links, the pages they're on, the text most
/// of them have, their positions and whether that's the template, in the order asked for; on one connection or a pass
/// split over several, with a link summary and without. Held to the links themselves: the view's links are listed by
/// the links grid, and grouped here.
/// </summary>
[Collection(GridCrawlCollection.NAME)]
public sealed class LinkGroupsTests(GridCrawl crawl) : IDisposable
{
    private readonly long _splitFrom = LinkGridReader.SplitFrom;
    private readonly int _passHelpers = LinkGridReader.PassHelpers;

    public void Dispose() => (LinkGridReader.SplitFrom, LinkGridReader.PassHelpers, LinkGridReader.Split) = (_splitFrom, _passHelpers, null);

    /// <summary>Views of the links grid: every link, what the link summary answers, what takes a pass, one end named, and what matches nothing.</summary>
    private static readonly (string Name, UrlFilter[] Filters, string? Search, bool NeedsSummary)[] VIEWS =
    [
        ("every link", [], null, false),
        ("hyperlinks", [Names("kind", FilterOperator.In, "hyperlink")], null, false),
        ("images", [Names("kind", FilterOperator.In, "image")], null, false),
        ("in the footer or the menu", [Names("position", FilterOperator.In, "footer", "navigation")], null, false),
        ("to broken URLs", [Names("to.statusClass", FilterOperator.In, "4xx", "5xx")], null, false),
        ("to other sites", [Boolean("to.isInternal", false)], null, false),
        ("to URLs with few inlinks", [Number("to.inlinks", FilterOperator.Lte, 3)], null, true),
        ("text contains", [Text("text", FilterOperator.Contains, "product")], null, false),
        ("from products", [Text("from.url", FilterOperator.Contains, "/p/")], null, false),
        ("from one page", [Number("from.id", FilterOperator.Eq, 1)], null, false),
        ("to one URL", [Number("to.id", FilterOperator.Eq, 1)], null, false),
        ("search", [], "about", false),
        ("nothing", [Text("text", FilterOperator.Contains, "no link says this at all")], null, false)
    ];

    /// <summary>A view's groups, worked out from its links as stored: by target, in the default order.</summary>
    private sealed record Expected(int Id, long Links, long Pages, string? Text, int Texts, LinkPosition[] Positions);

    [Fact]
    public void EachTargetOfAViewIsOneRowWithItsLinksPagesTextAndPositions()
    {
        var views = 0;
        foreach (var path in new[] { crawl.Path, crawl.Changed })
        {
            ReadCache cache = new();
            var urls = Urls(path);
            foreach (var (name, filters, search, _) in VIEWS.Where(view => path == crawl.Path || !view.NeedsSummary))
            {
                var (expected, sources) = Group(path, LinkIds(path, filters, search, cache));
                var read = ReadAll(path, new LinkGroupQuery { Filters = filters, Search = search, Limit = 37 }, cache, out var first);
                var label = $"{name}, {System.IO.Path.GetFileName(path)}";
                Assert.Equal(((long)expected.Count, expected.Sum(group => group.Links), sources), (first.Total, first.Links, first.Pages));
                Assert.Equal(new UrlSort(LinkGroupPage.PAGES, Descending: true), first.Sort);
                Assert.Equal(path == crawl.Path, first.Summary.Available);

                // The most linked from pages first, then the most links, then the order the crawl found the targets.
                var ordered = expected.OrderByDescending(group => group.Pages).ThenByDescending(group => group.Links).ThenBy(group => group.Id).ToList();
                Assert.True(ordered.Select(group => group.Id).SequenceEqual(read.Select(row => row.Id)), $"{label}: {ordered.Count} targets expected, {read.Count} read");
                foreach (var (want, row) in ordered.Zip(read))
                {
                    Assert.Equal((want.Id, urls[want.Id], want.Links, want.Pages, want.Text, want.Texts), (row.Id, row.Url, row.Links, row.Pages, row.Text, row.Texts));
                    Assert.Equal(want.Positions, row.Positions);
                    Assert.Equal(row.Pages >= IssueGroupReader.TEMPLATE_MIN_PAGES && row.Pages * 2 >= sources, row.LikelyTemplate);
                }

                views++;
            }
        }

        Assert.True(views >= 20, $"Only {views} views were grouped.");
    }

    /// <summary>
    /// A link in every page's template is one row, called the template: the synthetic site's menu links each page once,
    /// whatever else on it links there too. A target's facts are what a check's related URL reads.
    /// </summary>
    [Fact]
    public void ATemplateLinkIsOneRowCalledTheTemplateWithItsTargetsFacts()
    {
        var page = LinkGridReader.Groups(crawl.Path, new LinkGroupQuery { Filters = [Names("kind", FilterOperator.In, "hyperlink")], Limit = 5 });
        var top = page.Rows[0];
        Assert.True(top.LikelyTemplate, $"{top.Url}: {top.Pages} of {page.Pages} pages");
        Assert.InRange(top.Pages, page.Pages / 2, page.Pages);
        Assert.True(top.Links >= top.Pages);

        var broken = LinkGridReader.Groups(crawl.Path, new LinkGroupQuery { Filters = [Names("to.statusClass", FilterOperator.In, "4xx")], Limit = 200 });
        Assert.NotEmpty(broken.Rows);
        Assert.All(broken.Rows, row => Assert.InRange(row.Target.StatusCode ?? 0, 400, 499));
        Assert.All(broken.Rows, row => Assert.Null(row.Target.Final));
    }

    [Fact]
    public void TheGroupsSortByPagesLinksOrAColumnOfTheTarget()
    {
        ReadCache cache = new();
        UrlFilter[] filters = [Names("kind", FilterOperator.In, "hyperlink", "image")];
        var all = ReadAll(crawl.Path, new LinkGroupQuery { Filters = filters, Limit = 200 }, cache, out _);
        Assert.True(all.Count > 200, "The view has a page of targets and more.");

        List<LinkGroupRow> Sorted(UrlSort sort) => ReadAll(crawl.Path, new LinkGroupQuery { Filters = filters, Sort = sort, Limit = 61 }, cache, out _);

        Assert.Equal(all.OrderBy(row => row.Pages).ThenBy(row => row.Links).ThenBy(row => row.Id).Select(row => row.Id),
            Sorted(new UrlSort(LinkGroupPage.PAGES)).Select(row => row.Id));
        Assert.Equal(all.OrderByDescending(row => row.Links).ThenByDescending(row => row.Pages).ThenBy(row => row.Id).Select(row => row.Id),
            Sorted(new UrlSort(LinkGroupPage.LINKS, Descending: true)).Select(row => row.Id));

        // By a column of the target: in the order the links grid's own sort by it puts the targets.
        foreach (var sort in new[] { new UrlSort("to.url"), new UrlSort("to.statusCode", Descending: true), new UrlSort("to.sizeBytes", Descending: true) })
        {
            List<int> targets = [];
            for (var offset = 0; ; offset += 1_000)
            {
                var links = LinkGridReader.Query(crawl.Path, new UrlQuery { Columns = ["to.id"], Filters = filters, Sort = [sort], Offset = offset, Limit = 1_000 }, cache);
                targets.AddRange(links.Rows.Select(row => (int)(long)row.Values[1]!).Where(id => targets.Count == 0 || targets[^1] != id));
                if (links.Next is null)
                {
                    break;
                }
            }

            var groups = Sorted(sort);
            Assert.Equal(targets, groups.Select(row => row.Id));
            Assert.Equal(sort, LinkGridReader.Groups(crawl.Path, new LinkGroupQuery { Filters = filters, Sort = sort, Limit = 1 }, cache).Sort);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    public void APassSplitOverSeveralConnectionsGroupsAsOneDoes(int helpers)
    {
        LinkGridReader.SplitFrom = 1;
        LinkGridReader.PassHelpers = helpers;
        ConcurrentQueue<int> split = new();
        LinkGridReader.Split = split.Enqueue;
        foreach (var path in new[] { crawl.Path, crawl.Changed })
        {
            foreach (var (name, filters, search, _) in VIEWS.Where(view => !view.NeedsSummary))
            {
                var query = new LinkGroupQuery { Filters = filters, Search = search, Limit = 200 };
                split.Clear();
                var one = ReadAll(path, query, new ReadCache(), out var onePage);
                Assert.All(split, connections => Assert.Equal(1, connections));
                split.Clear();
                var several = ReadAll(path, query, new ReadCache { Helpers = ReadHelpers.UpTo(helpers) }, out var severalPage);
                Assert.Contains(helpers + 1, split);
                Assert.Equal((onePage.Total, onePage.Links, onePage.Pages), (severalPage.Total, severalPage.Links, severalPage.Pages));
                Assert.True(one.SequenceEqual(several, RowComparer.INSTANCE), $"{name}, {System.IO.Path.GetFileName(path)}: the groups differ on {helpers + 1} connections");
            }
        }
    }

    [Fact]
    public void AQueryTheGroupsCantAnswerIsRefusedByField()
    {
        void Refused(LinkGroupQuery query, string field)
        {
            var refused = Assert.Throws<UrlQueryException>(() => LinkGridReader.Groups(crawl.Path, query));
            Assert.Contains(field, refused.Errors.Keys);
        }

        Refused(new LinkGroupQuery { Sort = new UrlSort("kind") }, "sort.column");
        Refused(new LinkGroupQuery { Sort = new UrlSort("from.url") }, "sort.column");
        Refused(new LinkGroupQuery { Sort = new UrlSort("to.contentType") }, "sort.column");
        Refused(new LinkGroupQuery { Limit = 0 }, "limit");
        Refused(new LinkGroupQuery { Limit = LinkGroupPage.MAX_LIMIT + 1 }, "limit");
        Refused(new LinkGroupQuery { Offset = -1 }, "offset");
        Refused(new LinkGroupQuery { Filters = [Text("nothing", FilterOperator.Contains, "x")] }, "filters[0].column");

        // A sort by the target's column that needs the URL summary, on a crawl without one.
        Assert.Throws<NeedsAnalysisException>(() => LinkGridReader.Groups(crawl.Changed, new LinkGroupQuery { Sort = new UrlSort("to.inlinks", Descending: true) }));
        Assert.Empty(LinkGridReader.Groups(crawl.Path, new LinkGroupQuery { Offset = 1_000_000 }).Rows);
    }

    private sealed class RowComparer : IEqualityComparer<LinkGroupRow>
    {
        public static readonly RowComparer INSTANCE = new();

        public bool Equals(LinkGroupRow? x, LinkGroupRow? y) =>
            x is not null && y is not null && (x.Id, x.Url, x.Links, x.Pages, x.Text, x.Texts, x.LikelyTemplate, x.Target) == (y.Id, y.Url, y.Links, y.Pages, y.Text, y.Texts, y.LikelyTemplate, y.Target) &&
            x.Positions.SequenceEqual(y.Positions);

        public int GetHashCode(LinkGroupRow obj) => obj.Id;
    }

    private static List<LinkGroupRow> ReadAll(string path, LinkGroupQuery query, ReadCache cache, out LinkGroupPage first)
    {
        List<LinkGroupRow> rows = [];
        first = LinkGridReader.Groups(path, query, cache);
        for (var page = first; ; page = LinkGridReader.Groups(path, query with { Offset = rows.Count }, cache))
        {
            Assert.Equal(rows.Count, page.Offset);
            rows.AddRange(page.Rows);
            if (page.Rows.Count < query.Limit)
            {
                Assert.Equal(first.Total, rows.Count);
                return rows;
            }
        }
    }

    private static Dictionary<int, string> Urls(string path)
    {
        using var connection = CrawlDatabase.OpenReader(path);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, url FROM urls";
        using var reader = command.ExecuteReader();
        Dictionary<int, string> urls = [];
        while (reader.Read())
        {
            urls[reader.GetInt32(0)] = reader.GetString(1);
        }

        return urls;
    }

    /// <summary>The links a view matches, as the links grid lists them.</summary>
    private static List<long> LinkIds(string path, UrlFilter[] filters, string? search, ReadCache cache)
    {
        List<long> ids = [];
        for (var offset = 0; ; offset += 1_000)
        {
            var page = LinkGridReader.Query(path, new UrlQuery { Columns = ["kind"], Filters = filters, Search = search, Offset = offset, Limit = 1_000 }, cache);
            ids.AddRange(page.Rows.Select(row => row.Id));
            if (page.Next is null)
            {
                return ids;
            }
        }
    }

    /// <summary>Those links grouped by target, from the links as stored, in crawl order; and the pages they're on.</summary>
    private static (List<Expected> Groups, long Sources) Group(string path, List<long> ids)
    {
        List<(long Id, int From, int To, long Position, string? Text)> links = [];
        using (var connection = CrawlDatabase.OpenReader(path))
        {
            foreach (var chunk in ids.Chunk(5_000))
            {
                using var command = connection.CreateCommand();
                command.CommandText = $"SELECT rowid, from_id, to_id, position, text FROM links WHERE rowid IN ({string.Join(",", chunk)})";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    links.Add((reader.GetInt64(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt64(3), reader.IsDBNull(4) ? null : reader.GetString(4)));
                }
            }
        }

        var groups = links.GroupBy(link => link.To).Select(group =>
        {
            var sample = group.OrderBy(link => link.Id).Take(LinkGroupPage.TEXT_SAMPLE).Select((link, index) => (Text: link.Text ?? string.Empty, Index: index)).ToList();
            var top = sample.Where(entry => entry.Text.Length > 0).GroupBy(entry => entry.Text)
                .OrderByDescending(texts => texts.Count()).ThenBy(texts => texts.Min(entry => entry.Index)).Select(texts => texts.Key).FirstOrDefault();
            var positions = Enum.GetValues<LinkPosition>().Where(position => group.Any(link => link.Position == (long)position)).ToArray();
            return new Expected(group.Key, group.LongCount(), group.Select(link => link.From).Distinct().LongCount(), top,
                sample.Select(entry => entry.Text).Distinct().Count(), positions);
        }).ToList();
        return (groups, links.Select(link => link.From).Distinct().LongCount());
    }
}
