E = 'src/ShoutingIguana.Engine/Storage/'
S = 'src/ShoutingIguana.Sidecar/'
C = 'src/ShoutingIguana.Cli/Benchmarking/ReaderBenchmark.cs'

WRITES = [
(S + 'Crawls/ReadSlots.cs', '''using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Sidecar.Models;

namespace ShoutingIguana.Sidecar.Crawls;

/// <summary>
/// How many reads of crawl files run at once: one a core (<see cref="SidecarOptions.GridReads"/>), as each keeps a core
/// busy. A grid read takes one slot, queueing for it; a read that can split a pass over several connections takes more
/// only from those free at that moment, never waiting, and never the last one free, so a read that arrives while the
/// pass runs still starts at once.
/// </summary>
internal sealed class ReadSlots(SidecarOptions options)
{
    /// <summary>The slots free now: a read waits on it for its own.</summary>
    public SemaphoreSlim Free { get; } = new(options.GridReads, options.GridReads);

    /// <summary>The slots a read may take beside its own, as the engine's readers ask for them.</summary>
    public ReadHelpers Helpers => new(() => Free.CurrentCount > 1 && Free.Wait(0), () => Free.Release());
}
'''),
]

EDITS = [
# A regular expression given up on by a part of the pass is the pass's refusal.
(E + 'LinkGridReader.cs',
'''                using var alongside = context.OpenAlongside(stop.Token);
                return alongside is null ? ((LinkIdSet? Ids, LinkTally? Tally)?)null : Part(alongside.Context, Bounds(part));
            }
            catch
            {
                // One part failed, so the pass has: the others stop.
                stop.Cancel();
                throw;
            }
''',
'''                using var alongside = context.OpenAlongside(stop.Token);
                try
                {
                    return alongside is null ? ((LinkIdSet? Ids, LinkTally? Tally)?)null : Part(alongside.Context, Bounds(part));
                }
                catch (Exception exception) when (alongside?.Context.RefusedPattern is { } pattern && exception is not RegexTooComplexException)
                {
                    // A pattern this part gave up on: the read's refusal, as when the read's own connection gives up on it.
                    throw new RegexTooComplexException(pattern, exception);
                }
            }
            catch
            {
                // One part failed, so the pass has: the others stop.
                stop.Cancel();
                throw;
            }
'''),
(S + 'Crawls/GridData.cs',
'''internal sealed class GridData(CrawlManager crawls, ReadCache cache, SidecarOptions options)
{
    private readonly SemaphoreSlim _reads = new(options.GridReads, options.GridReads);
''',
'''internal sealed class GridData(CrawlManager crawls, ReadCache cache, ReadSlots slots)
{
    private readonly SemaphoreSlim _reads = slots.Free;
'''),
(S + 'SidecarHost.cs',
'''        builder.Services.AddSingleton(new ReadCache());
''',
'''        // The grids' reads keep what they count in one cache, and a pass over a large crawl's links may use the read
        // slots that are free beside its own.
        builder.Services.AddSingleton<ReadSlots>();
        builder.Services.AddSingleton(services => new ReadCache { Helpers = services.GetRequiredService<ReadSlots>().Helpers });
'''),
# The reader benchmark: the passes as a sidecar with cores to spare runs them, and on one connection.
(C,
'''        Linked("Links grid: text contains a common word (every link read)", "link-pass", new UrlQuery { Filters = [Text("text", FilterOperator.Contains, "product")] });
        Linked("Links grid: text contains rare text (every link read)", "link-pass", new UrlQuery { Filters = [Text("text", FilterOperator.Contains, "Product 12345")] });
        Linked("Links grid: from pages at depth 2 (every link read)", "link-pass", new UrlQuery { Filters = [Number("from.depth", FilterOperator.Eq, 2)] });
        Linked("Links grid: search (every link read)", "link-pass", new UrlQuery { Search = "/p/123" });
        Linked("Links grid: content links to pages four clicks from the start (a target and a position, the fewer pass: every link read, deep in)", "link-pass",
            new UrlQuery { Filters = [In("position", "content"), Number("to.depth", FilterOperator.Eq, 4)] });
        Timed("Links grid: facets of a text filter (every link read)", "facets", "link-pass", null,
            () => LinkGridReader.Facets(path, new UrlFacetQuery { Filters = [Text("text", FilterOperator.Contains, "product")] }));
''',
'''        // The reads that pass over every link: on one connection, as a running crawl's are read and as any is when every
        // read slot is taken (link-pass); and split over the connections a host with cores to spare lends (link-pass-split).
        (string Name, UrlQuery Query)[] passes =
        [
            ("text contains a common word", new UrlQuery { Filters = [Text("text", FilterOperator.Contains, "product")] }),
            ("text contains rare text", new UrlQuery { Filters = [Text("text", FilterOperator.Contains, "Product 12345")] }),
            ("text matches a pattern", new UrlQuery { Filters = [Text("text", FilterOperator.Regex, @"Product 1\\d{3}$")] }),
            ("from pages three clicks from the start", new UrlQuery { Filters = [Number("from.depth", FilterOperator.Eq, 3)] }),
            ("from pages four clicks from the start", new UrlQuery { Filters = [Number("from.depth", FilterOperator.Eq, 4)] }),
            ("search", new UrlQuery { Search = "/p/123" }),
            ("search for a common word", new UrlQuery { Search = "product" }),
            ("content links to pages four clicks from the start (a target and a position, the fewer pass: deep in)",
                new UrlQuery { Filters = [In("position", "content"), Number("to.depth", FilterOperator.Eq, 4)] })
        ];
        Linked("Links grid: from pages at depth 2 (few pages: their links read through their index)", "link-pass", new UrlQuery { Filters = [Number("from.depth", FilterOperator.Eq, 2)] });
        foreach (var (name, query) in passes)
        {
            Linked($"Links grid: {name} (every link read)", "link-pass", query);
            Linked($"Links grid: {name} (every link read, on several connections)", "link-pass-split", query, HELPERS);
        }

        Timed("Links grid: facets of a text filter (every link read)", "facets", "link-pass", null,
            () => LinkGridReader.Facets(path, new UrlFacetQuery { Filters = [Text("text", FilterOperator.Contains, "product")] }));
        Timed("Links grid: facets of a text filter (every link read, on several connections)", "facets", "link-pass-split", null,
            () => LinkGridReader.Facets(path, new UrlFacetQuery { Filters = [Text("text", FilterOperator.Contains, "product")] }, new ReadCache { Helpers = HELPERS }));
'''),
(C,
'''    /// <summary>A links grid read: its first page, and the page half way down and the one just past it.</summary>
    private void Linked(string name, string readClass, UrlQuery query)
    {
        if (!Wanted(name))
        {
            return;
        }

        var shown = query with { Columns = ["from.url", "to.url", "kind", "text", "position", "followed", "to.statusCode"] };
        long matching;
        try
        {
            matching = LinkGridReader.Query(path, shown with { Limit = 1 }).Matching;
        }
        catch (NeedsAnalysisException)
        {
            progress($"  skipped    {name}: it needs the URL summary");
            return;
        }

        Paged(name, "links-grid", readClass, matching, offset => LinkGridReader.Query(path, shown with { Offset = offset }));
    }
''',
'''    /// <summary>
    /// The connections a read may use beside its own, as a host with every core free would lend them: every other core. A
    /// read with them still keeps nothing between runs: each is given a cache of its own, empty, that only carries them.
    /// </summary>
    private static readonly ReadHelpers HELPERS = ReadHelpers.UpTo(Environment.ProcessorCount - 1);

    /// <summary>A links grid read: its first page, and the page half way down and the one just past it.</summary>
    /// <param name="helpers">Connections the read may split a pass over, beside its own; none reads on one.</param>
    private void Linked(string name, string readClass, UrlQuery query, ReadHelpers? helpers = null)
    {
        if (!Wanted(name))
        {
            return;
        }

        var shown = query with { Columns = ["from.url", "to.url", "kind", "text", "position", "followed", "to.statusCode"] };
        long matching;
        try
        {
            matching = LinkGridReader.Query(path, shown with { Limit = 1 }).Matching;
        }
        catch (NeedsAnalysisException)
        {
            progress($"  skipped    {name}: it needs the URL summary");
            return;
        }

        Paged(name, "links-grid", readClass, matching, offset => LinkGridReader.Query(path, shown with { Offset = offset }, helpers is null ? null : new ReadCache { Helpers = helpers }));
    }
'''),
(C,
'''        ["search"] = 25,
''',
'''        ["link-pass-split"] = 2_500,
        ["search"] = 25,
'''),
]
