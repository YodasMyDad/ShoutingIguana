using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using static ShoutingIguana.Engine.Tests.Support.GridQueries;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>
/// The links grid against the reference (<see cref="LinkReference"/>): every link a row, each filter's links and count,
/// every page by offset and by cursor in either direction, a sort by the target, the facets, and the same answers
/// whichever way a page is read: through the link summary (walking the links, through a rare filter's targets, from a set
/// of ids), without it, and without a URL summary.
/// </summary>
[Collection(GridCrawlCollection.NAME)]
public sealed class LinkGridReaderTests(GridCrawl crawl) : IDisposable
{
    private const int PAGE = 1_000;

    private static readonly string[] OWN_COLUMNS = ["kind", "text", "position", "noFollow", "sponsored", "ugc", "imageLink", "selfLink", "followed", "fragment"];

    private readonly int _driveCap = LinkGridReader.DriveCap;
    private readonly int _walkRows = LinkGridReader.WalkRows;
    private readonly int _listedEnds = LinkGridReader.ListedEnds;
    private LinkReference? _reference;

    private LinkReference Reference => _reference ??= LinkReference.Load(crawl.Path, crawl.Reference);

    public void Dispose()
    {
        (LinkGridReader.DriveCap, LinkGridReader.WalkRows, LinkGridReader.ListedEnds, LinkGridReader.UseBlocks, LinkGridReader.Ran) = (_driveCap, _walkRows, _listedEnds, true, null);
        GridReadContext.ReadWithoutLinkSummary = false;
    }

    /// <summary>
    /// How a page is read: as the reader chooses (from the links counted by block, or through a filter's targets), by walking
    /// the links, from a set of ids, and without either summary.
    /// </summary>
    public enum Way
    {
        Chosen,
        Walked,
        FromASet,
        WithoutLinkSummary,
        WithoutUrlSummary
    }

    /// <summary>Filter sets by name: on what a link is, on either end, on what the link summary holds and on what it doesn't.</summary>
    private Dictionary<string, (UrlFilter[] Filters, string? Search)> FilterSets() => new(StringComparer.Ordinal)
    {
        ["hyperlinks"] = ([Names("kind", FilterOperator.In, "hyperlink")], null),
        ["not hyperlinks or images"] = ([Names("kind", FilterOperator.NotIn, "hyperlink", "image")], null),
        ["in the footer"] = ([Names("position", FilterOperator.In, "footer")], null),
        ["navigation and header"] = ([Names("position", FilterOperator.In, "navigation", "header")], null),
        ["nofollow"] = ([Boolean("noFollow", true)], null),
        ["followed"] = ([Boolean("followed", true)], null),
        ["not followed"] = ([Boolean("followed", false)], null),
        ["nothing to follow"] = ([new UrlFilter("followed", FilterOperator.IsEmpty)], null),
        ["to their own page"] = ([Boolean("selfLink", true)], null),
        ["image links"] = ([Boolean("imageLink", true)], null),
        ["without text"] = ([new UrlFilter("text", FilterOperator.IsEmpty)], null),
        ["hyperlinks with text"] = ([Names("kind", FilterOperator.In, "hyperlink"), new UrlFilter("text", FilterOperator.IsNotEmpty)], null),
        ["text contains"] = ([Text("text", FilterOperator.Contains, "product")], null),
        ["text doesn't contain"] = ([Text("text", FilterOperator.NotContains, "PRODUCT")], null),
        ["text matches"] = ([Text("text", FilterOperator.Regex, @"^Product \d+$")], null),
        ["to a section"] = ([new UrlFilter("fragment", FilterOperator.IsNotEmpty)], null),
        ["to broken URLs"] = ([Names("to.statusClass", FilterOperator.In, "4xx")], null),
        ["hyperlinks to 404s"] = ([Number("to.statusCode", FilterOperator.Eq, 404), Names("kind", FilterOperator.In, "hyperlink")], null),
        ["to other sites"] = ([Boolean("to.isInternal", false)], null),
        ["to URLs like"] = ([Text("to.url", FilterOperator.Contains, "/p/1")], null),
        ["to much-linked URLs"] = ([Number("to.inlinks", FilterOperator.Gte, 100)], null),
        ["to flagged URLs"] = ([Names("to.check", FilterOperator.In, "internal-client-error")], null),
        ["to titles like"] = ([Text("to.title", FilterOperator.Contains, "Product 1")], null),
        ["to pages not fetched"] = ([Boolean("to.fetched", false)], null),
        ["from depth 1"] = ([Number("from.depth", FilterOperator.Eq, 1)], null),
        ["from one page"] = ([Text("from.url", FilterOperator.EndsWith, "/p/7")], null),
        ["from a page by id"] = ([Number("from.id", FilterOperator.Eq, UrlId("/p/7"))], null),
        ["from non-indexable pages"] = ([Boolean("from.indexable", false)], null),
        ["to the home page by id"] = ([Numbers("to.id", FilterOperator.In, UrlId("/"), UrlId("/p/3"))], null),
        ["by link id"] = ([new UrlFilter("id", FilterOperator.Between) { Min = 100, Max = 5_000 }], null),
        ["search"] = ([], "ABOUT"),
        ["search within hyperlinks"] = ([Names("kind", FilterOperator.In, "hyperlink")], "/p/2"),
        ["content hyperlinks that work"] = ([Names("kind", FilterOperator.In, "hyperlink"), Names("position", FilterOperator.In, "content"), Names("to.statusClass", FilterOperator.In, "2xx")], null),
        ["nofollowed to the site"] = ([Boolean("followed", false), Boolean("to.isInternal", true)], null),
        ["nothing"] = ([Names("kind", FilterOperator.In, "icon"), Boolean("noFollow", true)], null)
    };

    [Fact]
    public void EveryLinkIsARowInCrawlOrder()
    {
        var all = Reference.Links;
        Assert.True(all.Count > 5_000);
        foreach (var way in Enum.GetValues<Way>())
        {
            var (path, cache) = Take(way);
            Assert.Equal(all.Select(link => link.Id), ByOffset(path, new UrlQuery(), cache));
            Assert.Equal(all.Select(link => link.Id).Reverse(), ByOffset(path, new UrlQuery { Sort = [new UrlSort("id", true)] }, cache));
            Assert.Equal(all.Select(link => link.Id), ByCursor(path, new UrlQuery(), cache));
            Assert.Equal(all.Select(link => link.Id).Reverse(), ByCursor(path, new UrlQuery { Sort = [new UrlSort("id", true)] }, cache));
        }

        // A page says what it is: the columns asked for, the link's id first, the count of every link, and where it started.
        var page = LinkGridReader.Query(crawl.Path, new UrlQuery { Offset = 40, Limit = 25 });
        Assert.Equal(["id", "from.url", "to.url", "kind", "text"], page.Columns);
        Assert.Equal((40, all.Count, all.Count), (page.Offset, page.Matching, page.Total));
        Assert.Equal(all.Skip(40).Take(25).Select(link => link.Id), page.Rows.Select(row => row.Id));
        Assert.Equal(SummaryState.Present, page.Summary);
        Assert.Empty(page.Unavailable);
        Assert.NotNull(page.Next);
        Assert.Null(LinkGridReader.Query(crawl.Path, new UrlQuery { Offset = all.Count - 5, Limit = 25 }).Next);
        Assert.Empty(LinkGridReader.Query(crawl.Path, new UrlQuery { Offset = all.Count + 5 }).Rows);
    }

    [Fact]
    public void ARowCarriesTheLinkAndTheUrlGridsColumnsOfEitherEnd()
    {
        string[] columns = [.. OWN_COLUMNS, "from.id", "from.url", "from.depth", "from.indexable", "to.id", "to.url", "to.statusCode", "to.statusClass", "to.type", "to.inlinks", "to.title"];
        foreach (var (name, (filters, search)) in FilterSets())
        {
            var page = LinkGridReader.Query(crawl.Path, new UrlQuery { Columns = columns, Filters = filters, Search = search, Limit = 200 });
            Assert.Equal(columns.Prepend("id"), page.Columns);
            Assert.All(page.Rows, row =>
            {
                var link = Reference.ById[row.Id];
                Assert.Equal(row.Id, row.Values[0]);
                for (var index = 0; index < columns.Length; index++)
                {
                    var (expected, actual) = (Reference.Value(link, columns[index]), row.Values[index + 1]);
                    Assert.True(Same(expected, actual), $"{name}: link {row.Id} {columns[index]} is {actual ?? "null"}, expected {expected ?? "null"}");
                }
            });
        }

        // Without a URL summary, the columns that need it are left out and named, as in the URL grid.
        var changed = LinkGridReader.Query(crawl.Changed, new UrlQuery { Columns = ["to.inlinks", "kind", "from.issues", "to.url", "followed"], Limit = 50 });
        Assert.Equal(["id", "kind", "to.url", "followed"], changed.Columns);
        Assert.Equal(["from.issues", "to.inlinks"], changed.Unavailable);
        Assert.Equal(new SummaryState(false, SummaryUnavailableReason.CrawlChanged), changed.Summary);
        Assert.All(changed.Rows, row => Assert.True(Same(Reference.Value(Reference.ById[row.Id], "followed"), row.Values[3])));
    }

    [Theory]
    [InlineData(Way.Chosen)]
    [InlineData(Way.Walked)]
    [InlineData(Way.FromASet)]
    [InlineData(Way.WithoutLinkSummary)]
    [InlineData(Way.WithoutUrlSummary)]
    public void AFilterSetsLinksAreTheSameHoweverTheyAreRead(Way way)
    {
        var (path, cache) = Take(way);
        foreach (var (name, (filters, search)) in FilterSets())
        {
            // A filter on what only an analysis counts is refused without one, as in the URL grid.
            if (way == Way.WithoutUrlSummary && name == "to much-linked URLs")
            {
                var refused = Assert.Throws<NeedsAnalysisException>(() => LinkGridReader.Query(path, new UrlQuery { Filters = filters }, cache));
                Assert.Equal((SummaryUnavailableReason.CrawlChanged, "to.inlinks"), (refused.Reason, Assert.Single(refused.Columns)));
                continue;
            }

            var expected = Reference.Matching(filters, search).Select(link => link.Id).ToList();
            var query = new UrlQuery { Filters = filters, Search = search };
            var first = LinkGridReader.Query(path, query with { Limit = 10 }, cache);
            Assert.True(expected.Count == first.Matching, $"{name}: {first.Matching} match, expected {expected.Count}");
            Assert.Equal(Reference.Links.Count, first.Total);
            Assert.Equal(expected, ByOffset(path, query, cache));
            Assert.Equal(Enumerable.Reverse(expected), ByOffset(path, query with { Sort = [new UrlSort("id", true)] }, cache));
            Assert.Equal(expected, ByCursor(path, query, cache));
            Assert.Equal(Enumerable.Reverse(expected), ByCursor(path, query with { Sort = [new UrlSort("id", true)] }, cache));

            // A page from the middle, read on its own, nothing kept from the pages before it.
            if (expected.Count > 30)
            {
                var middle = LinkGridReader.Query(path, query with { Offset = expected.Count / 2, Limit = 7 });
                Assert.Equal(expected.Skip(expected.Count / 2).Take(7), middle.Rows.Select(row => row.Id));
                Assert.Equal(expected.Count, middle.Matching);
            }
        }

        Assert.NotEmpty(Reference.Matching(FilterSets()["nofollowed to the site"].Filters));
        Assert.Empty(Reference.Matching(FilterSets()["nothing"].Filters));
    }

    /// <summary>
    /// Each way of reading a page is taken where it should be: a filter on what a link is from the blocks that hold the page,
    /// a filter on the target through its targets when they have few links, a page near an end by walking, a page in the
    /// middle from a set of ids read once; and the links themselves for the count when a filter tests what the summary
    /// doesn't hold.
    /// </summary>
    [Fact]
    public void APageIsReadTheWayItsFilterAllows()
    {
        List<string> ran = [];
        LinkGridReader.Ran = ran.Add;
        bool Scanned(string sql) => sql.Contains("si_link(", StringComparison.Ordinal);
        bool Blocks(string sql) => sql.Contains("l.rowid BETWEEN $low AND $high", StringComparison.Ordinal);
        var hyperlinks = new UrlQuery { Filters = [Names("kind", FilterOperator.In, "hyperlink")], Limit = 20 };
        var half = Reference.Matching(hyperlinks.Filters).Count / 2;
        Assert.True(half > 3_000);

        // Filters on what a link is, rare or common, at any place: counted by block, and read from the blocks the page is in.
        foreach (var query in new[] { new UrlQuery { Filters = [Boolean("noFollow", true)] }, hyperlinks, hyperlinks with { Offset = half }, hyperlinks with { Offset = half * 2 - 30 } })
        {
            ran.Clear();
            var page = LinkGridReader.Query(crawl.Path, query);
            Assert.Equal(Reference.Matching(query.Filters).Skip(query.Offset).Take(query.Limit).Select(link => link.Id), page.Rows.Select(row => row.Id));
            Assert.StartsWith("SELECT c.block, SUM(c.links) FROM link_blocks c WHERE", ran[0], StringComparison.Ordinal);
            Assert.All(ran.Skip(1), sql => Assert.True(Blocks(sql)));
            Assert.InRange(ran.Count, 2, 3);
        }

        // A filter on the target, whose targets have few links in all: the summary names them, and the links' index by target
        // finds their links, which are kept: the next page reads none.
        var cache = new ReadCache();
        var broken = new UrlQuery { Filters = [Names("to.statusClass", FilterOperator.In, "4xx")], Limit = 20 };
        ran.Clear();
        LinkGridReader.Query(crawl.Path, broken, cache);
        Assert.Contains(ran, sql => sql.Contains("FROM link_summary c", StringComparison.Ordinal));
        Assert.Contains(ran, sql => sql.Contains("INDEXED BY ix_links_to", StringComparison.Ordinal) && sql.Contains("$driven", StringComparison.Ordinal));
        Assert.DoesNotContain(ran, Scanned);
        ran.Clear();
        LinkGridReader.Query(crawl.Path, broken with { Offset = 20 }, cache);
        Assert.Empty(ran);

        // A filter on the target that most targets pass: its first and last pages are walked from the nearer end.
        var internalLinks = new UrlQuery { Filters = [Boolean("to.isInternal", true)], Limit = 20 };
        var matching = Reference.Matching(internalLinks.Filters).Count;
        (LinkGridReader.DriveCap, LinkGridReader.WalkRows, LinkGridReader.ListedEnds) = (50, 2_000, 10);
        ran.Clear();
        LinkGridReader.Query(crawl.Path, internalLinks);
        LinkGridReader.Query(crawl.Path, internalLinks with { Offset = matching - 20 });
        Assert.Equal(1, ran.Count(sql => sql.Contains("ORDER BY l.rowid ASC", StringComparison.Ordinal)));
        Assert.Equal(1, ran.Count(sql => sql.Contains("ORDER BY l.rowid DESC", StringComparison.Ordinal)));
        Assert.DoesNotContain(ran, sql => Scanned(sql) || Blocks(sql) || sql.Contains("$driven", StringComparison.Ordinal));

        // And a page in its middle: the matching links' ids are read once, in one pass, and kept; the next pages read no link for them.
        cache = new ReadCache();
        ran.Clear();
        LinkGridReader.Query(crawl.Path, internalLinks with { Offset = matching / 2 }, cache);
        Assert.Single(ran, Scanned);
        ran.Clear();
        LinkGridReader.Query(crawl.Path, internalLinks with { Offset = matching / 2 - 500 }, cache);
        LinkGridReader.Query(crawl.Path, internalLinks with { Offset = 20 }, cache);
        Assert.Empty(ran);

        // A filter on a link's text is beyond the summary: one pass over the links gives its count, its ids and its facets.
        (LinkGridReader.DriveCap, LinkGridReader.WalkRows, LinkGridReader.ListedEnds) = (_driveCap, _walkRows, _listedEnds);
        var text = new UrlQuery { Filters = [Text("text", FilterOperator.Contains, "product")], Limit = 20 };
        ran.Clear();
        LinkGridReader.Query(crawl.Path, text, cache);
        Assert.True(Scanned(Assert.Single(ran)));
        Assert.Contains("links l NOT INDEXED", ran[0], StringComparison.Ordinal);
        ran.Clear();
        LinkGridReader.Query(crawl.Path, text with { Offset = 3_000 }, cache);
        LinkGridReader.Facets(crawl.Path, new UrlFacetQuery { Filters = text.Filters }, cache);
        LinkGridReader.Query(crawl.Path, text with { Sort = [new UrlSort("to.statusCode")] }, cache);
        Assert.DoesNotContain(ran, Scanned);

        // The links from one page are found on the links' index by source, not by reading every link; so are those from the
        // few pages a filter on the source matches.
        foreach (var from in new[] { Number("from.id", FilterOperator.Eq, UrlId("/p/7")), Text("from.url", FilterOperator.EndsWith, "/p/7") })
        {
            ran.Clear();
            LinkGridReader.Query(crawl.Path, new UrlQuery { Filters = [from] });
            Assert.DoesNotContain("NOT INDEXED", Assert.Single(ran), StringComparison.Ordinal);
        }

        // No filter at all reads no link to count or to find a page: the ids are the places.
        ran.Clear();
        LinkGridReader.Query(crawl.Path, new UrlQuery { Offset = 7_000 });
        Assert.Empty(ran);
    }

    [Theory]
    [InlineData(Way.Chosen)]
    [InlineData(Way.WithoutLinkSummary)]
    [InlineData(Way.WithoutUrlSummary)]
    public void ASortByTheTargetKeepsItsLinksTogether(Way way)
    {
        var (path, cache) = Take(way);
        var sets = FilterSets();
        foreach (var name in new[] { "", "hyperlinks", "text contains", "to broken URLs", "from depth 1", "nothing" })
        {
            var (filters, search) = name.Length == 0 ? ([], null) : sets[name];
            var matching = Reference.Matching(filters, search);
            foreach (var sort in new[]
                     {
                         new UrlSort("to.url"), new UrlSort("to.url", true), new UrlSort("to.statusCode"), new UrlSort("to.statusCode", true), new UrlSort("to.statusClass"),
                         new UrlSort("to.depth", true), new UrlSort("to.type"), new UrlSort("to.inlinks", true), new UrlSort("to.id", true)
                     })
            {
                var query = new UrlQuery { Filters = filters, Search = search, Sort = [sort] };
                if (way == Way.WithoutUrlSummary && sort.Column == "to.inlinks")
                {
                    Assert.Equal(["to.inlinks"], Assert.Throws<NeedsAnalysisException>(() => LinkGridReader.Query(path, query, cache)).Columns);
                    continue;
                }

                var expected = Reference.Order(matching, sort).Select(link => link.Id).ToList();
                Assert.True(expected.SequenceEqual(ByOffset(path, query, cache)), $"{name} by {sort.Column} {(sort.Descending ? "desc" : "asc")}, by offset");
                Assert.True(expected.SequenceEqual(ByCursor(path, query, cache, 333)), $"{name} by {sort.Column} {(sort.Descending ? "desc" : "asc")}, by cursor");
                if (expected.Count > 50)
                {
                    var middle = LinkGridReader.Query(path, query with { Offset = expected.Count / 2 - 3, Limit = 11 });
                    Assert.Equal(expected.Skip(expected.Count / 2 - 3).Take(11), middle.Rows.Select(row => row.Id));
                }
            }
        }
    }

    [Theory]
    [InlineData(Way.Chosen)]
    [InlineData(Way.WithoutLinkSummary)]
    [InlineData(Way.WithoutUrlSummary)]
    public void EveryFacetCountOpensItsLinks(Way way)
    {
        var (path, cache) = Take(way);
        var sets = FilterSets();
        foreach (var name in new[] { "", "hyperlinks", "text contains", "to other sites", "from depth 1", "search", "nothing" })
        {
            var (filters, search) = name.Length == 0 ? ([], null) : sets[name];
            var matching = Reference.Matching(filters, search);
            var facets = LinkGridReader.Facets(path, new UrlFacetQuery { Filters = filters, Search = search }, cache);
            Assert.Equal((matching.Count, Reference.Links.Count), (facets.Matching, facets.Total));
            Assert.Equal(LinkGridReader.FACETS, facets.Facets.Keys);
            Assert.Empty(facets.Unavailable);

            // Each facet's values are the reference's, with their counts: every link counts once in each.
            AssertBuckets(matching.GroupBy(link => Reference.Value(link, "kind")), facets.Facets["kind"]);
            AssertBuckets(matching.GroupBy(link => Reference.Value(link, "position")), facets.Facets["position"]);
            AssertBuckets(matching.GroupBy(link => (object?)(link.Followed switch { true => "followed", false => "nofollow", _ => "notHyperlink" })), facets.Facets["follow"]);
            AssertBuckets(matching.GroupBy(link => Reference.Value(link, "to.statusClass")), facets.Facets["to.statusClass"]);
            AssertBuckets(matching.GroupBy(link => Reference.Value(link, "to.isInternal")), facets.Facets["to.isInternal"]);

            // And its filter, added to the query's, gives a grid of exactly that many links.
            foreach (var bucket in facets.Facets.Values.SelectMany(buckets => buckets))
            {
                var opened = LinkGridReader.Query(path, new UrlQuery { Filters = [.. filters, bucket.Filter], Search = search, Limit = 1 }, cache);
                Assert.True(bucket.Count == opened.Matching, $"{name}: {bucket.Filter.Column} {bucket.Value ?? "null"} counts {bucket.Count} and opens {opened.Matching}");
            }
        }

        var asked = LinkGridReader.Facets(path, new UrlFacetQuery { Facets = ["follow", "kind"] }, cache);
        Assert.Equal(["follow", "kind"], asked.Facets.Keys);
        Assert.Equal(["followed", "nofollow", "notHyperlink"], asked.Facets["follow"].Select(bucket => bucket.Value));
    }

    /// <summary>
    /// The link summary is read only while it counts the crawl as it stands: once a run of the crawl has moved its stamp on,
    /// or when its tables aren't this engine's, it's passed over and the links read instead, to the same answers. It
    /// stands whoever analysed the crawl last: it counts the links, which no analysis changes.
    /// </summary>
    [Fact]
    public void ALinkSummaryIsReadOnlyWhileItCountsTheCrawlAsItStands()
    {
        var query = new UrlQuery { Filters = [Names("kind", FilterOperator.In, "image")], Limit = 50 };
        var expected = Reference.Matching(query.Filters).Select(link => link.Id).ToList();
        void AssertRead(string path, bool fromTheSummary)
        {
            List<string> ran = [];
            LinkGridReader.Ran = ran.Add;
            var page = LinkGridReader.Query(path, query);
            var facets = LinkGridReader.Facets(path, new UrlFacetQuery { Filters = query.Filters });
            LinkGridReader.Ran = null;
            Assert.Equal((expected.Count, Reference.Links.Count), (page.Matching, page.Total));
            Assert.Equal(expected.Take(50), page.Rows.Select(row => row.Id));
            Assert.Equal(expected.Count, facets.Facets["kind"].Single().Count);
            Assert.Equal(fromTheSummary, ran.Any(sql => sql.Contains("link_summary", StringComparison.Ordinal)));
            Assert.Equal(!fromTheSummary, ran.Any(sql => sql.Contains("si_link(", StringComparison.Ordinal)));
        }

        string With(string sql)
        {
            var copy = crawl.Copy();
            Change(copy, sql);
            return copy;
        }

        AssertRead(crawl.Path, fromTheSummary: true);

        // Analysed since by an engine whose URL summary this one doesn't read: the links are the same links.
        var otherEngine = With("UPDATE analysis SET analysis_version = 3");
        Assert.Equal(SummaryUnavailableReason.EngineChanged, UrlGridReader.Query(otherEngine, new UrlQuery()).Summary.Reason);
        AssertRead(otherEngine, fromTheSummary: true);

        // The crawl was continued: its links may have changed, so the summary of what they were isn't read.
        AssertRead(crawl.Changed, fromTheSummary: false);

        // Tables that aren't there, or aren't this engine's.
        AssertRead(With("DROP TABLE link_summary"), fromTheSummary: false);
        AssertRead(With("DROP TABLE link_blocks"), fromTheSummary: false);
        AssertRead(With("DELETE FROM link_summary_state"), fromTheSummary: false);
        AssertRead(With("DROP TABLE link_summary_state"), fromTheSummary: false);
        AssertRead(With("ALTER TABLE link_summary RENAME COLUMN links TO how_many"), fromTheSummary: false);
        AssertRead(With("ALTER TABLE link_summary_state RENAME COLUMN crawl_updated_at TO analyzed_at"), fromTheSummary: false);
    }

    /// <summary>
    /// The analysis of a crawl stores its links counted by target and by block, as grouping them would, with how many there
    /// are and their highest id, under the crawl's stamp. Analysing the crawl again leaves them as they are; analysing it
    /// once it has changed counts them afresh.
    /// </summary>
    [Fact]
    public async Task EachAnalysisStoresTheLinksCounted()
    {
        const string GROUPED = """
            SELECT l.to_id, l.kind, l.position, l.flags, l.from_id = l.to_id, l.text IS NOT NULL AND l.text <> '', COALESCE(src.is_nofollow, 0) <> 0, COUNT(*)
            FROM links l LEFT JOIN responses src ON src.url_id = l.from_id GROUP BY 1, 2, 3, 4, 5, 6, 7
            """;
        const string STORED = "SELECT to_id, kind, position, flags, self, has_text, source_nofollow, links FROM link_summary";
        const string STATE = "SELECT s.links, s.max_link_id, s.crawl_updated_at = c.updated_at FROM link_summary_state s, crawl c";
        const string BLOCKED = """
            SELECT l.rowid >> 14, l.kind, l.position, l.flags, l.from_id = l.to_id, l.text IS NOT NULL AND l.text <> '', COALESCE(src.is_nofollow, 0) <> 0, COUNT(*)
            FROM links l LEFT JOIN responses src ON src.url_id = l.from_id GROUP BY 1, 2, 3, 4, 5, 6, 7
            """;
        const string BLOCKS = "SELECT block, kind, position, flags, self, has_text, source_nofollow, links FROM link_blocks";
        Assert.Equal(TestCrawls.Rows(crawl.Path, GROUPED), TestCrawls.Rows(crawl.Path, STORED));
        Assert.Equal(TestCrawls.Rows(crawl.Path, BLOCKED), TestCrawls.Rows(crawl.Path, BLOCKS));
        Assert.Equal(14, CrawlSchema.LINK_BLOCK_SHIFT);
        Assert.Equal([$"{Reference.Links.Count} | {Reference.Links[^1].Id} | 1"], TestCrawls.Rows(crawl.Path, STATE));
        Assert.InRange(TestCrawls.Count(crawl.Path, "SELECT COUNT(*) FROM link_summary"), 1_000, Reference.Links.Count / 4);
        Assert.Contains(Reference.Links, link => link.SourceNofollow);
        Assert.Contains(Reference.Links, link => link.From == link.To);

        // Analysed again, the crawl as it was: the summary stands, and isn't counted again (a count changed by hand stays).
        var again = crawl.Copy();
        Change(again, "UPDATE link_summary SET links = links + 1000 WHERE to_id = (SELECT MIN(to_id) FROM link_summary)");
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(again);
        Assert.NotEqual(TestCrawls.Rows(again, GROUPED), TestCrawls.Rows(again, STORED));
        Assert.True(TestCrawls.Count(again, "SELECT SUM(links) FROM link_summary") >= Reference.Links.Count + 1_000);

        // Once a run of the crawl has stamped it again, the next analysis counts the links afresh.
        Stamp(again);
        Assert.Equal([$"{Reference.Links.Count} | {Reference.Links[^1].Id} | 0"], TestCrawls.Rows(again, STATE));
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(again);
        Assert.Equal(TestCrawls.Rows(again, GROUPED), TestCrawls.Rows(again, STORED));
        Assert.Equal([$"{Reference.Links.Count} | {Reference.Links[^1].Id} | 1"], TestCrawls.Rows(again, STATE));

        // A crawl whose links have gaps in their ids (a page fetched again has its links stored again): analysed, the
        // summary is the links as they now are, and the grid reads them at any place, in either direction, each way.
        var gaps = crawl.Copy();
        Change(gaps, "DELETE FROM links WHERE rowid % 5 = 0 OR rowid BETWEEN 2000 AND 2600");
        Stamp(gaps);
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(gaps);
        Assert.Equal(TestCrawls.Rows(gaps, GROUPED), TestCrawls.Rows(gaps, STORED));
        Assert.Equal(TestCrawls.Rows(gaps, BLOCKED), TestCrawls.Rows(gaps, BLOCKS));
        var links = LinkReference.Load(gaps, GridReference.Load(gaps));
        Assert.Equal([$"{links.Links.Count} | {links.Links[^1].Id} | 1"], TestCrawls.Rows(gaps, STATE));
        Assert.True(links.Links[^1].Id > links.Links.Count);
        var filtered = links.Matching([Names("kind", FilterOperator.In, "hyperlink")]).Select(link => link.Id).ToList();
        foreach (var way in new[] { Way.Chosen, Way.Walked, Way.FromASet, Way.WithoutLinkSummary })
        {
            var cache = Take(way).Cache;
            Assert.Equal(links.Links.Select(link => link.Id), ByOffset(gaps, new UrlQuery(), cache));
            Assert.Equal(links.Links.Select(link => link.Id).Reverse(), ByCursor(gaps, new UrlQuery { Sort = [new UrlSort("id", true)] }, cache));
            Assert.Equal(links.Links.Select(link => link.Id), ByCursor(gaps, new UrlQuery(), cache));
            Assert.Equal(filtered, ByOffset(gaps, new UrlQuery { Filters = [Names("kind", FilterOperator.In, "hyperlink")] }, cache));
            Assert.Equal(links.Links.Skip(4_321).Take(9).Select(link => link.Id), LinkGridReader.Query(gaps, new UrlQuery { Offset = 4_321, Limit = 9 }).Rows.Select(row => row.Id));
        }
    }

    [Fact]
    public void TheColumnsAreALinksOwnAndEachEnds()
    {
        var columns = LinkGridReader.Columns.ToDictionary(column => column.Id);
        Assert.Equal(["id", .. OWN_COLUMNS], LinkGridReader.Columns.Take(OWN_COLUMNS.Length + 1).Select(column => column.Id));
        Assert.Equal(UrlGridReader.Columns.Select(column => "from." + column.Id).Concat(UrlGridReader.Columns.Select(column => "to." + column.Id)),
            LinkGridReader.Columns.Skip(OWN_COLUMNS.Length + 1).Select(column => column.Id));

        // Only the link's id and the target's sortable columns sort; everything the URL grid filters by filters.
        Assert.Equal(["id", .. UrlGridReader.Columns.Where(column => column.Sortable).Select(column => "to." + column.Id)],
            LinkGridReader.Columns.Where(column => column.Sortable).Select(column => column.Id));
        Assert.All(UrlGridReader.Columns, column => Assert.Equal((column.Filterable, column.NeedsAnalysis, column.Type),
            (columns["from." + column.Id].Filterable, columns["to." + column.Id].NeedsAnalysis, columns["to." + column.Id].Type)));
        Assert.Equal(UrlColumnType.Enum, columns["kind"].Type);
        Assert.Contains("metaRefresh", columns["kind"].Values);
        Assert.Equal(["content", "navigation", "header", "footer", "sidebar", "head"], columns["position"].Values);
        Assert.All(OWN_COLUMNS, id => Assert.False(columns[id].NeedsAnalysis));
    }

    [Fact]
    public void AQueryThatCantRunIsRefusedByField()
    {
        void AssertRefused(UrlQuery query, string field, string? saying = null)
        {
            var errors = Assert.Throws<UrlQueryException>(() => LinkGridReader.Query(crawl.Path, query)).Errors;
            Assert.Contains(field, errors.Keys);
            if (saying is not null)
            {
                Assert.Contains(errors[field], message => message.Contains(saying, StringComparison.Ordinal));
            }
        }

        AssertRefused(new UrlQuery { Columns = ["kind", "anchor"] }, "columns[1]", "'anchor'");
        AssertRefused(new UrlQuery { Columns = ["to.detail"] }, "columns[0]");
        AssertRefused(new UrlQuery { Filters = [Boolean("noFollow", true), Text("via", FilterOperator.Eq, "x")] }, "filters[1].column");
        AssertRefused(new UrlQuery { Filters = [Text("kind", FilterOperator.Contains, "hyper")] }, "filters[0].op");
        AssertRefused(new UrlQuery { Filters = [Names("kind", FilterOperator.In, "anchor")] }, "filters[0].values");
        AssertRefused(new UrlQuery { Filters = [Text("to.statusCode", FilterOperator.Contains, "4")] }, "filters[0].op", "'to.statusCode'");
        AssertRefused(new UrlQuery { Filters = [Names("to.check", FilterOperator.In, "no-such-check")] }, "filters[0].values");
        AssertRefused(new UrlQuery { Filters = [Text("from.metaRobots", FilterOperator.Contains, "noindex")] }, "filters[0].column", "'from.metaRobots'");
        AssertRefused(new UrlQuery { Filters = [Text("text", FilterOperator.Regex, "(a")] }, "filters[0].value");
        AssertRefused(new UrlQuery { Sort = [new UrlSort("text")] }, "sort[0].column");
        AssertRefused(new UrlQuery { Sort = [new UrlSort("from.url")] }, "sort[0].column");
        AssertRefused(new UrlQuery { Sort = [new UrlSort("to.title")] }, "sort[0].column");
        AssertRefused(new UrlQuery { Sort = [new UrlSort("to.url"), new UrlSort("id")] }, "sort");
        AssertRefused(new UrlQuery { Limit = 1_001 }, "limit");
        AssertRefused(new UrlQuery { Offset = -1 }, "offset");
        AssertRefused(new UrlQuery { After = "not a cursor" }, "after");
        AssertRefused(new UrlQuery { Search = new string('a', 1_001) }, "search");

        // A cursor belongs to its query, and names a link that's still there with the sort value it had.
        var byUrl = new UrlQuery { Sort = [new UrlSort("to.url")], Limit = 5 };
        var next = LinkGridReader.Query(crawl.Path, byUrl).Next!;
        Assert.InRange(next.Length, 20, 100);
        AssertRefused(new UrlQuery { After = next }, "after", "another query");
        AssertRefused(byUrl with { After = next, Offset = 5 }, "after");
        var gone = crawl.Copy();
        Change(gone, "DELETE FROM links WHERE rowid = $id", ("$id", LinkGridReader.Query(crawl.Path, byUrl).Rows[^1].Id));
        Assert.Contains("after", Assert.Throws<UrlQueryException>(() => LinkGridReader.Query(gone, byUrl with { After = next })).Errors.Keys);

        Assert.Contains("facets[1]", Assert.Throws<UrlQueryException>(() => LinkGridReader.Facets(crawl.Path, new UrlFacetQuery { Facets = ["kind", "status"] })).Errors.Keys);
        Assert.Contains("filters[0].column", Assert.Throws<UrlQueryException>(() => LinkGridReader.Facets(crawl.Path, new UrlFacetQuery { Filters = [Boolean("nofollow", true)] })).Errors.Keys);
    }

    /// <summary>A kind or a position this engine has no name for is unknown: shown as null, counted as null, and found by "is empty".</summary>
    [Fact]
    public async Task StoredValuesThisEngineDoesntKnowAreUnknown()
    {
        var copy = crawl.Copy();
        var (odd, misplaced) = (Reference.Links[100].Id, Reference.Links[200].Id);
        Change(copy, "UPDATE links SET kind = 77 WHERE rowid = $id", ("$id", odd));
        Change(copy, "UPDATE links SET position = 9 WHERE rowid = $id", ("$id", misplaced));
        foreach (var analysed in new[] { false, true })
        {
            // Read from the links, then, analysed again, through a link summary that holds them as they are.
            GridReadContext.ReadWithoutLinkSummary = !analysed;
            if (analysed)
            {
                Stamp(copy);
                await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(copy);
            }

            var unknownKind = LinkGridReader.Query(copy, new UrlQuery { Columns = ["kind", "position", "followed"], Filters = [new UrlFilter("kind", FilterOperator.IsEmpty)] });
            Assert.Equal([odd], unknownKind.Rows.Select(row => row.Id));
            Assert.Equal([odd, null, Reference.Value(Reference.ById[odd], "position"), null], unknownKind.Rows[0].Values);
            Assert.Equal([misplaced], LinkGridReader.Query(copy, new UrlQuery { Filters = [new UrlFilter("position", FilterOperator.IsEmpty)] }).Rows.Select(row => row.Id));
            Assert.Equal(Reference.Links.Count - 1, LinkGridReader.Query(copy, new UrlQuery { Filters = [new UrlFilter("kind", FilterOperator.IsNotEmpty)], Limit = 1 }).Matching);
            Assert.Contains(odd, ByOffset(copy, new UrlQuery { Filters = [Names("kind", FilterOperator.NotIn, "hyperlink")] }, new ReadCache()));

            var facets = LinkGridReader.Facets(copy, new UrlFacetQuery());
            Assert.Equal((null, 1L, new UrlFilter("kind", FilterOperator.IsEmpty)), (facets.Facets["kind"][^1].Value, facets.Facets["kind"][^1].Count, facets.Facets["kind"][^1].Filter));
            Assert.Equal((null, 1L), (facets.Facets["position"][^1].Value, facets.Facets["position"][^1].Count));
            Assert.Equal(Reference.Links.Count, facets.Facets["follow"].Sum(bucket => bucket.Count));
        }
    }

    [Fact]
    public void ASetOfLinkIdsGivesAnyPlaceAmongThem()
    {
        var random = new Random(7);
        foreach (var (largest, links) in new[] { (50_000L, 50_000L), (300L, 300L), (5_000_000_000L, 10_000L), (63L, 64L), (0L, 0L) })
        {
            List<long> ids = [.. Enumerable.Range(0, 4_000).Select(_ => random.NextInt64(1, largest + 1)).Where(id => id <= largest).Distinct().Order()];
            var set = new LinkIdSet(largest, links);
            foreach (var id in ids.OrderBy(_ => random.Next()))
            {
                set.Add(id);
            }

            set.Seal();
            Assert.Equal(ids.Count, set.Count);
            Assert.Equal(ids, set.Range(0, ids.Count + 10));
            foreach (var offset in new[] { 0, 1, 63, 64, 65, ids.Count / 2, ids.Count - 3, ids.Count - 1, ids.Count })
            {
                Assert.Equal(ids.Skip(offset).Take(100), set.Range(offset, 100));
            }

            foreach (var id in ids.Take(50).Concat([0, 1, largest, largest + 1, largest / 2]).Concat(ids.TakeLast(50)))
            {
                Assert.Equal(ids.Count(other => other <= id), set.Rank(id));
            }

            Assert.Equal(ids.AsEnumerable().Reverse().Take(5).Reverse(), new LinkIdSet([.. ids.AsEnumerable().Reverse()]).Range(Math.Max(0, ids.Count - 5), 5));
        }
    }

    private (string Path, ReadCache Cache) Take(Way way)
    {
        // Walked and from a set, an end's URLs are tested link by link rather than listed for the links' index, which a
        // crawl this small would otherwise always do.
        (LinkGridReader.DriveCap, LinkGridReader.WalkRows, LinkGridReader.ListedEnds, LinkGridReader.UseBlocks) = way switch
        {
            Way.Walked => (-1, int.MaxValue, 0, false),
            Way.FromASet => (-1, -1, 0, false),
            _ => (_driveCap, _walkRows, _listedEnds, true)
        };
        GridReadContext.ReadWithoutLinkSummary = way == Way.WithoutLinkSummary;
        return (way == Way.WithoutUrlSummary ? crawl.Changed : crawl.Path, new ReadCache());
    }

    private int UrlId(string path)
    {
        var url = crawl.Server.BaseUri.GetLeftPart(UriPartial.Authority) + path;
        return crawl.Reference.Rows.Single(row => (string?)row.Value["url"] == url).Key;
    }

    private static List<long> ByOffset(string path, UrlQuery query, ReadCache cache)
    {
        List<long> ids = [];
        for (var offset = 0; ; offset += PAGE)
        {
            var page = LinkGridReader.Query(path, query with { Offset = offset, Limit = PAGE, Columns = ["kind"] }, cache);
            ids.AddRange(page.Rows.Select(row => row.Id));
            Assert.Equal(offset, page.Offset);
            if (page.Next is null)
            {
                Assert.Equal(page.Matching, ids.Count);
                return ids;
            }
        }
    }

    private static List<long> ByCursor(string path, UrlQuery query, ReadCache cache, int limit = PAGE)
    {
        List<long> ids = [];
        string? after = null;
        do
        {
            var page = LinkGridReader.Query(path, query with { After = after, Limit = limit, Columns = ["kind"] }, cache);
            ids.AddRange(page.Rows.Select(row => row.Id));
            Assert.Equal(after is null ? 0 : null, page.Offset);
            after = page.Next;
        }
        while (after is not null);

        return ids;
    }

    private static void AssertBuckets(IEnumerable<IGrouping<object?, LinkReference.Link>> expected, IReadOnlyList<FacetBucket> actual) =>
        Assert.Equal(
            expected.Select(group => (Value: group.Key?.ToString(), Count: group.LongCount())).OrderBy(bucket => bucket.Value, StringComparer.Ordinal),
            actual.Select(bucket => (Value: bucket.Value?.ToString(), bucket.Count)).OrderBy(bucket => bucket.Value, StringComparer.Ordinal));

    /// <summary>Values compare as what they are: a number as a number, whatever it's held in.</summary>
    private static bool Same(object? expected, object? actual) => (expected, actual) switch
    {
        (null, null) => true,
        (null, _) or (_, null) => false,
        (string a, string b) => a == b,
        (bool a, bool b) => a == b,
        (string or bool, _) or (_, string or bool) => false,
        _ => Convert.ToInt64(expected, System.Globalization.CultureInfo.InvariantCulture) == Convert.ToInt64(actual, System.Globalization.CultureInfo.InvariantCulture)
    };
}
