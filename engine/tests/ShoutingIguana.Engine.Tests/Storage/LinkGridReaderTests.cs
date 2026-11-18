using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using static ShoutingIguana.Engine.Tests.Support.GridQueries;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>
/// The links grid against the reference (<see cref="LinkReference"/>): every link a row, each filter's links and count,
/// every page by offset and by cursor in either direction, a sort by the target, the facets, and the same answers
/// whichever way a page is read: through the link summary (walking the links, through a rare filter's targets, on the
/// links' index by target, from a set of ids), without it, and without a URL summary.
/// </summary>
[Collection(GridCrawlCollection.NAME)]
public sealed class LinkGridReaderTests(GridCrawl crawl) : IDisposable
{
    private const int PAGE = 1_000;

    private static readonly string[] OWN_COLUMNS = ["kind", "text", "position", "noFollow", "sponsored", "ugc", "imageLink", "selfLink", "followed", "fragment"];

    private readonly int _driveCap = LinkGridReader.DriveCap;
    private readonly int _walkRows = LinkGridReader.WalkRows;
    private readonly int _listedEnds = LinkGridReader.ListedEnds;
    private readonly int _driveRows = LinkGridReader.DriveRows;
    private readonly int _placeSlack = LinkGridReader.PlaceSlack;
    private readonly TimeSpan _slowWalk = LinkGridReader.SlowWalk;
    private LinkReference? _reference;

    private LinkReference Reference => _reference ??= LinkReference.Load(crawl.Path, crawl.Reference);

    public void Dispose()
    {
        (LinkGridReader.DriveCap, LinkGridReader.WalkRows, LinkGridReader.ListedEnds, LinkGridReader.UseBlocks, LinkGridReader.Ran) = (_driveCap, _walkRows, _listedEnds, true, null);
        (LinkGridReader.DriveRows, LinkGridReader.PlaceSlack, LinkGridReader.SlowWalk) = (_driveRows, _placeSlack, _slowWalk);
        GridReadContext.ReadWithoutLinkSummary = false;
    }

    /// <summary>
    /// How a page is read: as the reader chooses (from the links counted by block, or through a filter's targets), by walking
    /// the links, from a set of ids, on the links' index by target as a large crawl's heavily linked targets are, from a
    /// set of ids read through every target, and without either summary.
    /// </summary>
    public enum Way
    {
        Chosen,
        Walked,
        FromASet,
        OnTheTargetsIndex,
        ThroughEveryTarget,
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
        ["anything but followed"] = ([NotBoolean("followed", true)], null),
        ["anything but nofollow"] = ([NotBoolean("noFollow", true)], null),
        ["to another page"] = ([NotBoolean("selfLink", true)], null),
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
        ["to the site"] = ([Boolean("to.isInternal", true)], null),
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
        ["nothing"] = ([Names("kind", FilterOperator.In, "icon"), Boolean("noFollow", true)], null),
        ["counted by a check"] = ([Names("check", FilterOperator.In, "broken-internal-links")], null),
        ["hyperlinks counted by the broken link checks"] =
            ([Names("kind", FilterOperator.In, "hyperlink"), Names("check", FilterOperator.In, "broken-internal-links", "broken-external-links")], null),
        ["counted by a check, in the content"] = ([Names("check", FilterOperator.In, "links-to-redirects"), Names("position", FilterOperator.In, "content")], null),
        ["counted by a check, searched"] = ([Names("check", FilterOperator.In, "broken-internal-links", "empty-anchor-text")], "/p/")
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
    [InlineData(Way.OnTheTargetsIndex)]
    [InlineData(Way.ThroughEveryTarget)]
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
        Assert.NotEmpty(Reference.Matching(FilterSets()["counted by a check, in the content"].Filters));
        Assert.Empty(Reference.Matching(FilterSets()["nothing"].Filters));
    }

    /// <summary>
    /// A filter by a check (<c>check</c>) holds the links the check counts: each link from a page it flagged to a URL that
    /// page's issue names. With hyperlinks alone, as the Links screen's "Broken links" and "Links to redirects" ask, those
    /// are exactly the links the link checks count, and the pages they're on exactly the pages those checks flagged: no
    /// redirect hop is the page a link is on, a working target is never in it, and a broken one on another site only when
    /// the check counts it.
    /// </summary>
    [Theory]
    [InlineData("broken-internal-links", "broken-external-links")]
    [InlineData("links-to-redirects")]
    public void AFilterByLinkChecksHoldsTheLinksTheyCount(params string[] checks)
    {
        var listed = string.Join(", ", checks.Select(check => $"'{check}'"));
        var expected = TestCrawls.Rows(crawl.Path, $"""
            SELECT DISTINCT l.rowid, l.from_id FROM links l
            JOIN issue_targets t ON t.url_id = l.from_id AND t.related_url_id = l.to_id
            WHERE t.issue IN ({listed}) AND l.kind = {(int)LinkKind.Hyperlink}
            """);
        var flagged = TestCrawls.Rows(crawl.Path, $"SELECT DISTINCT url_id FROM issues WHERE issue IN ({listed})");
        Assert.NotEmpty(flagged);

        var query = new UrlQuery { Filters = [Names("kind", FilterOperator.In, "hyperlink"), Names("check", FilterOperator.In, checks)], Columns = ["from.id"], Limit = PAGE };
        var page = LinkGridReader.Query(crawl.Path, query);
        Assert.Equal(expected.Count, page.Matching);
        var rows = page.Rows.Select(row => string.Create(CultureInfo.InvariantCulture, $"{row.Id} | {row.Values[1]}")).Order(StringComparer.Ordinal);
        Assert.Equal(expected, rows);
        Assert.Equal(flagged, page.Rows.Select(row => Convert.ToString(row.Values[1], CultureInfo.InvariantCulture)!).Distinct().Order(StringComparer.Ordinal));
    }

    /// <summary>A filter by a check reads what the analysis stored of each issue's URLs: a crawl without them is refused, as a column the analysis fills is.</summary>
    [Fact]
    public void AFilterByACheckNeedsTheIssuesURLs()
    {
        var copy = crawl.Copy();
        Change(copy, "DROP TABLE issue_targets");
        var refused = Assert.Throws<NeedsAnalysisException>(() =>
            LinkGridReader.Query(copy, new UrlQuery { Filters = [Names("check", FilterOperator.In, "broken-internal-links")] }));
        Assert.Equal("check", Assert.Single(refused.Columns));
    }

    /// <summary>
    /// Each way of reading a page is taken where it should be: a filter on what a link is from the blocks that hold the page,
    /// a filter on the target through its targets when they have few links, on the links' index by target when they have
    /// many, a page near an end by walking, a page in the middle from a set of ids read once; and the links themselves
    /// for the count when a filter tests what the summary doesn't hold. No way reads, page after page, the links before
    /// the page.
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

        // And a page in its middle. Most links pass this filter, so its links are counted by block as every link but the
        // others, which lead to the other targets and are read through those, once, on the index by target: the page is
        // read from the blocks that hold it, and so are the next pages, which read nothing else. No link is read for
        // every matching one, and no set of ids is kept.
        cache = new ReadCache();
        ran.Clear();
        Assert.Equal(Reference.Matching(internalLinks.Filters).Skip(matching / 2).Take(20).Select(link => link.Id),
            LinkGridReader.Query(crawl.Path, internalLinks with { Offset = matching / 2 }, cache).Rows.Select(row => row.Id));
        Assert.Equal("SELECT l.rowid FROM links l INDEXED BY ix_links_to WHERE l.to_id IN (SELECT value FROM json_each($others))",
            Assert.Single(ran, sql => sql.Contains("FROM links", StringComparison.Ordinal) && !Blocks(sql)));
        Assert.Contains(ran, Blocks);
        Assert.DoesNotContain(ran, sql => Scanned(sql) || sql.Contains("si_link_id(", StringComparison.Ordinal));
        ran.Clear();
        LinkGridReader.Query(crawl.Path, internalLinks with { Offset = matching / 2 - 500 }, cache);
        Assert.Equal(Enumerable.Reverse(Reference.Matching(internalLinks.Filters)).Skip(matching / 2 + 300).Take(20).Select(link => link.Id),
            LinkGridReader.Query(crawl.Path, internalLinks with { Offset = matching / 2 + 300, Sort = [new UrlSort("id", true)] }, cache).Rows.Select(row => row.Id));
        Assert.NotEmpty(ran);
        Assert.All(ran, sql => Assert.True(Blocks(sql), sql));

        // The hyperlinks among them are most hyperlinks too: every hyperlink, which the blocks count, but those to the
        // other targets, an entry each on the index by target and kind.
        var hyperlinksToTheSite = internalLinks with { Filters = [.. internalLinks.Filters, Names("kind", FilterOperator.In, "hyperlink")] };
        var internalHyperlinks = Reference.Matching(hyperlinksToTheSite.Filters).Select(link => link.Id).ToList();
        Assert.True(internalHyperlinks.Count > Reference.Links.Count / 2);
        cache = new ReadCache();
        ran.Clear();
        Assert.Equal(internalHyperlinks.Skip(internalHyperlinks.Count / 2).Take(20), LinkGridReader.Query(crawl.Path, hyperlinksToTheSite with { Offset = internalHyperlinks.Count / 2 }, cache).Rows.Select(row => row.Id));
        var others = Assert.Single(ran, sql => sql.Contains("FROM links", StringComparison.Ordinal) && !Blocks(sql));
        Assert.StartsWith("SELECT l.rowid FROM links l INDEXED BY ix_links_to_kind WHERE l.to_id IN (SELECT value FROM json_each($others)) AND (CASE WHEN l.kind", others, StringComparison.Ordinal);
        Assert.DoesNotContain(ran, sql => Scanned(sql) || sql.Contains("si_link_id(", StringComparison.Ordinal));

        // A filter the fewer links pass, with too many targets to list: its ids are read once through its own targets,
        // however many, as the index by target holds all the filter asks; with a kind of link too, on the index by kind
        // (the scripts to the site: the fewer of the scripts, as most are another site's... or, where most of a kind
        // pass, as the images to the site, from the blocks again, but for the other images).
        foreach (var filters in new[] { new[] { Boolean("to.isInternal", false) }, [Boolean("to.isInternal", false), Names("kind", FilterOperator.In, "hyperlink")], [Boolean("to.isInternal", true), Names("kind", FilterOperator.In, "image")] })
        {
            (LinkGridReader.DriveCap, LinkGridReader.WalkRows, LinkGridReader.DriveRows) = (0, -1, -1);
            var fewer = Reference.Matching(filters).Select(link => link.Id).ToList();
            Assert.InRange(fewer.Count, 10, Reference.Links.Count / 2);
            var ofItsKind = filters.Length == 1 ? Reference.Links.Count : Reference.Matching([filters[1]]).Count;
            cache = new ReadCache();
            ran.Clear();
            Assert.Equal(fewer.Skip(fewer.Count / 2).Take(20), LinkGridReader.Query(crawl.Path, new UrlQuery { Filters = filters, Offset = fewer.Count / 2, Limit = 20 }, cache).Rows.Select(row => row.Id));
            if (fewer.Count * 2 > ofItsKind)
            {
                // The others, if there are any (every image here may be the site's own), on the index by target and kind.
                Assert.All(ran.Where(sql => sql.Contains("FROM links", StringComparison.Ordinal) && !Blocks(sql)), sql => Assert.StartsWith(
                    "SELECT l.rowid FROM links l INDEXED BY ix_links_to_kind WHERE l.to_id IN (SELECT value FROM json_each($others)) AND (CASE WHEN l.kind", sql, StringComparison.Ordinal));
                Assert.Contains(ran, Blocks);
                ran.Clear();
                LinkGridReader.Query(crawl.Path, new UrlQuery { Filters = filters, Offset = 20, Limit = 20 }, cache);
                Assert.All(ran, sql => Assert.True(Blocks(sql), sql));
            }
            else
            {
                Assert.Equal($"SELECT COUNT(si_link_id(l.rowid)) FROM links l INDEXED BY {(filters.Length == 1 ? "ix_links_to" : "ix_links_to_kind")} WHERE l.to_id IN (SELECT value FROM json_each($driven)){(filters.Length == 1 ? string.Empty : " AND l.kind = 0")}",
                    Assert.Single(ran, sql => sql.Contains("FROM links", StringComparison.Ordinal)));
                ran.Clear();
                LinkGridReader.Query(crawl.Path, new UrlQuery { Filters = filters, Offset = 20, Limit = 20 }, cache);
                Assert.Empty(ran);
            }

            (LinkGridReader.DriveCap, LinkGridReader.WalkRows, LinkGridReader.DriveRows) = (50, 2_000, _driveRows);
        }

        Assert.True(Reference.Matching([Boolean("to.isInternal", true), Names("kind", FilterOperator.In, "image")]).Count * 2 > Reference.Matching([Names("kind", FilterOperator.In, "image")]).Count);

        // Targets with many links between them, as a large crawl's menu has (here: no links are few): a page anywhere is
        // found on the links' index by target, by counting their links between ids, and read there. Nothing is kept, no
        // link is read to be passed over, and a link's kind is on the index too.
        (LinkGridReader.DriveCap, LinkGridReader.WalkRows, LinkGridReader.ListedEnds, LinkGridReader.DriveRows) = (_driveCap, _walkRows, _listedEnds, -1);
        foreach (var (query, index) in new[] { (internalLinks, "ix_links_to"), (internalLinks with { Filters = [.. internalLinks.Filters, Names("kind", FilterOperator.In, "hyperlink")] }, "ix_links_to_kind") })
        {
            bool OnTheIndex(string sql) => sql.Contains(
                $"FROM links l INDEXED BY {index} WHERE l.to_id IN (SELECT value FROM json_each($targets)){(index == "ix_links_to" ? string.Empty : " AND l.kind = 0")} AND l.rowid > $low AND l.rowid <= $high", StringComparison.Ordinal);
            var expected = Reference.Matching(query.Filters).Select(link => link.Id).ToList();
            cache = new ReadCache();
            LinkGridReader.Query(crawl.Path, query with { Limit = 1 }, cache);
            foreach (var offset in new[] { 0, 20, expected.Count / 3, expected.Count / 2, expected.Count - 20 })
            {
                ran.Clear();
                var page = LinkGridReader.Query(crawl.Path, query with { Offset = offset }, cache);
                Assert.Equal(expected.Skip(offset).Take(20), page.Rows.Select(row => row.Id));
                Assert.All(ran, sql => Assert.True(OnTheIndex(sql), sql));
                Assert.InRange(ran.Count, 1, 12);
                Assert.Equal(1, ran.Count(sql => sql.StartsWith("SELECT l.rowid", StringComparison.Ordinal)));

                // And the page after it, by its cursor.
                if (page.Next is not null)
                {
                    ran.Clear();
                    Assert.Equal(expected.Skip(offset + 20).Take(20), LinkGridReader.Query(crawl.Path, query with { After = page.Next }, cache).Rows.Select(row => row.Id));
                    Assert.All(ran, sql => Assert.True(OnTheIndex(sql) && sql.StartsWith("SELECT l.rowid", StringComparison.Ordinal), sql));
                    Assert.NotEmpty(ran);
                }
            }
        }

        // One target named outright: its links are in crawl order on the index by target, which a walk follows at any
        // depth, whatever else is asked of them.
        var toHome = new UrlQuery { Filters = [Number("to.id", FilterOperator.Eq, UrlId("/")), Names("position", FilterOperator.In, "navigation", "header")], Limit = 20 };
        var home = Reference.Matching(toHome.Filters).Select(link => link.Id).ToList();
        Assert.True(home.Count > 100);
        ran.Clear();
        Assert.Equal(home.Skip(home.Count / 2).Take(20), LinkGridReader.Query(crawl.Path, toHome with { Offset = home.Count / 2 }).Rows.Select(row => row.Id));
        var followed = Assert.Single(ran, sql => sql.Contains("ORDER BY l.rowid", StringComparison.Ordinal));
        Assert.Contains("FROM links l WHERE", followed, StringComparison.Ordinal);

        // Several named, with a condition their index doesn't hold: their links are in no order there, so no page is read
        // by walking them. Deep in, where a walk of the links would pass many, the matching ids are read once, in one pass
        // over the links in order, and kept.
        LinkGridReader.WalkRows = 100;
        var toSome = new UrlQuery { Filters = [Numbers("to.id", FilterOperator.In, UrlId("/"), UrlId("/p/3")), Names("position", FilterOperator.In, "navigation", "header")], Limit = 20 };
        var some = Reference.Matching(toSome.Filters).Select(link => link.Id).ToList();
        Assert.True(some.Count >= home.Count);
        cache = new ReadCache();
        ran.Clear();
        Assert.Equal(some.Skip(some.Count / 2).Take(20), LinkGridReader.Query(crawl.Path, toSome with { Offset = some.Count / 2 }, cache).Rows.Select(row => row.Id));
        Assert.StartsWith("SELECT COUNT(si_link_id(l.rowid)) FROM links l NOT INDEXED WHERE", Assert.Single(ran, sql => sql.Contains("FROM links", StringComparison.Ordinal)), StringComparison.Ordinal);
        ran.Clear();
        Assert.Equal(some.Skip(some.Count / 2 + 20).Take(20), LinkGridReader.Query(crawl.Path, toSome with { Offset = some.Count / 2 + 20 }, cache).Rows.Select(row => row.Id));
        Assert.Empty(ran);

        // Near an end, they're walked: in the links' own order, not on the targets' index, which would give them a target at a time.
        LinkGridReader.WalkRows = _walkRows;
        ran.Clear();
        Assert.Equal(some.Take(20), LinkGridReader.Query(crawl.Path, toSome).Rows.Select(row => row.Id));
        Assert.Contains("FROM links l NOT INDEXED WHERE", Assert.Single(ran, sql => sql.Contains("ORDER BY l.rowid", StringComparison.Ordinal)), StringComparison.Ordinal);

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
    public async Task ALinkSummaryIsReadOnlyWhileItCountsTheCrawlAsItStands()
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

        // A summary that doesn't say how large its blocks are, or says what no build would: passed over, and counted again
        // by the next analysis, though the crawl hasn't changed.
        AssertRead(With("UPDATE link_summary_state SET block_shift = 0"), fromTheSummary: false);
        AssertRead(With("UPDATE link_summary_state SET block_shift = 64"), fromTheSummary: false);
        var withoutItsSize = With("ALTER TABLE link_summary_state DROP COLUMN block_shift");
        AssertRead(withoutItsSize, fromTheSummary: false);
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(withoutItsSize);
        AssertRead(withoutItsSize, fromTheSummary: true);

        // Numbers that can't be true of the file: a highest id that isn't the links', more links than there are ids for or
        // than the file could hold, none where there are some. Passed over, never believed: the links are counted.
        foreach (var untrue in new[]
                 {
                     "max_link_id = max_link_id + 1", "max_link_id = max_link_id - 1", "links = max_link_id + 1", "links = 0", "links = -5",
                     "links = 1073741824, max_link_id = 1073741824", "links = 9223372036854775807"
                 })
        {
            AssertRead(With($"UPDATE link_summary_state SET {untrue}"), fromTheSummary: false);
        }

        // A summary counted another way (an earlier or a later version of the counting), or one that doesn't say which,
        // as an engine from before the version built it: passed over too, with whatever it counted, and counted again by
        // the next analysis, as this engine counts.
        const string VERSION = "SELECT version FROM link_summary_state";
        Assert.Equal([$"{CrawlSchema.LINK_SUMMARY_VERSION}"], TestCrawls.Rows(crawl.Path, VERSION));
        foreach (var other in new[]
                 {
                     $"UPDATE link_summary_state SET version = {CrawlSchema.LINK_SUMMARY_VERSION + 1}; UPDATE link_summary SET links = links + 7",
                     "UPDATE link_summary_state SET version = 0; UPDATE link_blocks SET links = links + 7",
                     "ALTER TABLE link_summary_state DROP COLUMN version; UPDATE link_summary SET links = links + 7"
                 })
        {
            var counted = With(other);
            AssertRead(counted, fromTheSummary: false);
            await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(counted);
            AssertRead(counted, fromTheSummary: true);
            Assert.Equal([$"{CrawlSchema.LINK_SUMMARY_VERSION}"], TestCrawls.Rows(counted, VERSION));
            Assert.Equal(TestCrawls.Rows(crawl.Path, "SELECT * FROM link_summary"), TestCrawls.Rows(counted, "SELECT * FROM link_summary"));
            Assert.Equal(TestCrawls.Rows(crawl.Path, "SELECT * FROM link_blocks"), TestCrawls.Rows(counted, "SELECT * FROM link_blocks"));
        }
    }

    /// <summary>
    /// A large crawl's links fill hundreds of blocks, this crawl's one. Counted here by blocks of 16 link ids, with ids
    /// missing (one in seven, and a run that empties whole blocks), a filter on what a link is still reads each page from
    /// the blocks that hold it: the reference's links at every place, by offset and by cursor, in either direction, in
    /// pages smaller and larger than a block, whether the filter's links are in every block or in few. The size of a
    /// block is the file's own: it's read as it was built, whatever size this engine builds by.
    /// </summary>
    [Fact]
    public async Task APageIsReadFromItsBlocksHoweverManyTheLinksFill()
    {
        const int SHIFT = 4;
        const string COUNTED = "SELECT c.block, SUM(c.links) FROM link_blocks c";
        var copy = crawl.Copy();
        Change(copy, "DELETE FROM links WHERE rowid % 7 = 0 OR rowid BETWEEN 3000 AND 3400");
        Stamp(copy);
        LinkSummaryBuilder.BlockShift = SHIFT;
        try
        {
            await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(copy);
        }
        finally
        {
            LinkSummaryBuilder.BlockShift = CrawlSchema.LINK_BLOCK_SHIFT;
        }

        var links = LinkReference.Load(copy, GridReference.Load(copy));
        Assert.Equal([$"{SHIFT}"], TestCrawls.Rows(copy, "SELECT block_shift FROM link_summary_state"));
        Assert.Equal(TestCrawls.Rows(copy, $"SELECT rowid >> {SHIFT}, COUNT(*) FROM links GROUP BY 1"), TestCrawls.Rows(copy, "SELECT block, SUM(links) FROM link_blocks GROUP BY 1"));
        Assert.True(TestCrawls.Count(copy, "SELECT COUNT(DISTINCT block) FROM link_blocks") > 300);
        Assert.Equal(0, TestCrawls.Count(copy, $"SELECT COUNT(*) FROM link_blocks WHERE block BETWEEN (3000 >> {SHIFT}) + 1 AND (3400 >> {SHIFT}) - 1"));

        List<string> ran = [];
        LinkGridReader.Ran = ran.Add;
        var sets = FilterSets();
        foreach (var name in new[] { "hyperlinks", "not hyperlinks or images", "in the footer", "navigation and header", "nofollow", "not followed", "to their own page", "image links", "hyperlinks with text", "nothing" })
        {
            var filters = sets[name].Filters;
            var expected = links.Matching(filters).Select(link => link.Id).ToList();
            var query = new UrlQuery { Filters = filters };
            var descending = query with { Sort = [new UrlSort("id", true)] };
            var cache = new ReadCache();
            ran.Clear();
            Assert.Equal(expected, ByOffset(copy, query, cache));
            Assert.Equal(Enumerable.Reverse(expected), ByOffset(copy, descending, cache));
            Assert.Equal(expected, ByCursor(copy, query, cache, limit: 97));
            Assert.Equal(Enumerable.Reverse(expected), ByCursor(copy, descending, cache, limit: 97));
            foreach (var offset in new[] { 0, 1, 11, 12, 13, expected.Count / 3, expected.Count / 2, expected.Count - 20, expected.Count - 1 }.Where(offset => offset >= 0 && offset < expected.Count))
            {
                Assert.Equal(expected.Skip(offset).Take(5), LinkGridReader.Query(copy, query with { Offset = offset, Limit = 5 }).Rows.Select(row => row.Id));
                Assert.Equal(Enumerable.Reverse(expected).Skip(offset).Take(5), LinkGridReader.Query(copy, descending with { Offset = offset, Limit = 5 }).Rows.Select(row => row.Id));
            }

            // All of it from the blocks: their counts, then only the links of the blocks a page is in.
            Assert.Contains(ran, sql => sql.StartsWith(COUNTED, StringComparison.Ordinal));
            Assert.All(ran, sql => Assert.True(
                sql.StartsWith(COUNTED, StringComparison.Ordinal) || sql.Contains("l.rowid BETWEEN $low AND $high", StringComparison.Ordinal), $"{name}: {sql}"));
        }

        // The filters' links are spread as a large crawl's are: in every block, and in a few of them.
        Assert.InRange(links.Matching(sets["hyperlinks"].Filters).Count, 4_000, int.MaxValue);
        Assert.InRange(links.Matching(sets["nofollow"].Filters).Select(link => link.Id >> SHIFT).Distinct().Count(), 2, 100);
    }

    /// <summary>
    /// A filter on the target that most of the links it could match pass (the links to the site, alone or with what a link
    /// is: its kind, its position, whether it's followed) has its links counted by block as the blocks' own count but for
    /// the others, so a page anywhere is read from the blocks that hold it: in a crawl with link ids missing (one in
    /// seven, and a run of them) and blocks of sixteen, at every offset and from every cursor, in either direction, with
    /// no link read for each that matches and no pass over the links. A filter the fewer of them pass isn't read that way.
    /// </summary>
    [Fact]
    public async Task AFilterMostLinksPassIsReadFromTheBlocksButForTheOthers()
    {
        const int SHIFT = 4;
        var copy = crawl.Copy();
        Change(copy, "DELETE FROM links WHERE rowid % 7 = 0 OR rowid BETWEEN 3000 AND 3400");
        Stamp(copy);
        LinkSummaryBuilder.BlockShift = SHIFT;
        try
        {
            await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(copy);
        }
        finally
        {
            LinkSummaryBuilder.BlockShift = CrawlSchema.LINK_BLOCK_SHIFT;
        }

        var links = LinkReference.Load(copy, GridReference.Load(copy));
        Assert.True(links.Links[^1].Id > links.Links.Count + 300, "The copy's link ids have gaps.");
        List<string> ran = [];
        LinkGridReader.Ran = ran.Add;
        bool EveryLink(string sql) => sql.Contains("si_link(", StringComparison.Ordinal) || sql.Contains("si_link_id(", StringComparison.Ordinal);
        bool Blocks(string sql) => sql.Contains("l.rowid BETWEEN $low AND $high", StringComparison.Ordinal);

        // No targets are few enough to list or to read through, and no page is walked from an end: a page is read from
        // the blocks, or from a set of the matching ids.
        (LinkGridReader.DriveRows, LinkGridReader.DriveCap, LinkGridReader.WalkRows) = (-1, 0, -1);
        (string Name, UrlFilter[] Filters)[] most =
        [
            ("to the site", [Boolean("to.isInternal", true)]),
            ("hyperlinks to the site", [Boolean("to.isInternal", true), Names("kind", FilterOperator.In, "hyperlink")]),
            ("in the menu, to the site", [Boolean("to.isInternal", true), Names("position", FilterOperator.In, "navigation", "header")]),
            ("followed, to the site", [Boolean("followed", true), Boolean("to.isInternal", true)]),
            ("in the content, to URLs that answered", [Names("position", FilterOperator.In, "content"), Names("to.statusClass", FilterOperator.In, "2xx")])
        ];
        foreach (var (name, filters) in most)
        {
            var expected = links.Matching(filters).Select(link => link.Id).ToList();
            var ofItsKind = links.Matching([.. filters.Where(filter => !filter.Column.StartsWith("to.", StringComparison.Ordinal))]).Count;
            Assert.True(expected.Count * 2 > ofItsKind && expected.Count > 500, $"{name}: {expected.Count} of {ofItsKind}");
            var query = new UrlQuery { Filters = filters };
            var descending = query with { Sort = [new UrlSort("id", true)] };
            var cache = new ReadCache();
            ran.Clear();
            Assert.True(expected.SequenceEqual(ByOffset(copy, query, cache, 211)), $"{name}: by offset");
            Assert.True(Enumerable.Reverse(expected).SequenceEqual(ByOffset(copy, descending, cache, 211)), $"{name}: last first, by offset");
            Assert.True(expected.SequenceEqual(ByCursor(copy, query, cache, limit: 97)), $"{name}: by cursor");
            Assert.True(Enumerable.Reverse(expected).SequenceEqual(ByCursor(copy, descending, cache, limit: 97)), $"{name}: last first, by cursor");

            // The others are read once for all those pages, through their targets (the menu's links all lead to the site:
            // there are none), and each page from its blocks.
            Assert.True(ran.Count(Blocks) > 10, name);
            Assert.Equal(expected.Count < ofItsKind ? 1 : 0,
                ran.Count(sql => sql.StartsWith("SELECT l.rowid FROM links l INDEXED BY ix_links_to", StringComparison.Ordinal) && sql.Contains("$others", StringComparison.Ordinal)));
            foreach (var offset in new[] { 0, 1, 15, 16, 17, expected.Count / 3, expected.Count / 2, expected.Count - 20, expected.Count - 1 })
            {
                Assert.Equal(expected.Skip(offset).Take(5), LinkGridReader.Query(copy, query with { Offset = offset, Limit = 5 }).Rows.Select(row => row.Id));
                Assert.Equal(Enumerable.Reverse(expected).Skip(offset).Take(5), LinkGridReader.Query(copy, descending with { Offset = offset, Limit = 5 }).Rows.Select(row => row.Id));
            }

            Assert.DoesNotContain(ran, EveryLink);
        }

        Assert.Contains(most, set => links.Matching(set.Filters).Count < links.Matching([.. set.Filters.Where(filter => !filter.Column.StartsWith("to.", StringComparison.Ordinal))]).Count);

        // The fewer links of their kind: the blocks can't say where they are, and reading the others would read most links.
        UrlFilter[] fewer = [Names("position", FilterOperator.In, "content"), Text("to.url", FilterOperator.Contains, "/p/1")];
        var some = links.Matching(fewer).Select(link => link.Id).ToList();
        Assert.InRange(some.Count, 100, links.Matching([fewer[0]]).Count / 2);
        ran.Clear();
        Assert.Equal(some.Skip(some.Count / 2).Take(5), LinkGridReader.Query(copy, new UrlQuery { Filters = fewer, Offset = some.Count / 2, Limit = 5 }).Rows.Select(row => row.Id));
        Assert.DoesNotContain(ran, Blocks);
        Assert.Contains(ran, EveryLink);
    }

    /// <summary>
    /// The blocks but for the others serve a filter only while the others are few, an index entry being read for each:
    /// up to an eighth of the crawl's links. Either side of that eighth, by one target's links, the rows are the right
    /// rows, and they're read the way each side should be: the others through their targets and the pages from the
    /// blocks, or every link passed over. And where the blocks' counts less the others read don't come to the links the
    /// summary says match (a file whose counts another program changed), the blocks aren't used, so no page is placed
    /// by counts that are wrong.
    /// </summary>
    [Fact]
    public async Task TheOthersAreReadThroughTheirTargetsOnlyUpToAnEighthOfTheLinks()
    {
        const int SHIFT = 4;
        var copy = crawl.Copy();
        Stamp(copy);
        LinkSummaryBuilder.BlockShift = SHIFT;
        try
        {
            await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(copy);
        }
        finally
        {
            LinkSummaryBuilder.BlockShift = CrawlSchema.LINK_BLOCK_SHIFT;
        }

        var links = LinkReference.Load(copy, GridReference.Load(copy));
        var eighth = links.Links.Count / 8;

        // Targets whose links come to an eighth of the links or just under, taking the most linked that still fit; and
        // one more target, which takes them past it.
        List<(int To, int Links)> targets = [.. links.Links.GroupBy(link => link.To).Select(group => (group.Key, group.Count())).OrderByDescending(target => target.Item2)];
        List<int> under = [];
        var others = 0;
        foreach (var (to, count) in targets.Where(target => target.Links > 1))
        {
            if (others + count <= eighth)
            {
                under.Add(to);
                others += count;
            }
        }

        var extra = targets.First(target => !under.Contains(target.To));
        Assert.InRange(others, eighth / 2, eighth);
        Assert.True(others + extra.Links > eighth);
        UrlFilter[] within = [Numbers("to.id", FilterOperator.NotIn, [.. under.Select(id => (double)id)])];
        UrlFilter[] past = [Numbers("to.id", FilterOperator.NotIn, [.. under.Append(extra.To).Select(id => (double)id)])];

        List<string> ran = [];
        LinkGridReader.Ran = ran.Add;
        bool EveryLink(string sql) => sql.Contains("si_link(", StringComparison.Ordinal) || sql.Contains("si_link_id(", StringComparison.Ordinal);
        bool Blocks(string sql) => sql.Contains("l.rowid BETWEEN $low AND $high", StringComparison.Ordinal);
        bool Others(string sql) => sql.StartsWith("SELECT l.rowid FROM links l INDEXED BY ix_links_to", StringComparison.Ordinal) && sql.Contains("$others", StringComparison.Ordinal);
        (LinkGridReader.DriveRows, LinkGridReader.DriveCap, LinkGridReader.WalkRows) = (-1, 0, -1);

        void Reads(string file, UrlFilter[] filters, bool fromBlocks, string name)
        {
            var expected = links.Matching(filters).Select(link => link.Id).ToList();
            Assert.Equal(links.Links.Count - others - (filters == past ? extra.Links : 0), expected.Count);
            var cache = new ReadCache();
            ran.Clear();
            foreach (var offset in new[] { 0, 17, expected.Count / 3, expected.Count / 2, expected.Count - 20, expected.Count - 1 })
            {
                var page = LinkGridReader.Query(file, new UrlQuery { Filters = filters, Offset = offset, Limit = 5 }, cache);
                Assert.True(expected.Skip(offset).Take(5).SequenceEqual(page.Rows.Select(row => row.Id)), $"{name}: the page from {offset}");
            }

            // The others read once, through their targets, and every page from its blocks; or no page from the blocks.
            Assert.True(fromBlocks ? ran.Count(Others) == 1 && ran.Count(Blocks) >= 6 && !ran.Any(EveryLink) : !ran.Any(Blocks),
                $"{name}: {ran.Count(Others)} reads of the others, {ran.Count(Blocks)} of blocks, {ran.Count(EveryLink)} of every link");
        }

        Reads(copy, within, fromBlocks: true, "an eighth of the links to the others");
        Reads(copy, past, fromBlocks: false, "over an eighth of the links to the others");

        // A file whose counts by block another program changed: the first block says three links more than it has, so
        // every page past it would be placed three links early. The others are read (they're still under an eighth),
        // the blocks less the others don't come to the links the summary says match, and the pages are read from the
        // links themselves instead, which are as they were.
        var tampered = crawl.Copy();
        File.Copy(copy, tampered, overwrite: true);
        Change(tampered, """
            UPDATE link_blocks SET links = links + 3
            WHERE (block, kind, position, flags, self, has_text, source_nofollow) = (SELECT block, kind, position, flags, self, has_text, source_nofollow FROM link_blocks ORDER BY block LIMIT 1)
            """);
        var expectedStill = links.Matching(within).Select(link => link.Id).ToList();
        var tamperedCache = new ReadCache();
        ran.Clear();
        foreach (var offset in new[] { expectedStill.Count / 2, expectedStill.Count - 20, expectedStill.Count - 1 })
        {
            var page = LinkGridReader.Query(tampered, new UrlQuery { Filters = within, Offset = offset, Limit = 5 }, tamperedCache);
            Assert.True(expectedStill.Skip(offset).Take(5).SequenceEqual(page.Rows.Select(row => row.Id)), $"Links gone behind the summary's back: the page from {offset}");
        }

        Assert.True(ran.Count(Others) == 1 && !ran.Any(Blocks), $"{ran.Count(Others)} reads of the others, {ran.Count(Blocks)} of blocks, {ran.Count(EveryLink)} of every link");
    }

    /// <summary>
    /// A page of the links to a filter's targets is found on the links' index by target wherever it is: the place is
    /// closed in on to the link, by counting between ids, in a crawl whose ids have gaps (one in five, and a run of them),
    /// for targets with links from every page and for targets whose links are all in one part of the crawl, for one kind
    /// of link too, at every offset and from every cursor, in either direction. And where the targets are too many to list,
    /// their links' ids are read through them, once, on the same index. No link's row is read to find a page either way.
    /// </summary>
    [Fact]
    public async Task APageOfLinksToAFiltersTargetsIsFoundOnTheirIndex()
    {
        var copy = crawl.Copy();
        Change(copy, "DELETE FROM links WHERE rowid % 5 = 0 OR rowid BETWEEN 2000 AND 2600");
        Stamp(copy);
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(copy);
        var links = LinkReference.Load(copy, GridReference.Load(copy));
        List<string> ran = [];
        LinkGridReader.Ran = ran.Add;
        (string Name, UrlFilter[] Filters)[] sets =
        [
            ("to two pages", [Numbers("to.id", FilterOperator.In, UrlId("/"), UrlId("/p/3"))]),
            ("to the site", [Boolean("to.isInternal", true)]),
            ("hyperlinks to the site", [Boolean("to.isInternal", true), Names("kind", FilterOperator.In, "hyperlink")]),
            ("to other sites", [Boolean("to.isInternal", false)]),
            ("to broken URLs", [Names("to.statusClass", FilterOperator.In, "4xx")]),
            ("hyperlinks to pages like", [Text("to.url", FilterOperator.Contains, "/p/1"), Names("kind", FilterOperator.In, "hyperlink")])
        ];
        foreach (var (slack, cap) in new[] { (0, _driveCap), (7, _driveCap), (_placeSlack, _driveCap), (_placeSlack, 0) })
        {
            // No links are few, so nothing is read through its targets and kept unless they're too many to list (a cap of
            // none), where no page is walked either.
            (LinkGridReader.DriveRows, LinkGridReader.PlaceSlack, LinkGridReader.DriveCap, LinkGridReader.WalkRows) = (-1, slack, cap, cap == 0 ? -1 : _walkRows);
            foreach (var (name, filters) in sets)
            {
                var expected = links.Matching(filters).Select(link => link.Id).ToList();
                var query = new UrlQuery { Filters = filters };
                var descending = query with { Sort = [new UrlSort("id", true)] };
                var cache = new ReadCache();
                ran.Clear();
                Assert.True(expected.SequenceEqual(ByOffset(copy, query, cache, 211)), $"{name}, slack {slack}, cap {cap}: by offset");
                Assert.True(Enumerable.Reverse(expected).SequenceEqual(ByOffset(copy, descending, cache, 211)), $"{name}, slack {slack}, cap {cap}: last first, by offset");
                Assert.True(expected.SequenceEqual(ByCursor(copy, query, cache, limit: 97)), $"{name}, slack {slack}, cap {cap}: by cursor");
                Assert.True(Enumerable.Reverse(expected).SequenceEqual(ByCursor(copy, descending, cache, limit: 97)), $"{name}, slack {slack}, cap {cap}: last first, by cursor");
                foreach (var offset in new[] { 0, 1, 2, expected.Count / 3, expected.Count / 2, expected.Count - 6, expected.Count - 1 }.Where(offset => offset >= 0 && offset < expected.Count))
                {
                    Assert.Equal(expected.Skip(offset).Take(5), LinkGridReader.Query(copy, query with { Offset = offset, Limit = 5 }).Rows.Select(row => row.Id));
                    Assert.Equal(Enumerable.Reverse(expected).Skip(offset).Take(5), LinkGridReader.Query(copy, descending with { Offset = offset, Limit = 5 }).Rows.Select(row => row.Id));
                }

                // The link summary's rows for the counts; every read of the links on the index by target, between two ids
                // or, kept, through every target. Where the targets are too many to list and most links pass, though, the
                // links are counted by block but for the others, read through the other targets, and a page is read from
                // its blocks: missing ids and all, with no link read for each that matches. A cursor's page is walked from
                // its link, the next matching links being right there.
                var index = filters.Length == 1 ? "ix_links_to" : "ix_links_to_kind";
                var kind = filters.Length == 1 ? string.Empty : " AND l.kind = 0";
                var fromBlocks = cap == 0 && name is "to the site" or "hyperlinks to the site";
                Assert.All(ran.Where(sql => !sql.Contains("FROM link_summary c", StringComparison.Ordinal)), sql => Assert.True(
                    fromBlocks
                        ? sql.StartsWith("SELECT c.block, SUM(c.links) FROM link_blocks c", StringComparison.Ordinal) ||
                          sql.StartsWith($"SELECT l.rowid FROM links l INDEXED BY {index} WHERE l.to_id IN (SELECT value FROM json_each($others))", StringComparison.Ordinal) ||
                          sql.Contains("l.rowid BETWEEN $low AND $high", StringComparison.Ordinal) || sql.Contains("l.rowid > $after", StringComparison.Ordinal) ||
                          sql.Contains("l.rowid < $after", StringComparison.Ordinal)
                        : sql.Contains($"FROM links l INDEXED BY {index} WHERE l.to_id IN (SELECT value FROM json_each($targets)){kind} AND l.rowid > $low AND l.rowid <= $high", StringComparison.Ordinal) ||
                          (cap == 0 && sql == $"SELECT COUNT(si_link_id(l.rowid)) FROM links l INDEXED BY {index} WHERE l.to_id IN (SELECT value FROM json_each($driven)){kind}"),
                    $"{name}, slack {slack}, cap {cap}: {sql}"));
                Assert.Equal(cap == 0 && !fromBlocks, ran.Any(sql => sql.Contains("si_link_id(", StringComparison.Ordinal)));
                Assert.Equal(fromBlocks, ran.Any(sql => sql.Contains("l.rowid BETWEEN $low AND $high", StringComparison.Ordinal)));
                Assert.True(expected.Count > 5, name);
            }
        }

        // The targets' links are spread as a large crawl's are: most of the links there are, and a few of them.
        Assert.InRange(links.Matching(sets[1].Filters).Count, links.Links.Count * 3 / 4, links.Links.Count);
        Assert.InRange(links.Matching(sets[4].Filters).Count, 6, links.Links.Count / 20);
        Assert.True(links.Links[^1].Id > links.Links.Count + 500);

        // Most links matching, of a kind that isn't the first on the index (here: every hyperlink is an image, and every
        // image a hyperlink), in a crawl with no id missing: they're kept as every link but the rest, which are the links
        // to the other targets, and to these targets the kinds after this one and those before it.
        var swapped = crawl.Copy();
        Change(swapped, "UPDATE links SET kind = 1 - kind WHERE kind IN (0, 1)");
        Stamp(swapped);
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(swapped);
        UrlFilter[] images = [Boolean("to.isInternal", true), Names("kind", FilterOperator.In, "image")];
        var expectedImages = LinkReference.Load(swapped, GridReference.Load(swapped)).Matching(images).Select(link => link.Id).ToList();
        Assert.True(expectedImages.Count > Reference.Links.Count / 2);
        // (Without the links counted by block, which would count these but for the others: this is how a filter most links
        // pass is read when the others are too many for that.)
        (LinkGridReader.DriveRows, LinkGridReader.PlaceSlack, LinkGridReader.DriveCap, LinkGridReader.WalkRows, LinkGridReader.UseBlocks) = (-1, _placeSlack, 0, -1, false);
        var kept = new ReadCache();
        ran.Clear();
        Assert.Equal(expectedImages, ByOffset(swapped, new UrlQuery { Filters = images }, kept, 211));
        Assert.Equal(Enumerable.Reverse(expectedImages), ByCursor(swapped, new UrlQuery { Filters = images, Sort = [new UrlSort("id", true)] }, kept, limit: 97));
        const string OTHERS = "SELECT COUNT(si_link_id(l.rowid)) FROM links l INDEXED BY ix_links_to_kind WHERE l.to_id IN (SELECT value FROM json_each($others)) AND l.kind ";
        Assert.Equal(
            ["SELECT COUNT(si_link_id(l.rowid)) FROM links l INDEXED BY ix_links_to WHERE l.to_id IN (SELECT value FROM json_each($others))", OTHERS + "> 1", OTHERS + "< 1"],
            ran.Where(sql => sql.Contains("FROM links", StringComparison.Ordinal)));
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
        const string STATE = "SELECT s.links, s.max_link_id, s.crawl_updated_at = c.updated_at, s.block_shift, s.version FROM link_summary_state s, crawl c";
        const string BLOCKED = """
            SELECT l.rowid >> 14, l.kind, l.position, l.flags, l.from_id = l.to_id, l.text IS NOT NULL AND l.text <> '', COALESCE(src.is_nofollow, 0) <> 0, COUNT(*)
            FROM links l LEFT JOIN responses src ON src.url_id = l.from_id GROUP BY 1, 2, 3, 4, 5, 6, 7
            """;
        const string BLOCKS = "SELECT block, kind, position, flags, self, has_text, source_nofollow, links FROM link_blocks";
        Assert.Equal(TestCrawls.Rows(crawl.Path, GROUPED), TestCrawls.Rows(crawl.Path, STORED));
        Assert.Equal(TestCrawls.Rows(crawl.Path, BLOCKED), TestCrawls.Rows(crawl.Path, BLOCKS));
        Assert.Equal(14, CrawlSchema.LINK_BLOCK_SHIFT);
        Assert.Equal([$"{Reference.Links.Count} | {Reference.Links[^1].Id} | 1 | 14 | 1"], TestCrawls.Rows(crawl.Path, STATE));
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
        Assert.Equal([$"{Reference.Links.Count} | {Reference.Links[^1].Id} | 0 | 14 | 1"], TestCrawls.Rows(again, STATE));
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(again);
        Assert.Equal(TestCrawls.Rows(again, GROUPED), TestCrawls.Rows(again, STORED));
        Assert.Equal([$"{Reference.Links.Count} | {Reference.Links[^1].Id} | 1 | 14 | 1"], TestCrawls.Rows(again, STATE));

        // A crawl whose links have gaps in their ids (a page fetched again has its links stored again): analysed, the
        // summary is the links as they now are, and the grid reads them at any place, in either direction, each way.
        var gaps = crawl.Copy();
        Change(gaps, "DELETE FROM links WHERE rowid % 5 = 0 OR rowid BETWEEN 2000 AND 2600");
        Stamp(gaps);
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(gaps);
        Assert.Equal(TestCrawls.Rows(gaps, GROUPED), TestCrawls.Rows(gaps, STORED));
        Assert.Equal(TestCrawls.Rows(gaps, BLOCKED), TestCrawls.Rows(gaps, BLOCKS));
        var links = LinkReference.Load(gaps, GridReference.Load(gaps));
        Assert.Equal([$"{links.Links.Count} | {links.Links[^1].Id} | 1 | 14 | 1"], TestCrawls.Rows(gaps, STATE));
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

    /// <summary>
    /// What a links read keeps is sized by what the file holds, not by what it says: a link summary that claims a quarter
    /// of a billion links in a file of a few megabytes, beside one real link with an id of a billion, is passed over, and
    /// a URL whose id is far past the URLs there are doesn't make a set of URL ids a bit an id.
    /// </summary>
    [Fact]
    public void ALinksReadAllocatesByWhatTheFileHolds()
    {
        const long FAR = 1L << 30;
        var claimed = crawl.Copy();
        Change(claimed, $"INSERT INTO links (rowid, from_id, to_id, kind, flags, position) VALUES ({FAR}, 1, 1, 0, 0, 0)");
        Change(claimed, $"UPDATE link_summary_state SET links = {FAR / 4}, max_link_id = {FAR}");
        var far = crawl.Copy();
        Change(far, "INSERT INTO urls (id, url, is_internal, fetch_mode, depth, state, skip_reason, source) VALUES (134217727, 'https://far.example/', 0, 0, 1, 0, 0, 1)");
        Stamp(far);

        // A filter on a link's text keeps the matching ids, and a search the URLs it matches; the facets count by target.
        foreach (var path in new[] { claimed, far })
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var page = LinkGridReader.Query(path, new UrlQuery { Filters = [Text("text", FilterOperator.Contains, "product")], Search = "p", Sort = [new UrlSort("id", true)], Limit = 5 });
            var facets = LinkGridReader.Facets(path, new UrlFacetQuery { Search = "about" });
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal((Reference.Links.Count + (path == claimed ? 1 : 0), 5), (page.Total, page.Rows.Count));
            Assert.InRange(page.Matching, 1_000, Reference.Links.Count);
            Assert.InRange(facets.Matching, 1, Reference.Links.Count);
            Assert.True(allocated < 12_000_000, $"The reads allocated {allocated:N0} bytes.");
        }
    }

    /// <summary>
    /// A walk from the nearer end that took long found its links far from that end (a filter on deep targets has them on
    /// the pages crawled last). It's noted for the filter set: the next page isn't walked, but read from a set of the
    /// matching ids, read once and kept.
    /// </summary>
    [Fact]
    public void AWalkThatTookLongIsntTakenAgain()
    {
        List<string> ran = [];
        LinkGridReader.Ran = ran.Add;
        bool Walked(string sql) => sql.Contains("ORDER BY l.rowid", StringComparison.Ordinal) && !sql.Contains("BETWEEN $low AND $high", StringComparison.Ordinal);

        // A filter on the target and on what a link is that the fewer links of its kind pass: its place can't be told from
        // the links counted by block, nor from the index by target.
        var content = Reference.Matching([Names("position", FilterOperator.In, "content")]).Count;
        var query = new UrlFilter[] { Text("to.url", FilterOperator.Contains, "/p/1"), Number("to.depth", FilterOperator.Gte, 3), Names("to.type", FilterOperator.In, "image") }
            .Select(target => new UrlQuery { Filters = [target, Names("position", FilterOperator.In, "content")], Limit = 20 })
            .First(candidate => Reference.Matching(candidate.Filters).Count is var matches && matches > 200 && matches * 2 <= content);
        var expected = Reference.Matching(query.Filters).Select(link => link.Id).ToList();
        Assert.True(expected.Count > 200);

        // No targets are few enough to read through, and every page is near enough an end to walk to: both pages are walked.
        (LinkGridReader.DriveCap, LinkGridReader.WalkRows, LinkGridReader.DriveRows) = (0, int.MaxValue, -1);
        var cache = new ReadCache();
        foreach (var offset in new[] { 0, 20 })
        {
            ran.Clear();
            Assert.Equal(expected.Skip(offset).Take(20), LinkGridReader.Query(crawl.Path, query with { Offset = offset }, cache).Rows.Select(row => row.Id));
            Assert.Single(ran, Walked);
        }

        // Any walk has taken long: the first is taken and noted, the next page is read from the set, and the one after
        // from the set as kept.
        LinkGridReader.SlowWalk = TimeSpan.Zero;
        cache = new ReadCache();
        ran.Clear();
        Assert.Equal(expected.Take(20), LinkGridReader.Query(crawl.Path, query, cache).Rows.Select(row => row.Id));
        Assert.Single(ran, Walked);
        ran.Clear();
        Assert.Equal(expected.Skip(20).Take(20), LinkGridReader.Query(crawl.Path, query with { Offset = 20 }, cache).Rows.Select(row => row.Id));
        Assert.DoesNotContain(ran, Walked);
        Assert.StartsWith("SELECT COUNT(si_link_id(l.rowid)) FROM links l NOT INDEXED WHERE", Assert.Single(ran, sql => sql.Contains("FROM links", StringComparison.Ordinal)), StringComparison.Ordinal);
        ran.Clear();
        Assert.Equal(expected.Skip(expected.Count - 30).Take(20), LinkGridReader.Query(crawl.Path, query with { Offset = expected.Count - 30 }, cache).Rows.Select(row => row.Id));
        Assert.Empty(ran);

        // The note is the filter set's: another's first page is walked.
        ran.Clear();
        var footer = query with { Filters = [Boolean("to.isInternal", true), Names("position", FilterOperator.In, "footer")] };
        LinkGridReader.Query(crawl.Path, footer, cache);
        Assert.Single(ran, Walked);

        // And where most links of the kind pass (the footer's links all lead to the site), the page after a walk that took
        // long is read from the blocks that hold it: no pass over the links for a set of ids.
        ran.Clear();
        var inFooter = Reference.Matching(footer.Filters).Select(link => link.Id).ToList();
        Assert.Equal(inFooter.Skip(20).Take(20), LinkGridReader.Query(crawl.Path, footer with { Offset = 20 }, cache).Rows.Select(row => row.Id));
        Assert.DoesNotContain(ran, sql => Walked(sql) || sql.Contains("si_link(", StringComparison.Ordinal) || sql.Contains("si_link_id(", StringComparison.Ordinal));
        Assert.Contains(ran, sql => sql.Contains("l.rowid BETWEEN $low AND $high", StringComparison.Ordinal));
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
        AssertRefused(new UrlQuery { Filters = [Names("check", FilterOperator.In, "no-such-check")] }, "filters[0].values");
        AssertRefused(new UrlQuery { Filters = [Names("check", FilterOperator.NotIn, "broken-internal-links")] }, "filters[0].op");
        AssertRefused(new UrlQuery { Filters = [Text("check", FilterOperator.Eq, "broken-internal-links")] }, "filters[0].op");
        AssertRefused(new UrlQuery { Filters = [Names("check", FilterOperator.In, "broken-internal-links") with { Group = new IssueGroupKey(null, 3) }] }, "filters[0].group");
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

    /// <summary>A set turned into every id it didn't hold, from 1 to the largest: the links a filter matches, given the fewer it doesn't.</summary>
    [Fact]
    public void ASetOfLinkIdsBecomesEveryIdItDidntHold()
    {
        var random = new Random(11);
        foreach (var largest in new long[] { 1, 2, 63, 64, 65, 127, 128, 1_000, 4_096, 50_001 })
        {
            foreach (var share in new[] { 0, 1, 3, 50 })
            {
                HashSet<long> held = [.. Enumerable.Range(1, (int)largest).Select(id => (long)id).Where(_ => random.Next(100) < share)];
                var set = new LinkIdSet(largest, largest);
                foreach (var id in held)
                {
                    set.Add(id);
                }

                List<long> rest = [.. Enumerable.Range(1, (int)largest).Select(id => (long)id).Where(id => !held.Contains(id))];
                Assert.True(set.SealAllBut(largest));
                Assert.Equal(rest.Count, set.Count);
                Assert.Equal(rest, set.Range(0, rest.Count + 5));
                Assert.Equal(rest.Skip(rest.Count / 2).Take(7), set.Range(rest.Count / 2, 7));
                Assert.Equal(rest.Count, set.Rank(largest + 100));
                Assert.Equal(rest.Count(id => id <= largest / 2), set.Rank(largest / 2));
            }
        }

        // Ids kept as a list (far apart, a crafted file's) aren't turned, nor a set sized for another crawl.
        Assert.False(new LinkIdSet(5_000_000_000, 10).SealAllBut(5_000_000_000));
        Assert.False(new LinkIdSet(1_000, 1_000).SealAllBut(2_000));
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

            // No targets are few enough to list, and no page is walked: a filter's ids are read through every target.
            Way.ThroughEveryTarget => (0, -1, _listedEnds, true),
            _ => (_driveCap, _walkRows, _listedEnds, true)
        };

        // On the targets' index: no links are few, so none are read through their targets and kept, and a place is closed
        // in on to within a link or two.
        (LinkGridReader.DriveRows, LinkGridReader.PlaceSlack) = way switch
        {
            Way.OnTheTargetsIndex => (-1, 2),
            Way.ThroughEveryTarget => (-1, _placeSlack),
            _ => (_driveRows, _placeSlack)
        };
        GridReadContext.ReadWithoutLinkSummary = way == Way.WithoutLinkSummary;
        return (way == Way.WithoutUrlSummary ? crawl.Changed : crawl.Path, new ReadCache());
    }

    private int UrlId(string path)
    {
        var url = crawl.Server.BaseUri.GetLeftPart(UriPartial.Authority) + path;
        return crawl.Reference.Rows.Single(row => (string?)row.Value["url"] == url).Key;
    }

    private static List<long> ByOffset(string path, UrlQuery query, ReadCache cache, int limit = PAGE)
    {
        List<long> ids = [];
        for (var offset = 0; ; offset += limit)
        {
            var page = LinkGridReader.Query(path, query with { Offset = offset, Limit = limit, Columns = ["kind"] }, cache);
            ids.AddRange(page.Rows.Select(row => row.Id));
            Assert.Equal(offset, page.Offset);
            if (page.Next is null)
            {
                Assert.Equal(page.Matching, ids.Count);
                return ids;
            }

            GridWalks.AssertGoesOn(page.Rows.Count, ids.Count, page.Matching);
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
            if (after is not null)
            {
                GridWalks.AssertGoesOn(page.Rows.Count, ids.Count, page.Matching);
            }
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
