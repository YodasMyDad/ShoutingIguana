using System.Collections.Concurrent;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using static ShoutingIguana.Engine.Tests.Support.GridQueries;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>
/// A pass over every link, split by link id over the connections a host lends: it answers exactly as the pass on one
/// connection does (the count, every page, the facets, a sort by the target), gives its helpers back whatever becomes of
/// it, is stopped whole by a writer that wants the file, and stays on one connection for a crawl that's running, for a
/// host that lends none, and for a crawl too small to be worth it.
/// </summary>
[Collection(GridCrawlCollection.NAME)]
public sealed class LinkPassSplitTests(GridCrawl crawl) : IDisposable
{
    private readonly long _splitFrom = LinkGridReader.SplitFrom;
    private readonly int _passHelpers = LinkGridReader.PassHelpers;
    private readonly TimeSpan _regexBudget = GridReadContext.RegexBudget;

    public void Dispose()
    {
        (LinkGridReader.SplitFrom, LinkGridReader.PassHelpers, LinkGridReader.Split, LinkGridReader.Ran) = (_splitFrom, _passHelpers, null, null);
        GridReadContext.RegexBudget = _regexBudget;
    }

    /// <summary>Filter sets the link summary can't answer, so every link is read: on the link's text, on the page it's on, a search, and a target with a text.</summary>
    private static readonly (string Name, UrlFilter[] Filters, string? Search)[] PASSES =
    [
        ("text contains", [Text("text", FilterOperator.Contains, "product")], null),
        ("text doesn't contain", [Text("text", FilterOperator.NotContains, "PRODUCT")], null),
        ("text matches", [Text("text", FilterOperator.Regex, @"^Product \d+$")], null),
        ("text is", [Text("text", FilterOperator.Eq, "Home")], null),
        ("to a section", [new UrlFilter("fragment", FilterOperator.IsNotEmpty)], null),
        ("from pages two clicks in or more", [Number("from.depth", FilterOperator.Gte, 2)], null),
        ("from pages that answered", [Names("from.statusClass", FilterOperator.In, "2xx")], null),
        ("from products to working pages", [Text("from.url", FilterOperator.Contains, "/p/"), Names("to.statusClass", FilterOperator.In, "2xx")], null),
        ("hyperlinks with a word, to pages", [Names("kind", FilterOperator.In, "hyperlink"), Text("text", FilterOperator.Contains, "a"), Names("to.type", FilterOperator.In, "page")], null),
        ("search", [], "about"),
        ("search with a filter", [Names("position", FilterOperator.In, "content")], "product"),
        ("nothing matches", [Text("text", FilterOperator.Contains, "no link says this at all")], null)
    ];

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    public void APassSplitOverSeveralConnectionsAnswersAsOneDoes(int helpers)
    {
        LinkGridReader.SplitFrom = 1;
        LinkGridReader.PassHelpers = helpers;
        ConcurrentQueue<int> split = new();
        LinkGridReader.Split = split.Enqueue;
        string[] columns = ["from.id", "to.id", "kind", "text"];
        foreach (var path in new[] { crawl.Path, crawl.Changed })
        {
            foreach (var (name, filters, search) in PASSES)
            {
                // A crawl continued since its analysis has no link summary: every filter set is a pass, a sort by the target too.
                foreach (var sort in new UrlSort?[] { null, new UrlSort("id", true), new UrlSort("to.url"), new UrlSort("to.statusCode", true) })
                {
                    var query = new UrlQuery { Columns = columns, Filters = filters, Search = search, Sort = sort is null ? [] : [sort], Limit = 200 };
                    split.Clear();
                    var (one, parts) = (Walk(path, query, new ReadCache()), split.ToArray());
                    Assert.All(parts, connections => Assert.Equal(1, connections));
                    split.Clear();
                    var several = Walk(path, query, new ReadCache { Helpers = ReadHelpers.UpTo(helpers) });
                    Assert.True(one.SequenceEqual(several), $"{name}, {sort?.Column ?? "crawl order"}, {System.IO.Path.GetFileName(path)}: {one.Count} links on one connection, {several.Count} on {helpers + 1}");

                    // Every link was read, on every connection lent: unless the filters name few enough targets for the links'
                    // index to find their links, as this small crawl's are, which reads only those and is no pass.
                    if (!filters.Any(filter => filter.Column.StartsWith("to.", StringComparison.Ordinal)))
                    {
                        Assert.True(split.Contains(helpers + 1), $"{name}, {sort?.Column ?? "crawl order"}, {System.IO.Path.GetFileName(path)}: read on {string.Join(", ", split)} connections");
                    }
                }

                var facets = LinkGridReader.Facets(path, new UrlFacetQuery { Filters = filters, Search = search }, new ReadCache());
                var splitFacets = LinkGridReader.Facets(path, new UrlFacetQuery { Filters = filters, Search = search }, new ReadCache { Helpers = ReadHelpers.UpTo(helpers) });
                Assert.Equal((facets.Matching, facets.Total), (splitFacets.Matching, splitFacets.Total));
                foreach (var facet in LinkGridReader.FACETS)
                {
                    Assert.Equal(facets.Facets[facet].Select(bucket => (bucket.Value, bucket.Count)), splitFacets.Facets[facet].Select(bucket => (bucket.Value, bucket.Count)));
                }
            }
        }

        // The filter sets did match links, and one matched none.
        Assert.True(LinkGridReader.Query(crawl.Path, new UrlQuery { Filters = PASSES[0].Filters, Limit = 1 }, new ReadCache { Helpers = ReadHelpers.UpTo(helpers) }).Matching > 100);
        Assert.Equal(0, LinkGridReader.Query(crawl.Path, new UrlQuery { Filters = PASSES[^1].Filters, Limit = 1 }, new ReadCache { Helpers = ReadHelpers.UpTo(helpers) }).Matching);
    }

    /// <summary>
    /// A pass stays on the read's one connection where splitting it isn't right or isn't worth it: a crawl that's running
    /// (its one snapshot is the moment it answers for), a host that lends no helpers, a read with no cache, few links,
    /// and a filter the link summary answers without a pass at all.
    /// </summary>
    [Fact]
    public void APassStaysOnOneConnectionWhereSplittingIsntRightOrWorthIt()
    {
        LinkGridReader.SplitFrom = 1;
        List<int> split = [];
        LinkGridReader.Split = split.Add;
        var query = new UrlQuery { Filters = [Text("text", FilterOperator.Contains, "product")], Limit = 5 };
        long Read(string path, ReadCache? cache) => LinkGridReader.Query(path, query, cache).Matching;

        var expected = Read(crawl.Path, null);
        Assert.Equal([1], split);
        Assert.Equal(expected, Read(crawl.Path, new ReadCache()));
        Assert.Equal(expected, Read(crawl.Path, new ReadCache { Helpers = ReadHelpers.UpTo(0) }));
        Assert.Equal([1, 1, 1], split);

        // With helpers, split; but not below the size worth it, nor past the helpers there are.
        Assert.Equal(expected, Read(crawl.Path, new ReadCache { Helpers = ReadHelpers.UpTo(8) }));
        Assert.Equal(LinkGridReader.PassHelpers + 1, split[^1]);
        Assert.Equal(expected, Read(crawl.Path, new ReadCache { Helpers = ReadHelpers.UpTo(2) }));
        Assert.Equal(3, split[^1]);
        LinkGridReader.SplitFrom = TestCrawls.Count(crawl.Path, "SELECT MAX(rowid) FROM links") + 1;
        Assert.Equal(expected, Read(crawl.Path, new ReadCache { Helpers = ReadHelpers.UpTo(8) }));
        Assert.Equal(1, split[^1]);
        LinkGridReader.SplitFrom = 1;

        // A filter the link summary answers reads no link at all: nothing to split, and no helper taken.
        split.Clear();
        var taken = 0;
        ReadHelpers counting = new(() => Interlocked.Increment(ref taken) > 0, () => { });
        Assert.True(LinkGridReader.Query(crawl.Path, new UrlQuery { Filters = [Names("kind", FilterOperator.In, "hyperlink")], Limit = 5 }, new ReadCache { Helpers = counting }).Matching > 0);
        Assert.Equal((0, 0), (split.Count, taken));

        // A crawl that's running: one connection, one snapshot, whatever the host could lend.
        var running = crawl.Copy();
        Change(running, $"UPDATE crawl SET status = {(int)CrawlStatus.Running}, updated_at = updated_at + 1");
        using (CrawlFileLock.Acquire(running))
        {
            split.Clear();
            Assert.Equal(expected, Read(running, new ReadCache { Helpers = ReadHelpers.UpTo(8) }));
            Assert.All(split, connections => Assert.Equal(1, connections));
            Assert.NotEmpty(split);
        }
    }

    /// <summary>
    /// The helpers a pass took are given back as it ends, however it ends: answered, refused (a pattern too complex for the
    /// crawl, given up on by whichever part met it), or stopped with its request.
    /// </summary>
    [Fact]
    public void ThePassGivesItsHelpersBackHoweverItEnds()
    {
        LinkGridReader.SplitFrom = 1;
        int taken = 0, released = 0, free = 3;
        ReadHelpers helpers = new(
            () =>
            {
                if (Interlocked.Decrement(ref free) < 0)
                {
                    Interlocked.Increment(ref free);
                    return false;
                }

                Interlocked.Increment(ref taken);
                return true;
            },
            () =>
            {
                Interlocked.Increment(ref released);
                Interlocked.Increment(ref free);
            });

        // Answered.
        Assert.True(LinkGridReader.Query(crawl.Path, new UrlQuery { Filters = [Text("text", FilterOperator.Contains, "product")], Limit = 5 }, new ReadCache { Helpers = helpers }).Matching > 0);
        Assert.Equal((3, 3, 3), (taken, released, free));

        // Refused: no time at all for a pattern, so the first part to match one gives up on it, and the read says which.
        GridReadContext.RegexBudget = TimeSpan.Zero;
        var refused = Assert.Throws<UrlQueryException>(() =>
            LinkGridReader.Query(crawl.Path, new UrlQuery { Filters = [Text("text", FilterOperator.Regex, @"^Product \d+$")], Limit = 5 }, new ReadCache { Helpers = helpers }));
        Assert.Equal(["filters[0].value"], refused.Errors.Keys);
        Assert.Equal(ReaderFunctions.TOO_COMPLEX, refused.Errors["filters[0].value"][0]);
        Assert.Equal((6, 6, 3), (taken, released, free));
        GridReadContext.RegexBudget = _regexBudget;

        // Stopped: its token is cancelled as its parts are about to read.
        using var cancel = new CancellationTokenSource();
        LinkGridReader.Ran = _ => cancel.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            LinkGridReader.Query(crawl.Path, new UrlQuery { Filters = [Text("text", FilterOperator.Contains, "product")], Limit = 5 }, new ReadCache { Helpers = helpers }, cancel.Token));
        LinkGridReader.Ran = null;
        Assert.Equal((taken, 3), (released, free));
        Assert.True(taken >= 6);
    }

    /// <summary>
    /// A writer here that wants the file (an analysis or a continue switching it to WAL mode) stops every part of a pass
    /// under way, and none is part-way through the file while the writer has it; the read then runs again, split as
    /// before, to the answer it would have given.
    /// </summary>
    [Fact]
    public async Task AWriterStopsEveryPartOfAPassAndTheReadRunsAgain()
    {
        LinkGridReader.SplitFrom = 1;
        var copy = crawl.Copy();
        var query = new UrlQuery { Filters = [Text("text", FilterOperator.Contains, "product")], Limit = 5 };
        var expected = LinkGridReader.Query(copy, query).Matching;

        using var held = new ManualResetEventSlim();
        using var go = new ManualResetEventSlim();
        var (starting, writing, during) = (0, 0, 0);
        LinkGridReader.Ran = _ =>
        {
            // Each part, as it's about to read: the first pass's parts wait here until the writer has asked for the file.
            if (Volatile.Read(ref writing) == 1)
            {
                Interlocked.Increment(ref during);
            }

            if (Interlocked.Increment(ref starting) <= 4)
            {
                held.Set();
                go.Wait(TimeSpan.FromSeconds(20));
            }
        };
        List<int> split = [];
        LinkGridReader.Split = connections =>
        {
            lock (split)
            {
                split.Add(connections);
            }
        };

        var read = Task.Run(() => LinkGridReader.Query(copy, query, new ReadCache { Helpers = ReadHelpers.UpTo(3) }).Matching);
        Assert.True(held.Wait(TimeSpan.FromSeconds(20)), "No part of the pass began.");

        var writer = Task.Run(() =>
        {
            using (CrawlReadGate.Exclusive(copy))
            {
                // The file is the writer's: no part of the pass is reading it, nor begins to while it's held.
                Volatile.Write(ref writing, 1);
                Thread.Sleep(300);
                Volatile.Write(ref writing, 0);
            }
        });

        // The writer has asked: the parts held here go on, find themselves stopped, and let go of the file.
        await Task.Delay(150);
        go.Set();
        await writer.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(expected, await read.WaitAsync(TimeSpan.FromSeconds(20)));
        Assert.Equal(0, Volatile.Read(ref during));

        // The pass that answered ran after the writer was done, split over the four connections.
        Assert.True(Volatile.Read(ref starting) > 4, $"Only {starting} parts began: the read didn't run again.");
        Assert.Equal(4, split[^1]);
    }

    /// <summary>Every link the query matches, by id, in the order asked, read a page at a time through one cache.</summary>
    private static List<long> Walk(string path, UrlQuery query, ReadCache cache)
    {
        List<long> ids = [];
        for (var offset = 0; ; offset += query.Limit)
        {
            var page = LinkGridReader.Query(path, query with { Offset = offset }, cache);
            ids.AddRange(page.Rows.Select(row => row.Id));
            if (page.Next is null)
            {
                Assert.Equal(page.Matching, ids.Count);
                return ids;
            }
        }
    }
}
