using System.IO.Compression;
using System.Text;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using static ShoutingIguana.Engine.Tests.Support.GridQueries;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>
/// The inspector's links, paged by offset and by cursor against the links table in the order they were found, a page's
/// links to itself left out, each naming the other end with its status; any offset reached directly; and the stored HTML,
/// cut where a character ends past the limit.
/// </summary>
[Collection(GridCrawlCollection.NAME)]
public sealed class UrlLinksAndSourceTests(GridCrawl crawl)
{
    private const int PAGE = 7;

    public static TheoryData<string, LinkDirection, LinkKind[]> Lists() => new()
    {
        { "/", LinkDirection.Inlinks, [] },
        { "/", LinkDirection.Inlinks, [LinkKind.Hyperlink] },
        { "/", LinkDirection.Inlinks, [LinkKind.Hyperlink, LinkKind.Canonical] },
        { "/p/7", LinkDirection.Inlinks, [LinkKind.Canonical, LinkKind.Hyperlink] },
        { "/", LinkDirection.Outlinks, [] },
        { "/p/7", LinkDirection.Inlinks, [] },
        { "/p/7", LinkDirection.Outlinks, [LinkKind.Hyperlink, LinkKind.Image] },
        { "/plants/", LinkDirection.Outlinks, [] },
        { "/plants/fragments", LinkDirection.Outlinks, [LinkKind.Hyperlink] },
        { "/plants/nofollow", LinkDirection.Outlinks, [] },
        { "/img/1.jpg", LinkDirection.Inlinks, [LinkKind.Image] }
    };

    [Theory]
    [MemberData(nameof(Lists))]
    public void LinksPageAlikeByOffsetAndByCursor(string path, LinkDirection direction, LinkKind[] kinds)
    {
        var id = Id(path);
        var expected = Expected(crawl.Path, id, direction, kinds);
        Assert.NotEmpty(expected);
        var cache = new ReadCache();
        Assert.Equal(expected, ByOffset(crawl.Path, id, direction, kinds, cache));
        Assert.Equal(expected, ByCursor(crawl.Path, id, direction, kinds, cache));

        // Any offset, directly: through the index when every kind is listed, from the far end past half way otherwise.
        foreach (var offset in Enumerable.Range(0, expected.Count))
        {
            var page = UrlInspectorReader.ReadLinks(crawl.Path, id, direction, new LinkQuery { Kinds = kinds, Offset = offset, Limit = 3 }, cache)!;
            Assert.Equal(expected.Skip(offset).Take(3), page.Rows);
            Assert.Equal(expected.Count, page.Total);
            Assert.Equal(offset, page.Offset);
            Assert.Equal(offset + 3 < expected.Count, page.Next is not null);
        }

        Assert.Empty(UrlInspectorReader.ReadLinks(crawl.Path, id, direction, new LinkQuery { Kinds = kinds, Offset = expected.Count }, cache)!.Rows);
    }

    /// <summary>
    /// Some kinds of link to a URL are reached at any offset on the analysis's index of the links to a URL by kind alone, each
    /// kind's in crawl order and merged, so skipping to the middle of a nav target's million hyperlinks reads none of them.
    /// Without the index (an earlier engine's analysis), the same rows come from the links themselves.
    /// </summary>
    [Fact]
    public void SomeKindsOfLinkToAUrlAreReadOnTheIndexByKind()
    {
        var home = Id("/");
        LinkKind[] kinds = [LinkKind.Hyperlink, LinkKind.Canonical];
        List<(string Sql, IReadOnlyList<KeyValuePair<string, object>> Parameters)> ran = [];
        UrlInspectorReader.Ran = (sql, parameters) => ran.Add((sql, parameters));
        try
        {
            UrlInspectorReader.ReadLinks(crawl.Path, home, LinkDirection.Inlinks, new LinkQuery { Kinds = kinds, Offset = 5, Limit = 3 });
        }
        finally
        {
            UrlInspectorReader.Ran = null;
        }

        var (last, values) = ran[^1];
        using (var connection = CrawlDatabase.OpenReader(crawl.Path))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "EXPLAIN QUERY PLAN " + last;
            foreach (var (name, value) in values)
            {
                command.Parameters.AddWithValue(name, value);
            }

            using var reader = command.ExecuteReader();
            List<string> plan = [];
            while (reader.Read())
            {
                plan.Add(reader.GetString(3));
            }

            Assert.Contains("MERGE (UNION ALL)", plan);
            Assert.Equal(2, plan.Count(step => step == "SEARCH links USING COVERING INDEX ix_links_to_kind (to_id=? AND kind=?)"));
            Assert.Contains("SEARCH l USING INTEGER PRIMARY KEY (rowid=?)", plan);
        }

        var copy = crawl.Copy();
        Change(copy, "DROP INDEX ix_links_to_kind");
        foreach (var asked in new[] { kinds, [LinkKind.Hyperlink], [LinkKind.Canonical] })
        {
            Assert.Equal(Expected(crawl.Path, home, LinkDirection.Inlinks, asked), ByOffset(copy, home, LinkDirection.Inlinks, asked, new ReadCache()));
        }
    }

    /// <summary>A page's own links to itself (its canonical, a link back to the top) aren't its inlinks or outlinks.</summary>
    [Fact]
    public void APageLinksToItselfAreLeftOut()
    {
        var home = Id("/");
        Assert.True(TestCrawls.Count(crawl.Path, $"SELECT COUNT(*) FROM links WHERE from_id = {home} AND to_id = {home}") > 0);
        var page = UrlInspectorReader.ReadLinks(crawl.Path, home, LinkDirection.Inlinks, new LinkQuery { Limit = 1_000 })!;
        Assert.DoesNotContain(page.Rows, link => link.Id == home);
        Assert.DoesNotContain(UrlInspectorReader.ReadLinks(crawl.Path, home, LinkDirection.Outlinks, new LinkQuery { Limit = 1_000 })!.Rows, link => link.Id == home);
    }

    /// <summary>A hyperlink is followed unless it, or the page it's on, says nofollow.</summary>
    [Fact]
    public void FollowedSaysWhetherSearchEnginesFollowAHyperlink()
    {
        var nofollowPage = UrlInspectorReader.ReadLinks(crawl.Path, Id("/plants/nofollow"), LinkDirection.Outlinks, new LinkQuery { Kinds = [LinkKind.Hyperlink], Limit = 1_000 })!;
        Assert.NotEmpty(nofollowPage.Rows);
        Assert.All(nofollowPage.Rows, link => Assert.False(link.Followed));

        var hidden = UrlInspectorReader.ReadLinks(crawl.Path, Id("/hidden/43"), LinkDirection.Inlinks, new LinkQuery())!;
        Assert.All(hidden.Rows, link => Assert.True(link.Flags.HasFlag(LinkFlags.NoFollow) && link.Followed == false));
        Assert.All(UrlInspectorReader.ReadLinks(crawl.Path, Id("/img/1.jpg"), LinkDirection.Inlinks, new LinkQuery { Kinds = [LinkKind.Image] })!.Rows,
            link => Assert.Null(link.Followed));
    }

    /// <summary>
    /// A link's other end has no status when it was never fetched, and none when it was asked for and gave no HTTP answer:
    /// its outcome tells them apart, with the URL summary and without it.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ALinkSaysWhatBecameOfItsOtherEnd(bool continuedSinceItsAnalysis)
    {
        var path = continuedSinceItsAnalysis ? crawl.Changed : crawl.Path;
        LinkRow To(string sql)
        {
            var ends = TestCrawls.Rows(crawl.Path, $"SELECT l.from_id, l.to_id FROM links l {sql} ORDER BY l.rowid LIMIT 1").Single().Split(" | ").Select(int.Parse).ToList();
            return UrlInspectorReader.ReadLinks(path, ends[0], LinkDirection.Outlinks, new LinkQuery { Limit = 1_000 })!.Rows.First(link => link.Id == ends[1]);
        }

        var refused = To("JOIN responses r ON r.url_id = l.to_id WHERE r.outcome = 5 AND l.from_id <> l.to_id");
        Assert.Equal(((int?)null, (UrlOutcome?)UrlOutcome.RobotsDisallowed, true), (refused.StatusCode, refused.Outcome, refused.Fetched));

        var missing = To("JOIN responses r ON r.url_id = l.to_id WHERE r.status_code = 404 AND l.from_id <> l.to_id");
        Assert.Equal(((int?)404, (UrlOutcome?)UrlOutcome.ClientError, true), (missing.StatusCode, missing.Outcome, missing.Fetched));

        var neverFetched = To("LEFT JOIN responses r ON r.url_id = l.to_id WHERE r.url_id IS NULL AND l.from_id <> l.to_id");
        Assert.Equal(((int?)null, (UrlOutcome?)null, false), (neverFetched.StatusCode, neverFetched.Outcome, neverFetched.Fetched));

        // A value no engine writes reads as unknown, as everywhere.
        var odd = crawl.Copy();
        Change(odd, $"UPDATE responses SET outcome = 77 WHERE url_id = {refused.Id}");
        Assert.Null(UrlInspectorReader.ReadLinks(odd, (int)TestCrawls.Count(crawl.Path, $"SELECT from_id FROM links WHERE to_id = {refused.Id} LIMIT 1"),
            LinkDirection.Outlinks, new LinkQuery { Limit = 1_000 })!.Rows.First(link => link.Id == refused.Id).Outcome);
    }

    [Fact]
    public void WithoutASummaryTheOtherEndReadsTheCrawlsOwnTables()
    {
        var copy = crawl.Copy();
        Stamp(copy);
        foreach (var (path, direction, kinds) in new (string, LinkDirection, LinkKind[])[] { ("/", LinkDirection.Inlinks, [LinkKind.Hyperlink]), ("/plants/", LinkDirection.Outlinks, []) })
        {
            var id = Id(path);
            Assert.Equal(Expected(crawl.Path, id, direction, kinds), ByOffset(copy, id, direction, kinds, new ReadCache()));
        }
    }

    [Fact]
    public void ALinksQueryThatCantRunIsRefused()
    {
        var home = Id("/");
        var cursor = UrlInspectorReader.ReadLinks(crawl.Path, home, LinkDirection.Inlinks, new LinkQuery { Limit = 2 })!.Next!;
        (string Field, LinkQuery Query, LinkDirection Direction)[] refused =
        [
            ("limit", new LinkQuery { Limit = 0 }, LinkDirection.Inlinks),
            ("limit", new LinkQuery { Limit = 1_001 }, LinkDirection.Inlinks),
            ("offset", new LinkQuery { Offset = -1 }, LinkDirection.Inlinks),
            ("after", new LinkQuery { Offset = 5, After = cursor }, LinkDirection.Inlinks),
            ("after", new LinkQuery { After = "not a cursor" }, LinkDirection.Inlinks),
            ("after", new LinkQuery { After = cursor }, LinkDirection.Outlinks),
            ("after", new LinkQuery { After = cursor, Kinds = [LinkKind.Hyperlink] }, LinkDirection.Inlinks),
            ("kinds", new LinkQuery { Kinds = [.. Enum.GetValues<LinkKind>(), LinkKind.Image] }, LinkDirection.Inlinks)
        ];
        foreach (var (field, query, direction) in refused)
        {
            var error = Assert.Throws<UrlQueryException>(() => UrlInspectorReader.ReadLinks(crawl.Path, home, direction, query));
            Assert.Contains(field, error.Errors.Keys);
        }

        // Another URL's list: its cursor names another list.
        Assert.Throws<UrlQueryException>(() => UrlInspectorReader.ReadLinks(crawl.Path, Id("/p/7"), LinkDirection.Inlinks, new LinkQuery { After = cursor }));
        Assert.Null(UrlInspectorReader.ReadLinks(crawl.Path, crawl.Reference.Rows.Keys.Max() + 1, LinkDirection.Inlinks, new LinkQuery()));
    }

    [Fact]
    public void TheSourceIsTheStoredHtml()
    {
        var id = Id("/p/7");
        var source = UrlInspectorReader.ReadSource(crawl.Path, id)!;
        Assert.True(source.Stored);
        Assert.Null(source.Missing);
        Assert.False(source.Truncated);
        Assert.StartsWith("<!DOCTYPE html>", source.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains((string)crawl.Reference.Rows[id]["h1"]!, source.Html);
        Assert.Equal(Encoding.UTF8.GetByteCount(source.Html!), source.Bytes);

        // The crawl stored HTML, but an image has none.
        var image = UrlInspectorReader.ReadSource(crawl.Path, Id("/img/1.jpg"))!;
        Assert.False(image.Stored);
        Assert.Equal(HtmlSourceMissing.NotPage, image.Missing);
        Assert.Null(UrlInspectorReader.ReadSource(crawl.Path, crawl.Reference.Rows.Keys.Max() + 1));
    }

    /// <summary>Past the limit, the HTML is cut, and a character the cut falls inside is left out rather than shown half.</summary>
    [Fact]
    public void SourceOverTheLimitIsCutWhereACharacterEnds()
    {
        var copy = crawl.Copy();
        var id = Id("/p/7");
        var kept = new string('a', UrlInspectorReader.MAX_SOURCE_BYTES - 1);
        var html = kept + "é and the rest";
        using (var packed = new MemoryStream())
        {
            using (var brotli = new BrotliStream(packed, CompressionLevel.Fastest, leaveOpen: true))
            {
                brotli.Write(Encoding.UTF8.GetBytes(html));
            }

            Change(copy, "UPDATE response_html SET html = $html WHERE url_id = $id", ("$html", packed.ToArray()), ("$id", id));
        }

        var source = UrlInspectorReader.ReadSource(copy, id)!;
        Assert.True(source.Truncated);
        Assert.Equal(Encoding.UTF8.GetByteCount(html), source.Bytes);
        Assert.Equal(kept.Length, source.Html!.Length);
        Assert.Equal(kept, source.Html);
    }

    /// <summary>The links of the kinds asked for, in the order they were found, a page's links to itself left out, as the reference sees them.</summary>
    private List<LinkRow> Expected(string path, int id, LinkDirection direction, IReadOnlyCollection<LinkKind> kinds)
    {
        var (near, far) = direction == LinkDirection.Inlinks ? ("to_id", "from_id") : ("from_id", "to_id");
        var ofKinds = kinds.Count == 0 ? string.Empty : $" AND kind IN ({string.Join(", ", kinds.Select(kind => (int)kind))})";
        using var connection = CrawlDatabase.OpenReader(path);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {far}, from_id, kind, text, position, flags, fragment FROM links WHERE {near} = {id} AND {far} <> {id}{ofKinds} ORDER BY rowid";
        using var reader = command.ExecuteReader();
        List<LinkRow> links = [];
        while (reader.Read())
        {
            var other = crawl.Reference.Rows[reader.GetInt32(0)];
            var kind = (LinkKind)reader.GetInt32(2);
            var flags = (LinkFlags)reader.GetInt32(5);
            var pageSaysNofollow = (bool?)crawl.Reference.Rows[reader.GetInt32(1)]["nofollow"] == true;
            links.Add(new LinkRow(
                reader.GetInt32(0),
                (string)other["url"]!,
                kind,
                reader.IsDBNull(3) ? null : reader.GetString(3),
                (LinkPosition)reader.GetInt32(4),
                flags,
                kind == LinkKind.Hyperlink ? !flags.HasFlag(LinkFlags.NoFollow) && !pageSaysNofollow : null,
                reader.IsDBNull(6) ? null : reader.GetString(6),
                (int?)(long?)other["statusCode"],
                (bool?)other["indexable"],
                (bool)other["fetched"]!,
                other["outcome"] is string outcome ? Enum.Parse<UrlOutcome>(outcome, ignoreCase: true) : null));
        }

        return links;
    }

    private static List<LinkRow> ByOffset(string path, int id, LinkDirection direction, LinkKind[] kinds, ReadCache cache)
    {
        List<LinkRow> links = [];
        for (var offset = 0; ; offset += PAGE)
        {
            var page = UrlInspectorReader.ReadLinks(path, id, direction, new LinkQuery { Kinds = kinds, Offset = offset, Limit = PAGE }, cache)!;
            links.AddRange(page.Rows);
            if (page.Next is null)
            {
                Assert.Equal(page.Total, links.Count);
                return links;
            }
        }
    }

    private static List<LinkRow> ByCursor(string path, int id, LinkDirection direction, LinkKind[] kinds, ReadCache cache)
    {
        List<LinkRow> links = [];
        string? after = null;
        do
        {
            var page = UrlInspectorReader.ReadLinks(path, id, direction, new LinkQuery { Kinds = kinds, After = after, Limit = PAGE }, cache)!;
            Assert.Equal(after is null ? 0 : null, page.Offset);
            links.AddRange(page.Rows);
            after = page.Next;
            if (after is not null)
            {
                GridWalks.AssertGoesOn(page.Rows.Count, links.Count, page.Total);
            }
        }
        while (after is not null);

        return links;
    }

    private int Id(string path) => crawl.Reference.Rows.Single(row => (string?)row.Value["url"] == crawl.Server.BaseUri.GetLeftPart(UriPartial.Authority) + path).Key;
}
