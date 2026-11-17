using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using static ShoutingIguana.Engine.Tests.Support.GridQueries;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>
/// What every grid read can count on, whatever it reads: it stops when told to, a regular expression can't hang it, a
/// writer here never waits on it, a cursor stays small and never reads on from a place that has moved, a stored value
/// this engine doesn't know reads as unknown, and a deep page of a sort without an index is the sort's.
/// </summary>
[Collection(GridCrawlCollection.NAME)]
public sealed class GridReadTests(GridCrawl crawl)
{
    private const int PAGE = 13;

    /// <summary>A statement that runs for half a minute or so: counting two billion numbers.</summary>
    private const string FOREVER = "WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM n) SELECT COUNT(*) FROM (SELECT x FROM n LIMIT 2000000000)";

    /// <summary>A hundred thousand texts of 200,000 characters, each matched by <c>$p</c>.</summary>
    private const string MATCHED = "WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM n LIMIT 100000) SELECT COUNT(*) FROM n WHERE si_regex($p, hex(zeroblob(100000)) || x)";

    /// <summary>A pattern that costs about 0.3 ms on such a text, from the first: linear, in a small automaton, and not one .NET finds by vector search.</summary>
    private const string SLOW = "(0|1|2|3|4|5|6|7|8|9)+z";

    [Fact]
    public void CancellingAReadStopsTheStatementItIsRunning()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var clock = Stopwatch.StartNew();
        Assert.ThrowsAny<OperationCanceledException>(() => GridReadContext.Read(crawl.Path, null, cancel.Token, context => context.Scalar(FOREVER, [])));
        Assert.InRange(clock.ElapsedMilliseconds, 150, 2_000);

        // Cancelled before it starts, it never opens the file.
        Assert.ThrowsAny<OperationCanceledException>(() => UrlGridReader.Query(crawl.Path, new UrlQuery(), cancellationToken: cancel.Token));
    }

    [Fact]
    public void CancellingAReadStopsARegularExpressionPartWayThroughItsRows()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var clock = Stopwatch.StartNew();
        Assert.ThrowsAny<OperationCanceledException>(() => GridReadContext.Read(crawl.Path, null, cancel.Token, context => context.Scalar(MATCHED, [new("$p", SLOW)])));
        Assert.InRange(clock.ElapsedMilliseconds, 150, 2_000);
    }

    /// <summary>
    /// Nested counted repeats build an automaton that takes minutes on one URL: the first match that takes longer than
    /// the timeout refuses the pattern, on the filter that has it, for the grid, its facets and a check's rows alike.
    /// </summary>
    [Fact]
    public async Task APatternWhoseMatchesTakeTooLongIsRefusedOnItsFilter()
    {
        UrlFilter[] filters = [Text("title", FilterOperator.Contains, "product"), Text("url", FilterOperator.Regex, "(((.{0,9}){0,9}){0,9}){0,9}z")];
        var check = crawl.Reference.Issues.SelectMany(issue => issue.Value).First().Issue;
        Func<object?>[] reads =
        [
            () => UrlGridReader.Query(crawl.Path, new UrlQuery { Filters = filters }),
            () => UrlGridReader.Query(crawl.Changed, new UrlQuery { Filters = filters }),
            () => UrlGridReader.Facets(crawl.Path, new UrlFacetQuery { Filters = filters }),
            () => IssueGroupReader.ReadRows(crawl.Path, check, new UrlQuery { Filters = filters })
        ];

        foreach (var read in reads)
        {
            var clock = Stopwatch.StartNew();
            var refusing = Task.Run(() => Assert.Throws<UrlQueryException>(read));
            var refused = await refusing.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.InRange(clock.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(5));
            Assert.Equal(["filters[1].value"], refused.Errors.Keys);
            Assert.Equal(ReaderFunctions.TOO_COMPLEX, Assert.Single(refused.Errors["filters[1].value"]));
        }
    }

    /// <summary>Matches that each stay under the timeout still stop once a read's have taken its budget in all.</summary>
    [Fact]
    public void AReadsRegularExpressionsStopOnceTheyHaveTakenItsBudget()
    {
        var before = GridReadContext.RegexBudget;
        GridReadContext.RegexBudget = TimeSpan.FromMilliseconds(300);
        try
        {
            var clock = Stopwatch.StartNew();
            var refused = Assert.Throws<RegexTooComplexException>(() => GridReadContext.Read(crawl.Path, null, default, context => context.Scalar(MATCHED, [new("$p", SLOW)])));
            Assert.Equal(SLOW, refused.Pattern);
            Assert.InRange(clock.Elapsed, TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(5));
        }
        finally
        {
            GridReadContext.RegexBudget = before;
        }
    }

    [Fact]
    public void APatternTooLargeToBuildIsCalledTooComplex()
    {
        Assert.Contains("too complex", ReaderFunctions.CheckPattern("a{100000}"), StringComparison.Ordinal);
        Assert.Contains("too complex", ReaderFunctions.CheckPattern("(a{100}){100}"), StringComparison.Ordinal);
        Assert.Contains("backtracking", ReaderFunctions.CheckPattern(@"(a)\1"), StringComparison.Ordinal);
        Assert.Contains("backtracking", ReaderFunctions.CheckPattern("(?<=a)b"), StringComparison.Ordinal);
        Assert.Null(ReaderFunctions.CheckPattern("x{1000}"));
    }

    /// <summary>
    /// A closed crawl is in rollback-journal mode, and a grid read part-way through it holds its shared lock: a writer
    /// here (an analysis, a continue) stops the read, switches the file to WAL mode at once, and the read runs again.
    /// Closing, the writer does the same to switch it back, so the crawl is a single file again.
    /// </summary>
    [Fact]
    public async Task AWriterHereNeverWaitsOnAGridRead()
    {
        var copy = crawl.Copy();
        Assert.Equal("delete", JournalMode(copy));
        var (opening, attempts) = StartLongRead(copy);
        var clock = Stopwatch.StartNew();
        var writer = CrawlDatabase.OpenWriter(copy, allowNew: false);
        Assert.InRange(clock.ElapsedMilliseconds, 0, 900);
        Assert.Equal(crawl.Reference.Rows.Count, await opening.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(2, attempts());

        var (closing, closingAttempts) = StartLongRead(copy);
        CrawlDatabase.CloseWriter(writer, NullLogger.Instance);
        Assert.Equal(crawl.Reference.Rows.Count, await closing.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(2, closingAttempts());
        Assert.Equal("delete", JournalMode(copy));
    }

    /// <summary>
    /// A cursor carries the row's id and a hash of its sort values, never the values: one after a URL of thousands of
    /// characters (a faceted search's, full of <c>&amp;</c> and <c>+</c>) is as short as any, and reads on.
    /// </summary>
    [Fact]
    public void ACursorAfterALongUrlIsShortAndReadsOn()
    {
        var copy = crawl.Copy();
        Stamp(copy);
        var id = crawl.Reference.Rows.Keys.Max() + 1;
        var url = crawl.Server.BaseUri.AbsoluteUri + "a-faceted-search?" + string.Join("&", Enumerable.Range(0, 600).Select(index => $"f{index}=a+b"));
        Change(copy, "INSERT INTO urls (id, url, is_internal, fetch_mode, depth, state, skip_reason, source) VALUES ($id, $url, 1, 0, 3, 0, 0, 1)",
            ("$id", id), ("$url", url));
        var query = new UrlQuery { Sort = [new UrlSort("url")], Limit = 1 };
        var order = ByOffset(copy, query with { Limit = 1_000 });
        var at = order.IndexOf(id);
        Assert.InRange(at, 0, order.Count - 2);

        var page = UrlGridReader.Query(copy, query with { Offset = at });
        Assert.Equal(id, Assert.Single(page.Rows).Id);
        Assert.InRange(page.Next!.Length, 1, 100);
        Assert.Equal(order.Skip(at + 1).Take(5), UrlGridReader.Query(copy, query with { After = page.Next, Limit = 5 }).Rows.Select(row => row.Id));
    }

    [Fact]
    public void ACursorWhoseRowHasChangedOrGoneIsRefused()
    {
        var copy = crawl.Copy();
        var query = new UrlQuery { Sort = [new UrlSort("depth")], Limit = 5 };
        var page = UrlGridReader.Query(copy, query);
        var last = page.Rows[^1].Id;

        // Read on from where the row was, it would skip or repeat rows: refused, with why.
        Change(copy, "UPDATE url_summary SET depth = depth + 1 WHERE url_id = $id", ("$id", last));
        var changed = Assert.Throws<UrlQueryException>(() => UrlGridReader.Query(copy, query with { After = page.Next }));
        Assert.Contains("changed", Assert.Single(changed.Errors["after"]), StringComparison.Ordinal);

        Change(copy, "DELETE FROM url_summary WHERE url_id = $id", ("$id", last));
        var gone = Assert.Throws<UrlQueryException>(() => UrlGridReader.Query(copy, query with { After = page.Next }));
        Assert.Contains("isn't in the crawl any more", Assert.Single(gone.Errors["after"]), StringComparison.Ordinal);
    }

    /// <summary>A newer engine's kind of link, position or outcome, or a crafted file's numbers, read as unknown: never an error.</summary>
    [Fact]
    public void StoredValuesThisEngineDoesntKnowReadAsUnknown()
    {
        var copy = crawl.Copy();
        var home = Id("/");
        Change(copy, "UPDATE links SET kind = 99, position = 42, flags = flags | 1048576 WHERE rowid = (SELECT MIN(rowid) FROM links WHERE from_id = $id AND to_id <> $id)",
            ("$id", home));
        Change(copy, "UPDATE urls SET source = 9, fetch_mode = 7, state = 8 WHERE id = $id", ("$id", home));
        Change(copy, "UPDATE responses SET outcome = 50, response_ms = 9e999, ttfb_ms = -9e999 WHERE url_id = $id", ("$id", home));
        Change(copy, "UPDATE url_summary SET indexability_reason = 77, inlinks = 9e999 WHERE url_id = $id", ("$id", home));

        var url = UrlInspectorReader.Read(copy, home)!;
        Assert.Null(url.Discovery.Source);
        Assert.Null(url.Discovery.FetchMode);
        Assert.Null(url.Discovery.State);
        Assert.Null(url.Response!.Outcome);
        Assert.Null(url.Response.ResponseMs);
        Assert.Null(url.Response.TtfbMs);
        Assert.Null(url.Indexability.Reason);
        Assert.Equal(0, url.Links!.Inlinks);

        var outlinks = UrlInspectorReader.ReadLinks(copy, home, LinkDirection.Outlinks, new LinkQuery { Limit = 1_000 })!;
        var unknown = Assert.Single(outlinks.Rows, row => row.Kind is null);
        Assert.Null(unknown.Position);
        Assert.Null(unknown.Followed);
        var target = unknown.Id;
        Assert.Contains(UrlInspectorReader.ReadLinks(copy, target, LinkDirection.Inlinks, new LinkQuery { Limit = 1_000 })!.Rows, row => row.Kind is null);
        Assert.Null(UrlGridReader.Query(copy, new UrlQuery { Columns = ["outcome", "source"], Filters = [Number("id", FilterOperator.Eq, home)] }).Rows[0].Values[1]);
    }

    /// <summary>The cut at the limit backs off to a character's start: the page's own characters there, a replacement character included, stay.</summary>
    [Fact]
    public void TheSourceCutKeepsThePagesOwnCharacters()
    {
        var copy = crawl.Copy();
        var id = Id("/p/7");
        var kept = new string('a', UrlInspectorReader.MAX_SOURCE_BYTES - 3) + "�";
        StoreHtml(copy, id, kept + "after the cut");
        var source = UrlInspectorReader.ReadSource(copy, id)!;
        Assert.True(source.Truncated);
        Assert.Equal(kept, source.Html);

        // A four-byte character the cut falls inside is left out whole.
        var four = new string('a', UrlInspectorReader.MAX_SOURCE_BYTES - 2);
        StoreHtml(copy, id, four + "\U0001F600 and the rest");
        Assert.Equal(four, UrlInspectorReader.ReadSource(copy, id)!.Html);

        // A page that ends part-way through a character shows it replaced, as decoding it whole would.
        StoreHtml(copy, id, [.. "<p>end"u8, 0xE2, 0x82]);
        Assert.Equal("<p>end�", UrlInspectorReader.ReadSource(copy, id)!.Html);
    }

    /// <summary>
    /// Deep in a sort without an index, a page is picked out in memory rather than by SQLite's sorter: with the threshold
    /// at 0, every page of such sorts, with and without the URL summary, and with a text filter's set, is the sort's.
    /// </summary>
    [Fact]
    public void APagePickedOutInMemoryIsTheSorts()
    {
        var before = GridPlanner.SelectFrom;
        GridPlanner.SelectFrom = 0;
        try
        {
            (string Path, string Column, bool Descending, UrlFilter[] Filters)[] cases =
            [
                (crawl.Path, "followedInlinks", true, []),
                (crawl.Path, "outlinks", false, []),
                (crawl.Path, "titleLength", true, [Text("title", FilterOperator.Contains, "product")]),
                (crawl.Path, "inSitemap", false, [Names("type", FilterOperator.NotIn, "page")]),
                (crawl.Changed, "statusCode", true, []),
                (crawl.Changed, "responseMs", false, [Text("url", FilterOperator.Contains, "/p/")]),
                (crawl.Changed, "type", true, []),
                (crawl.Changed, "depth", false, [])
            ];

            foreach (var (path, column, descending, filters) in cases)
            {
                UrlSort[] sort = [new(column, descending)];
                Assert.Equal(crawl.Reference.Order(sort, crawl.Reference.Matching(filters)), ByOffset(path, new UrlQuery { Filters = filters, Sort = sort, Limit = PAGE }));
            }
        }
        finally
        {
            GridPlanner.SelectFrom = before;
        }
    }

    /// <summary>
    /// LIKE's wildcards and its escape in the user's text match themselves, for every text operator and the search, with
    /// the URL summary and without: a URL with an underscore isn't found by a different character there.
    /// </summary>
    [Fact]
    public void WildcardsInTextMatchThemselves()
    {
        var copy = crawl.Copy();
        var changed = crawl.Copy();
        var next = crawl.Reference.Rows.Keys.Max() + 1;
        Dictionary<int, string> planted = [];
        foreach (var name in new[] { "under_score", "underXscore", "per%cent", "perXcent", @"back\slash", "backXslash" })
        {
            planted[next] = name;
            Plant(copy, next, crawl.Server.BaseUri.AbsoluteUri + "wild/" + name, $"Title {name} here");
            Plant(changed, next++, crawl.Server.BaseUri.AbsoluteUri + "wild/" + name, $"Title {name} here");
        }

        Stamp(changed);
        foreach (var path in new[] { copy, changed })
        {
            foreach (var text in new[] { "under_score", "per%cent", @"back\slash" })
            {
                var only = planted.Single(entry => entry.Value == text).Key;
                Assert.Equal([only], Planted(path, new UrlQuery { Filters = [Text("url", FilterOperator.Contains, text)] }));
                Assert.Equal([only], Planted(path, new UrlQuery { Filters = [Text("title", FilterOperator.Contains, text)] }));
                Assert.Equal([only], Planted(path, new UrlQuery { Filters = [Text("url", FilterOperator.EndsWith, text)] }));
                Assert.Equal([only], Planted(path, new UrlQuery { Filters = [Text("title", FilterOperator.StartsWith, "title " + text)] }));
                Assert.Equal([only], Planted(path, new UrlQuery { Search = text }));
                Assert.Equal(planted.Keys.Where(id => id != only).Order(), Planted(path, new UrlQuery { Filters = [Text("url", FilterOperator.NotContains, text)] }));
            }
        }

        List<int> Planted(string path, UrlQuery query) =>
            [.. ByOffset(path, query with { Sort = [new UrlSort("id")], Limit = 1_000 }).Where(planted.ContainsKey)];
    }

    /// <summary>
    /// Counts are kept per analysis: analysed again with other thresholds, the same crawl's counts are read afresh,
    /// through the same cache.
    /// </summary>
    [Fact]
    public async Task AnAnalysisAgainCountsAfreshThroughTheSameCache()
    {
        var copy = crawl.Copy();
        var cache = new ReadCache();
        var query = new UrlQuery { Filters = [Names("check", FilterOperator.In, "title-too-long")], Limit = 1 };
        var before = UrlGridReader.Query(copy, query, cache).Matching;
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(copy, new AnalysisSettings { TitleMinLength = 5, TitleMaxLength = 10 });
        var after = UrlGridReader.Query(copy, query, cache).Matching;
        Assert.True(after > before, $"{after} titles over 10 characters, against {before} over 60");
        Assert.Equal(after, UrlGridReader.Query(copy, query, new ReadCache()).Matching);
    }

    /// <summary>
    /// A long read of the file that runs until a writer stops it the first time, and reads the URLs at once the second:
    /// its task, and how many times it has started.
    /// </summary>
    private static (Task<long> Read, Func<int> Attempts) StartLongRead(string path)
    {
        var attempts = 0;
        using var started = new ManualResetEventSlim();
        var read = Task.Run(() => GridReadContext.Read(path, null, default, context =>
        {
            if (Interlocked.Increment(ref attempts) > 1)
            {
                return context.Scalar("SELECT COUNT(*) FROM urls", []);
            }

            started.Set();
            return context.Scalar(FOREVER, []);
        }));

        Assert.True(started.Wait(TimeSpan.FromSeconds(10)));

        // Part-way through its statement.
        Thread.Sleep(100);
        return (read, () => Volatile.Read(ref attempts));
    }

    private static string JournalMode(string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode";
        return (string)command.ExecuteScalar()!;
    }

    /// <summary>A URL and its response with a title, and its row in the URL summary: a page the crawl found.</summary>
    private static void Plant(string path, int id, string url, string title)
    {
        Change(path, "INSERT INTO urls (id, url, is_internal, fetch_mode, depth, state, skip_reason, source) VALUES ($id, $url, 1, 0, 2, 2, 0, 1)",
            ("$id", id), ("$url", url));
        Change(path, """
            INSERT INTO responses (url_id, fetched_at, outcome, status_code, content_type, ttfb_ms, response_ms, is_html, title, title_count,
                                   meta_description_count, h1_count, is_noindex, is_nofollow, indexability, indexability_reason, word_count, outlink_count)
            VALUES ($id, 0, 0, 200, 'text/html', 1, 1, 1, $title, 1, 0, 0, 0, 0, 0, 0, 100, 0)
            """, ("$id", id), ("$title", title));
        Change(path, """
            INSERT INTO url_summary (url_id, is_internal, source, depth, resource_type, status_code, indexable, indexability_reason, final_url_id,
                                     inlinks, followed_inlinks, nofollow_inlinks, linking_pages, issues, errors, warnings, notices, used_on)
            VALUES ($id, 1, 1, 2, 0, 200, 1, 0, $id, 0, 0, 0, 0, 0, 0, 0, 0, 0)
            """, ("$id", id));
    }

    private static void StoreHtml(string path, int id, string html) => StoreHtml(path, id, Encoding.UTF8.GetBytes(html));

    private static void StoreHtml(string path, int id, byte[] html)
    {
        using var packed = new MemoryStream();
        using (var brotli = new BrotliStream(packed, CompressionLevel.Fastest, leaveOpen: true))
        {
            brotli.Write(html);
        }

        Change(path, "UPDATE response_html SET html = $html WHERE url_id = $id", ("$html", packed.ToArray()), ("$id", id));
    }

    private int Id(string path) => UrlGridReader.Lookup(crawl.Path, crawl.Server.BaseUri.AbsoluteUri.TrimEnd('/') + path)!.Id;

    private static List<int> ByOffset(string path, UrlQuery query)
    {
        List<int> ids = [];
        for (var offset = 0; ; offset += query.Limit)
        {
            var page = UrlGridReader.Query(path, query with { Offset = offset });
            ids.AddRange(page.Rows.Select(row => row.Id));
            if (page.Next is null)
            {
                Assert.Equal(page.Matching, ids.Count);
                return ids;
            }
        }
    }
}
