using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// The links grid's reads: a page of the crawl's links with the columns a view shows, and the facets its filter chips
/// count. Every stored link is a row, in the order the crawl stored them; each end's URL grid columns come with it
/// (<c>from.</c>, <c>to.</c>), read and filtered by the URL grid's own code. Each read takes one snapshot of the file, as
/// the URL grid's do.
/// <para>
/// What's counted is kept between reads, and while a crawl runs each read's snapshot is a later one than the last. So a
/// running crawl is answered for one moment of it (<see cref="LinkMoment"/>), kept for <see cref="ReadCache.LIVE_FOR"/>:
/// everything counted is counted for that moment's links, whichever read counts it, and an answer's count, total, rows,
/// facets and cursor are of the same links.
/// </para>
/// <para>
/// A crawl has some 50 links a page, so nothing here may read them all and stay in budget. With the link summary an
/// analysis stores (<see cref="CrawlSchema.LINK_SUMMARY"/>), the count, the facets and each target's matching links come
/// from its rows, about 2 a URL, for any filter on what a link is and on its target; the rows of a page are then read from
/// the links by walking them until the page is full, through the few targets a rare filter leads to, or from a set of the
/// matching links' ids read once and kept. Without it (a crawl running, never analysed or continued since), or for a
/// filter it doesn't hold (the link's text, the page it's on), the links are read once per filter set, in one pass that
/// gives the count, the facets and the set.
/// </para>
/// </summary>
public static class LinkGridReader
{
    /// <summary>The facets <see cref="Facets"/> counts, in order.</summary>
    public static readonly IReadOnlyList<string> FACETS = ["kind", "position", "follow", "to.statusClass", "to.isInternal"];

    /// <summary>What a page shows when it asks for no columns.</summary>
    private static readonly string[] DEFAULT_COLUMNS = ["from.url", "to.url", "kind", "text"];

    /// <summary>The follow facet's values, by <see cref="LinkTally.FollowOf"/>, each with the filter that selects its links.</summary>
    private static readonly (string Value, UrlFilter Filter)[] FOLLOW =
    [
        ("followed", new UrlFilter("followed", FilterOperator.Is) { Value = FilterValue.Of(true) }),
        ("nofollow", new UrlFilter("followed", FilterOperator.Is) { Value = FilterValue.Of(false) }),
        ("notHyperlink", new UrlFilter("followed", FilterOperator.IsEmpty))
    ];

    /// <summary>
    /// A filter whose links lead to at most this many targets has them read through those targets: all of them and kept,
    /// when the targets have few links in all (<see cref="DriveRows"/>), or a page at a time on the links' index by target
    /// (<see cref="Placed"/>). Tests change it, to read every page each way: below 0, none are.
    /// </summary>
    internal static int DriveCap { get; set; } = 20_000;

    /// <summary>
    /// The most links read through a filter's targets to find its own and keep them, or a thirty-second of the crawl's if
    /// that's more. Tests lower it, to read a small crawl's pages as a large one's are: below 0, no links are few.
    /// </summary>
    internal static int DriveRows { get; set; } = 100_000;

    /// <summary>
    /// A page's place on the links' index by target is closed in on until it's at most this many links past the id found,
    /// which the page's read then skips. Tests lower it, so a small crawl's places are closed in on to the link.
    /// </summary>
    internal static int PlaceSlack { get; set; } = 1_000;

    /// <summary>The first tries at a place take the links as evenly spread; after these, each try halves what's left.</summary>
    private const int EVEN_TRIES = 6;

    /// <summary>The fewest ids a page's read on the index by target looks through.</summary>
    private const int LEAST_SPAN = 4_096;

    /// <summary>
    /// A walk that takes longer than this found its links bunched far from the end it started at (a filter on deep targets,
    /// whose links are all on pages crawled late): the filter set's next pages are read from a set of its ids instead.
    /// Tests lower it, so that any walk has taken long.
    /// </summary>
    internal static TimeSpan SlowWalk { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>Tests only: false reads pages without the links counted by block, as if every filter were on the target.</summary>
    internal static bool UseBlocks { get; set; } = true;

    /// <summary>
    /// The most target URL ids a condition lists for the links' index by target; a larger set is tested link by link. Tests
    /// change it, to read every page each way.
    /// </summary>
    internal static int ListedEnds { get; set; } = 1_000;

    /// <summary>
    /// A page is read by walking the links when that should pass at most this many, or a sixteenth of them if that's more.
    /// Tests change it, to read every page each way: below 0, no page is.
    /// </summary>
    internal static int WalkRows { get; set; } = 200_000;

    /// <summary>
    /// The most helpers a pass over every link takes beside its own connection (<see cref="ReadHelpers"/>): eight
    /// connections in all. Measured on the 100k benchmark's 5 million links, beside the same pass on one connection, a
    /// pass split in four takes a quarter of the time and one split in eight a sixth, for twice the cores kept from other
    /// reads for a fraction of a second. Eight is what brings a search and a filter on the source's depth inside the
    /// budget (190 and 230 ms, where four gave 290 and 400), so eight it is; a host with fewer cores to lend gives fewer.
    /// Each part keeps its own ids and counts until they're put together: a megabyte a part for those 5 million links.
    /// Tests change it.
    /// </summary>
    internal static int PassHelpers { get; set; } = 7;

    /// <summary>
    /// From this many links on, a pass over all of them is worth splitting: below it the pass takes tens of milliseconds
    /// by itself, and opening the connections to split it a few. Tests lower it.
    /// </summary>
    internal static long SplitFrom { get; set; } = 500_000;

    /// <summary>Tests only: how many connections each pass over the links ran on, in order.</summary>
    internal static Action<int>? Split { get; set; }

    /// <summary>Unknown, among the status classes a URL's facts hold.</summary>
    private const int NO_STATUS_CLASS = 7;

    /// <summary>The moment a running crawl's reads answer for is kept under this.</summary>
    private const string MOMENT = "links|moment";

    /// <summary>Tests only: each statement that reads links as it runs, so the way a page was read can be told.</summary>
    internal static Action<string>? Ran { get; set; }

    /// <summary>The links grid's columns: a link's own, then each end's.</summary>
    public static IReadOnlyList<UrlColumnInfo> Columns { get; } = LinkColumns.Describe();

    /// <summary>One page of the links grid.</summary>
    /// <param name="query">
    /// The URL grid's query, over the links grid's columns (<see cref="Columns"/>). It sorts by one key: the link's id, or a
    /// column of the target that the URL grid sorts by, which keeps a target's links together, in crawl order.
    /// </param>
    /// <param name="cache">Where counts and sets are kept between reads; none reads them each time.</param>
    /// <exception cref="UrlQueryException">The query can't be run: the errors say why, by field.</exception>
    /// <exception cref="NeedsAnalysisException">It filters or sorts on the URL summary, and there's none that describes the crawl.</exception>
    /// <exception cref="InvalidOperationException">The file isn't a crawl, or was written with another schema.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static LinkGridPage Query(string databasePath, UrlQuery query, ReadCache? cache = null, CancellationToken cancellationToken = default)
    {
        UrlGridReader.ThrowIfInvalid(Validate(query));
        return UrlGridReader.Refusing(query.Filters, () => GridReadContext.Read(databasePath, cache, cancellationToken, context => Query(context, query)));
    }

    /// <summary>One page of the links grid through a context already open, whose snapshot is the read's; the query is taken as checked.</summary>
    internal static LinkGridPage Query(GridReadContext context, UrlQuery query) => Settled(context, () => Read(context, query));

    /// <summary>
    /// The matching links counted by the values of each facet, each value with the filter that selects its links.
    /// </summary>
    /// <inheritdoc cref="Query" path="/exception"/>
    public static UrlFacets Facets(string databasePath, UrlFacetQuery query, ReadCache? cache = null, CancellationToken cancellationToken = default)
    {
        Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);
        ValidateFilters(query.Filters, query.Search, errors);
        RefuseEnds(query.LinkEnds, errors);
        for (var index = 0; index < query.Facets.Count; index++)
        {
            if (!FACETS.Contains(query.Facets[index]))
            {
                errors[$"facets[{index}]"] = [$"There's no facet called '{query.Facets[index]}': there are {string.Join(", ", FACETS)}."];
            }
        }

        UrlGridReader.ThrowIfInvalid(errors);
        return UrlGridReader.Refusing(query.Filters, () => GridReadContext.Read(databasePath, cache, cancellationToken, context => Settled(context, () => Facets(context, query))));
    }

    /// <summary>
    /// One page of the URLs at one end of the links the filters match, each once, in the order the crawl found them, with
    /// how many of those links each has: a view's targets (or the pages its links are on) as a list, however many links
    /// the view holds. Their number is the URL grid's count under the same links (<see cref="UrlQuery.LinkEnds"/>).
    /// <list type="bullet">
    /// <item>The targets come from what the links grid counts by target anyway: the link summary's rows where it holds
    /// what the filters test, else the pass over the links that gives the view its count.</item>
    /// <item>The sources are counted in a pass over the links, as nothing counts links by the page they're on: once per
    /// set of filters, and kept.</item>
    /// </list>
    /// </summary>
    /// <inheritdoc cref="Query(string, UrlQuery, ReadCache?, CancellationToken)" path="/exception"/>
    public static LinkEndPage Ends(string databasePath, LinkEndQuery query, ReadCache? cache = null, CancellationToken cancellationToken = default)
    {
        Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);
        ValidateFilters(query.Filters, query.Search, errors);
        UrlQueryCompiler.ValidatePaging(query.Offset, query.Limit, after: null, errors);
        UrlGridReader.ThrowIfInvalid(errors);
        return UrlGridReader.Refusing(query.Filters, () => GridReadContext.Read(databasePath, cache, cancellationToken, context =>
        {
            var filters = Compile(context, query.Filters, query.Search, sort: null);
            var tally = EndTally(context, filters, query.End);
            var total = Held(context, $"linkendcount|{query.End}|{filters.Key}", () => tally.Targets().LongCount());
            List<(int Id, int Links)> ends = [.. tally.Targets().Skip(query.Offset).Take(query.Limit)];
            Dictionary<int, string> urls = [];
            if (ends.Count > 0)
            {
                using var command = context.Command($"SELECT id, url FROM urls WHERE id IN ({string.Join(",", ends.Select(end => end.Id.ToString(CultureInfo.InvariantCulture)))})", []);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    urls[reader.GetInt32(0)] = reader.GetString(1);
                }
            }

            // An end the crawl has no URL for (a crafted file's link to nowhere) has no row: it's counted, and can't be named.
            return new LinkEndPage(query.End, total, tally.Links, query.Offset,
                [.. ends.Where(end => urls.ContainsKey(end.Id)).Select(end => new LinkEndRow(end.Id, urls[end.Id], end.Links))], context.Summary);
        }));
    }

    /// <summary>The filters of a condition on the ends of links, checked as the links grid's own are, each error under <c>linkEnds</c>.</summary>
    internal static void ValidateEnds(UrlLinkEnds? ends, Dictionary<string, List<string>> errors)
    {
        if (ends is null)
        {
            return;
        }

        Dictionary<string, List<string>> found = new(StringComparer.Ordinal);
        ValidateFilters(ends.Filters, ends.Search, found);
        foreach (var (field, messages) in found)
        {
            messages.ForEach(message => UrlQueryCompiler.Add(errors, "linkEnds." + field, message));
        }
    }

    /// <summary>
    /// The URLs at one end of the links some link filters match, as a set, for the URL grid to narrow itself by: read from
    /// the same counts <see cref="Ends"/> lists, so the grid's count is the list's, and kept per set of link filters.
    /// </summary>
    /// <exception cref="NeedsAnalysisException">A link filter needs the URL summary, and there's none.</exception>
    internal static UrlIdSet EndIds(GridReadContext context, UrlLinkEnds ends)
    {
        var filters = Compile(context, ends.Filters, ends.Search, sort: null);
        return HeldLarge(context, $"linkendids|{ends.End}|{filters.Key}", () =>
        {
            var set = UrlIds(context);
            foreach (var (id, _) in EndTally(context, filters, ends.End).Targets())
            {
                set.Add(id);
            }

            return set.Seal(GridPlanner.ID_SET_CAP);
        });
    }

    /// <summary>
    /// The matching links counted by the URL at one end: by target as the grid counts them for its facets and its sorts
    /// (<see cref="Tally"/>), or by the page they're on, which takes a pass over the links of its own, kept per filter set.
    /// </summary>
    private static LinkTally EndTally(GridReadContext context, Filters filters, LinkEnd end) => end == LinkEnd.To
        ? Tally(context, filters)
        : HeldHuge(context, "linksources|" + filters.Key, () => Scan(context, filters, Total(context), ids: false, tally: true, bySource: true).Tally!);

    /// <returns>Null when a running crawl's moment no longer holds (<see cref="Settled"/>).</returns>
    private static LinkGridPage? Read(GridReadContext context, UrlQuery query)
    {
        // Before anything is counted, so that all of it is counted for this moment.
        var moment = Live(context);
        var sort = query.Sort.Count == 0 ? null : query.Sort[0];
        var byTarget = sort is not null && LinkColumns.Own(sort.Column) is null;
        var filters = Compile(context, query.Filters, query.Search, byTarget ? sort : null);
        var total = Total(context);
        var matching = Matching(context, filters, total);
        var descending = sort?.Descending == true;
        var hash = Hash(context, filters, sort);

        // The sort value a cursor carries is the target's, read again when it comes back, so a target that has changed is refused.
        var target = byTarget ? TargetSort(context, sort!) : null;
        object?[]? Keys(long linkId) => Target(context, linkId) is { } toId
            ? target is null ? [] : target.Text ? [UrlOf(context, toId)] : GridPlanner.KeysOf(context, target.Query, toId)
            : null;
        var after = query.After is { } cursor ? GridCursor.Decode(cursor, hash, id => Keys(id)) : null;

        bool more;
        long[]? ids;
        if (target is null)
        {
            ids = Page(context, filters, total, matching, descending, query.Offset, query.Limit, after?.Id, out more);
        }
        else
        {
            ids = TargetPage(context, filters, Order(context, filters, target, descending), matching, query.Offset, query.Limit, after?.Id, out more);
        }

        if (ids is null)
        {
            return null;
        }

        var (columns, unavailable) = Shown(context, query.Columns);
        var rows = Values(context, columns, ids);

        // The rows are read from this read's snapshot, the places from the moment: a link the moment counted that's gone
        // since (its page was fetched again) leaves the page short of what the count says it holds.
        if (moment is { Shared: true } && (rows.Count != ids.Length || (after is null && rows.Count != Math.Clamp(matching - query.Offset, 0, query.Limit))))
        {
            return null;
        }

        var next = more && ids.Length > 0 ? GridCursor.Encode(hash, Keys(ids[^1]) ?? [], ids[^1]) : null;
        return new LinkGridPage([.. columns.Select(column => column.Id)], rows, after is null ? query.Offset : null, matching, total, next, context.Summary, unavailable);
    }

    /// <returns>Null when a running crawl's moment no longer holds (<see cref="Settled"/>).</returns>
    private static UrlFacets? Facets(GridReadContext context, UrlFacetQuery query)
    {
        var moment = Live(context);
        var filters = Compile(context, query.Filters, query.Search, sort: null);
        var total = Total(context);
        var tally = Tally(context, filters);

        // Each facet's counts add up to the tally's links. With no filter those are every link of the moment, unless some
        // have gone since it was taken.
        if (moment is { Shared: true } && filters.Conditions.Count == 0 && tally.Links != total)
        {
            return null;
        }

        var asked = (query.Facets.Count == 0 ? FACETS : query.Facets).Distinct(StringComparer.Ordinal).ToList();
        Dictionary<string, IReadOnlyList<FacetBucket>> facets = new(StringComparer.Ordinal);
        foreach (var facet in asked)
        {
            facets[facet] = facet switch
            {
                "kind" => Shapes(tally, LinkColumns.KIND, LinkTally.KindOf, LinkTally.UNKNOWN_KIND),
                "position" => Shapes(tally, LinkColumns.POSITION, LinkTally.PositionOf, LinkTally.UNKNOWN_POSITION),
                "follow" =>
                [
                    .. FOLLOW.Select((value, index) => (value, Count: Sum(tally, LinkTally.FollowOf, index))).Where(bucket => bucket.Count > 0)
                        .Select(bucket => new FacetBucket(bucket.value.Value, bucket.Count, bucket.value.Filter))
                ],
                "to.statusClass" => StatusClasses(context, tally),
                _ => Internal(context, tally)
            };
        }

        return new UrlFacets(filters.Conditions.Count == 0 ? total : tally.Links, total, context.Summary, [], facets);
    }

    // ---- A running crawl's moment.

    /// <summary>
    /// The moment of a running crawl this read answers for (<see cref="LinkMoment"/>); null for a crawl that stands still,
    /// whose counts are kept under its stamp. The moment the last reads answered for is kept for
    /// <see cref="ReadCache.LIVE_FOR"/>, so a running crawl's links are counted at most that often, and this read takes it
    /// too, unless its own snapshot is the older one (it began before the read that took the moment, and can't see all of
    /// the moment's links): then it takes a moment of its own, as a read without a cache does.
    /// </summary>
    private static LinkMoment? Live(GridReadContext context) => context.Stamp is not { Live: true } ? null : context.Once(MOMENT, () =>
    {
        LinkMoment? mine = null;
        var kept = context.Cached(MOMENT, () =>
        {
            var taken = Take(context, shared: true);
            mine = taken;
            return taken;
        });
        if (ReferenceEquals(kept, mine))
        {
            return kept;
        }

        using var command = context.Command("SELECT (SELECT COALESCE(MAX(rowid), 0) FROM links), (SELECT COALESCE(MAX(id), 0) FROM urls)", []);
        using var reader = command.ExecuteReader();
        reader.Read();
        return kept.MaxLinkId <= reader.GetInt64(0) && kept.MaxUrlId <= reader.GetInt64(1) ? kept : Take(context, shared: false);
    });

    /// <summary>The crawl as this read's snapshot has it, as a moment: its links and URLs counted, with their highest ids, in one statement.</summary>
    private static LinkMoment Take(GridReadContext context, bool shared)
    {
        using var command = context.Command(
            "SELECT (SELECT COUNT(*) FROM links), (SELECT COALESCE(MAX(rowid), 0) FROM links), (SELECT COUNT(*) FROM urls), (SELECT COALESCE(MAX(id), 0) FROM urls)", []);
        using var reader = command.ExecuteReader();
        reader.Read();
        return new LinkMoment(LinkMoment.NextId(), reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), shared);
    }

    /// <summary>
    /// A read whose answer is of one moment. A read of a running crawl that finds the moment it took doesn't hold any more
    /// (links it counted are gone: the crawl fetched their page again) answers null, and is run again on a moment of its
    /// own snapshot, which the reads after it take too; should that one not hold either (another read counted for it after
    /// more links went), once more on a moment nothing else counts for, which is this read's snapshot and nothing else.
    /// </summary>
    private static T Settled<T>(GridReadContext context, Func<T?> read) where T : class
    {
        var answer = read();
        for (var again = 0; answer is null; again++)
        {
            var shared = again == 0;
            context.StartOver();
            var moment = Take(context, shared);
            context.Hold(MOMENT, moment);
            if (shared)
            {
                context.Keep(MOMENT, moment);
            }

            answer = read();
        }

        return answer;
    }

    /// <summary>A key as what's counted is kept under it: a running crawl's under its moment, so nothing counted for one moment is read for another.</summary>
    private static string Under(GridReadContext context, string key) => Live(context) is { } moment ? moment.Key + key : key;

    /// <summary>For a running crawl, the condition that keeps a read of the links within its moment; otherwise none.</summary>
    private static string? Within(GridReadContext context) =>
        Live(context) is { } moment ? string.Create(CultureInfo.InvariantCulture, $"l.rowid <= {moment.MaxLinkId}") : null;

    /// <summary>
    /// Something counted, kept between reads (<see cref="GridReadContext.Cached{T}"/>) and held by this one: whatever the
    /// cache lets go of or counts again meanwhile, the read answers with what it read first.
    /// </summary>
    private static T Held<T>(GridReadContext context, string key, Func<T> read) where T : notnull =>
        context.Once(key, () => context.Cached(Under(context, key), read));

    /// <inheritdoc cref="Held{T}"/>
    private static T HeldLarge<T>(GridReadContext context, string key, Func<T> read) where T : notnull =>
        context.Once(key, () => context.CachedLarge(Under(context, key), read));

    /// <inheritdoc cref="Held{T}"/>
    private static T HeldHuge<T>(GridReadContext context, string key, Func<T> read) where T : notnull =>
        context.Once(key, () => context.CachedHuge(Under(context, key), read));

    /// <summary>Holds and keeps something read while reading something else, in place of any kept.</summary>
    private static void HoldHuge<T>(GridReadContext context, string key, T value) where T : notnull
    {
        context.Hold(key, value);
        context.KeepHuge(Under(context, key), value);
    }

    // ---- The query, checked.

    internal static Dictionary<string, List<string>> Validate(UrlQuery query)
    {
        Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);
        for (var index = 0; index < query.Columns.Count; index++)
        {
            if (query.Columns[index] is null)
            {
                UrlQueryCompiler.Add(errors, $"columns[{index}]", "Give a column's id.");
            }
            else if (LinkColumns.Own(query.Columns[index]) is null && LinkColumns.End(query.Columns[index]) is null)
            {
                UrlQueryCompiler.Add(errors, $"columns[{index}]", $"There's no column called '{query.Columns[index]}'.");
            }
        }

        ValidateFilters(query.Filters, query.Search, errors);
        RefuseEnds(query.LinkEnds, errors);
        if (query.Sort.Count > 1)
        {
            UrlQueryCompiler.Add(errors, "sort", "The links grid sorts by one column.");
        }

        for (var index = 0; index < query.Sort.Count; index++)
        {
            if (query.Sort[index] is null)
            {
                UrlQueryCompiler.Add(errors, $"sort[{index}]", "Give a column to sort by.");
                continue;
            }

            var key = query.Sort[index].Column;
            if (LinkColumns.Own(key) is null && LinkColumns.End(key) is null)
            {
                UrlQueryCompiler.Add(errors, $"sort[{index}].column", $"There's no column called '{key}'.");
            }
            else if (LinkColumns.Own(key) is { Sortable: false } || LinkColumns.End(key) is not (null or (LinkColumns.TO, { Sortable: true })))
            {
                UrlQueryCompiler.Add(errors, $"sort[{index}].column",
                    $"The links grid can't sort by '{key}': it sorts by the link's id, or by a column of the target that the URL grid sorts by. Filter by it instead.");
            }
        }

        UrlQueryCompiler.ValidatePaging(query.Offset, query.Limit, query.After, errors);
        return errors;
    }

    /// <summary>The ends of some links narrow the URL grids: asked of the links grid, which its own filters narrow, it's a field error.</summary>
    private static void RefuseEnds(UrlLinkEnds? ends, Dictionary<string, List<string>> errors)
    {
        if (ends is not null)
        {
            UrlQueryCompiler.Add(errors, "linkEnds", "Only the URL grids take linkEnds: the links grid takes the link filters themselves.");
        }
    }

    private static void ValidateFilters(IReadOnlyList<UrlFilter> filters, string? search, Dictionary<string, List<string>> errors)
    {
        if (filters.Count > UrlQueryCompiler.MAX_FILTERS)
        {
            UrlQueryCompiler.Add(errors, "filters", $"Use at most {UrlQueryCompiler.MAX_FILTERS} filters.");
        }

        for (var index = 0; index < filters.Count; index++)
        {
            var field = $"filters[{index}]";
            if (filters[index] is not { } filter)
            {
                UrlQueryCompiler.Add(errors, field, "Give a filter: a column, an operator and what it takes.");
            }
            else if (LinkColumns.Own(filter.Column) is { } own)
            {
                if (filter.Group is not null)
                {
                    UrlQueryCompiler.Add(errors, $"{field}.group", "Only the check filter takes a group.");
                }

                UrlQueryCompiler.ValidateOperator(filter, own, field, errors);
            }
            else if (LinkColumns.End(filter.Column, filter: true) is var (prefix, _))
            {
                // The URL grid's own checks, its messages naming the column as it was asked for.
                var name = filter.Column[prefix.Length..];
                Dictionary<string, List<string>> found = new(StringComparer.Ordinal);
                UrlQueryCompiler.ValidateFilter(filter with { Column = name }, field, GridScope.AllUrls, found);
                foreach (var (key, messages) in found)
                {
                    messages.ForEach(message => UrlQueryCompiler.Add(errors, key, message.Replace($"'{name}'", $"'{filter.Column}'", StringComparison.Ordinal)));
                }
            }
            else
            {
                UrlQueryCompiler.Add(errors, $"{field}.column", $"There's no column called '{filter.Column}'.");
            }
        }

        if (search is { Length: > UrlQueryCompiler.MAX_TEXT })
        {
            UrlQueryCompiler.Add(errors, "search", $"Search for at most {UrlQueryCompiler.MAX_TEXT:N0} characters.");
        }
    }

    // ---- The filters, compiled.

    /// <summary>One condition on the links: over <c>links l</c>, and over <c>link_summary c</c> when the summary holds what it tests.</summary>
    /// <param name="Key">The condition with its values, to keep counts and sets by.</param>
    /// <param name="Direct">It names links by their id or an end's, which the links' key or an index finds without reading the rest.</param>
    /// <param name="OnTarget">It tests the link's target, which the summary by target holds and the one by block doesn't.</param>
    /// <param name="Indexed">It reads nothing of a link but its id, target and kind, which the links' index by target and kind holds.</param>
    /// <param name="Kind">The one kind of link it asks for, when that's all it does.</param>
    /// <param name="Single">It names one URL as the links' end, whose links are in crawl order on that end's index.</param>
    private sealed record Condition(string Key, string LinkSql, string? SummarySql, IReadOnlyList<KeyValuePair<string, object>> Parameters, bool Direct = false,
        bool OnTarget = false, bool Indexed = false, long? Kind = null, bool Single = false);

    /// <param name="HasSummary">The crawl has a link summary, and so its links counted by block.</param>
    private sealed record Filters(IReadOnlyList<Condition> Conditions, bool HasSummary = false)
    {
        public string Key { get; } = string.Join("&", Conditions.Select(condition => condition.Key));

        /// <summary>The link summary holds everything the conditions test.</summary>
        public bool Summarised { get; } = Conditions.All(condition => condition.SummarySql is not null);

        /// <summary>
        /// The conditions are all on what a link is (none, too), not on where it leads, so the links counted by block answer
        /// them: only when there's a link summary to read (<paramref name="HasSummary"/>).
        /// </summary>
        public bool Shaped { get; } = HasSummary && Conditions.All(condition => condition is { SummarySql: not null, OnTarget: false });

        public bool Direct { get; } = Conditions.Any(condition => condition.Direct);

        /// <summary>
        /// A condition names one URL as the links' end: its links are in crawl order on that end's index, so a walk of them
        /// passes no other link. Several ends' links aren't: their index gives them an end at a time.
        /// </summary>
        public bool OneEnd { get; } = Conditions.Any(condition => condition.Single);

        /// <summary>
        /// Beyond the target, the conditions ask nothing of a link, or one kind of link: each target's matching links are
        /// then on the links' index by target (and kind) in crawl order, where they're counted and read between any two
        /// ids with no link's row.
        /// </summary>
        public bool OnTargetIndex { get; } = Conditions.Where(condition => !condition.OnTarget).ToList() is [] or [{ Kind: not null }];

        public IReadOnlyList<KeyValuePair<string, object>> Parameters { get; } = [.. Conditions.SelectMany(condition => condition.Parameters)];

        /// <summary>" WHERE …", or nothing; <paramref name="and"/> for more conditions before these.</summary>
        /// <param name="targetsGiven">The links read are those of targets already known to pass: the conditions on the target are left out.</param>
        public string Where(string? and = null, bool targetsGiven = false) =>
            Join(Conditions.Where(condition => !(targetsGiven && condition.OnTarget)).Select(condition => condition.LinkSql).Prepend(and).OfType<string>());

        /// <summary>
        /// The conditions read nothing of a link but its id, target and kind: its links to some targets are then read on the
        /// index by target and kind alone, no link's row with them.
        /// </summary>
        public bool Indexed { get; } = Conditions.All(condition => condition.Indexed);

        /// <summary>
        /// Beyond the target, the conditions ask for one kind of link and nothing else: a target's links of that kind are in
        /// crawl order on the index by target and kind.
        /// </summary>
        public long? OneKind { get; } = Conditions.Where(condition => !condition.OnTarget).ToList() is [{ Kind: { } kind }] ? kind : null;

        public string SummaryWhere() => Join(Conditions.Select(condition => condition.SummarySql!));

        /// <summary>The links, read in id order unless a condition names them by id or by an end, which its index then finds.</summary>
        public string From => Direct ? "links l" : "links l NOT INDEXED";

        /// <summary>The links as a walk reads them: in id order, on the index of the one end a condition names when it names one.</summary>
        public string Walked => OneEnd ? "links l" : "links l NOT INDEXED";

        private static string Join(IEnumerable<string> conditions) => string.Join(" AND ", conditions) is { Length: > 0 } all ? " WHERE " + all : string.Empty;
    }

    /// <param name="sort">A sort by the target, which needs the URL summary as a filter on its column would.</param>
    private static Filters Compile(GridReadContext context, IReadOnlyList<UrlFilter> filters, string? search, UrlSort? sort)
    {
        List<Condition> conditions = [];
        Dictionary<string, List<UrlFilter>> ends = new(StringComparer.Ordinal) { [LinkColumns.FROM] = [], [LinkColumns.TO] = [] };
        for (var index = 0; index < filters.Count; index++)
        {
            var filter = filters[index];
            if (LinkColumns.Own(filter.Column) is { } own)
            {
                conditions.Add(Own(context, filter, own, index));
            }
            else
            {
                var prefix = LinkColumns.End(filter.Column, filter: true)!.Value.Prefix;
                ends[prefix].Add(filter with { Column = filter.Column[prefix.Length..] });
            }
        }

        var scope = GridScope.AllUrls with { TargetsStored = context.HasIssueTargets };
        if (!context.Summary.Available)
        {
            UrlSort[] targetSort = sort is null ? [] : [sort with { Column = sort.Column[LinkColumns.TO.Length..] }];
            List<string> needing =
            [
                .. UrlQueryCompiler.NeedingSummary(ends[LinkColumns.FROM], [], scope, context.Analysis is not null).Select(column => LinkColumns.FROM + column),
                .. UrlQueryCompiler.NeedingSummary(ends[LinkColumns.TO], targetSort, scope, context.Analysis is not null).Select(column => LinkColumns.TO + column)
            ];
            if (needing.Count > 0)
            {
                throw new NeedsAnalysisException(context.Summary.Reason!.Value, needing);
            }
        }

        conditions.AddRange(End(context, LinkColumns.FROM, ends[LinkColumns.FROM], scope));
        conditions.AddRange(End(context, LinkColumns.TO, ends[LinkColumns.TO], scope));
        if (!string.IsNullOrEmpty(search))
        {
            conditions.Add(Search(context, search));
        }

        return new Filters(conditions, context.LinkSummary is not null && UseBlocks);
    }

    /// <summary>A filter on one of a link's own columns, compiled as the URL grid compiles a filter on a column of its row.</summary>
    private static Condition Own(GridReadContext context, UrlFilter filter, UrlColumnDefinition column, int index)
    {
        (string Sql, List<KeyValuePair<string, object>> Parameters) Compiled(string sql)
        {
            List<KeyValuePair<string, object>> parameters = [];
            string Parameter(object value)
            {
                var name = string.Create(CultureInfo.InvariantCulture, $"$l{index}_{parameters.Count}");
                parameters.Add(new(name, value));
                return name;
            }

            return (UrlQueryCompiler.Inline(filter, column, sql, null, indexed: false, Parameter, parameters).Sql, parameters);
        }

        var onLinks = column.CrawlSql!;
        if (onLinks.Contains(LinkColumns.SOURCE_NOFOLLOW, StringComparison.Ordinal))
        {
            onLinks = onLinks.Replace(LinkColumns.SOURCE_NOFOLLOW, $"{ReaderFunctions.IN_SET}({Slot(context, NofollowPages(context))}, l.from_id)", StringComparison.Ordinal);
        }

        // Whether a link has text at all is in the summary; what the text is isn't.
        var onSummary = column == LinkColumns.TEXT && filter.Operator is FilterOperator.IsEmpty or FilterOperator.IsNotEmpty ? LinkColumns.HAS_TEXT : column.SummarySql;
        var (linkSql, parameters) = Compiled(onLinks);
        // By what the filter says, not its SQL: that names sets by their place in this read.
        var key = string.Create(CultureInfo.InvariantCulture,
            $"{column.Id} {filter.Operator} {UrlQueryCompiler.Describe(filter.Value)} [{string.Join(",", (filter.Values ?? []).Select(value => UrlQueryCompiler.Describe(value)))}] {filter.Min}..{filter.Max}");
        long? kind = column == LinkColumns.KIND && filter is { Operator: FilterOperator.In, Values: [{ Text: { } name }] } ? column.Values.ToList().IndexOf(name) : null;
        return new Condition(key, linkSql, onSummary is null ? null : Compiled(onSummary).Sql, parameters, Direct: column == LinkColumns.ID,
            Indexed: column == LinkColumns.ID || column == LinkColumns.KIND, Kind: kind);
    }

    /// <summary>
    /// The filters on one end of a link, as conditions on that end's URL id. A filter on the end's id names its URLs
    /// outright; the rest are run as a URL grid filter set, by the URL grid's compiler, into the set of URL ids they match
    /// (kept, per filter set), which each link's end is tested against: a few ids as a list, so the links' index on that end
    /// finds their links.
    /// </summary>
    private static IEnumerable<Condition> End(GridReadContext context, string prefix, List<UrlFilter> filters, GridScope scope)
    {
        var end = prefix == LinkColumns.TO ? "to_id" : "from_id";
        var inSummary = prefix == LinkColumns.TO;
        List<UrlFilter> rest = [];
        foreach (var filter in filters)
        {
            if (filter is { Column: "id", Operator: FilterOperator.Eq or FilterOperator.In })
            {
                List<long> ids = [.. (filter.Operator == FilterOperator.Eq ? [filter.Value!.Value] : filter.Values!).Select(value => value.Number!.Value)
                    .Where(number => Math.Floor(number) == number && number is >= long.MinValue and <= long.MaxValue).Select(number => (long)number).Distinct().Order()];
                var list = ids.Count == 0 ? "NULL" : string.Join(", ", ids.Select(id => id.ToString(CultureInfo.InvariantCulture)));
                yield return new Condition($"{prefix}id in {list}", $"l.{end} IN ({list})", inSummary ? $"c.{end} IN ({list})" : null, [], Direct: true, OnTarget: inSummary,
                    Indexed: inSummary, Single: ids.Count == 1);
            }
            else
            {
                rest.Add(filter);
            }
        }

        if (rest.Count == 0)
        {
            yield break;
        }

        var compiled = GridPlanner.Materialize(context, UrlQueryCompiler.Compile(new UrlQuery { Filters = rest }, context.Mode, scope, context.Reason));
        var key = prefix + string.Join("&", compiled.All.Select(predicate => predicate.Key));
        var set = HeldLarge(context, "linkend|" + key, () =>
        {
            if (compiled.Predicates is [{ Matched: { } only }])
            {
                return only;
            }

            var matched = UrlIds(context);
            var slot = context.Use(matched);
            using var command = context.Command(
                GridPlanner.Over(context, compiled, $"COUNT({ReaderFunctions.ADD_TO_SET}({slot.ToString(CultureInfo.InvariantCulture)}, {GridPlanner.Id(compiled)}))", groupBy: 0),
                compiled.All.SelectMany(predicate => predicate.Parameters).ToList());
            command.ExecuteScalar();
            return matched.Seal(GridPlanner.ID_SET_CAP);
        });

        // A page's links are stored together, so the links of a twentieth of the URLs are read through the index by source
        // sooner than every link is tested; a target's are anywhere, so only a few targets are listed.
        var listed = inSummary ? set.Count <= ListedEnds : ListedEnds > 0 && set.Count * 20 <= UrlCount(context);
        if (listed && set is { Count: 1, Json: { } one })
        {
            // One URL, named outright: a walk of its links follows the end's index, in crawl order.
            var id = one[1..^1];
            yield return new Condition(key, $"l.{end} IN ({id})", inSummary ? $"c.{end} IN ({id})" : null, [], Direct: true, OnTarget: inSummary, Indexed: inSummary, Single: true);
        }
        else if (listed && set.Json is { } json)
        {
            var name = prefix == LinkColumns.TO ? "$toIds" : "$fromIds";
            yield return new Condition(key, $"l.{end} IN (SELECT value FROM json_each({name}))", inSummary ? $"c.{end} IN (SELECT value FROM json_each({name}))" : null,
                [new(name, json)], Direct: true, OnTarget: inSummary, Indexed: inSummary);
        }
        else
        {
            var test = $"{ReaderFunctions.IN_SET}({Slot(context, set)}, ";
            yield return new Condition(key, $"{test}l.{end})", inSummary ? $"{test}c.{end})" : null, [], OnTarget: inSummary, Indexed: inSummary);
        }
    }

    /// <summary>The link's text, or either end's URL, contains the text: the URLs that do are read into a set first.</summary>
    private static Condition Search(GridReadContext context, string search)
    {
        var pattern = $"%{UrlQueryCompiler.Escape(search)}%";
        var urls = HeldLarge(context, $"linksearch|{UrlQueryCompiler.Describe(pattern)}", () =>
        {
            var matched = UrlIds(context);
            using var command = context.Command(
                $@"SELECT COUNT({ReaderFunctions.ADD_TO_SET}({Slot(context, matched)}, u.id)) FROM urls u WHERE u.url LIKE $q ESCAPE '\'", [new("$q", pattern)]);
            command.ExecuteScalar();
            return matched.Seal(GridPlanner.ID_SET_CAP);
        });
        var slot = Slot(context, urls);
        return new Condition($"search {UrlQueryCompiler.Describe(pattern)}",
            $@"({ReaderFunctions.IN_SET}({slot}, l.from_id) OR {ReaderFunctions.IN_SET}({slot}, l.to_id) OR l.text LIKE $q ESCAPE '\')", null, [new("$q", pattern)]);
    }

    private static string Slot(GridReadContext context, UrlIdSet set) => context.Use(set).ToString(CultureInfo.InvariantCulture);

    /// <summary>The pages whose robots directives say nofollow: none of their hyperlinks are followed.</summary>
    private static UrlIdSet NofollowPages(GridReadContext context) => HeldLarge(context, "links|nofollow pages", () =>
    {
        var pages = UrlIds(context);
        using var command = context.Command($"SELECT COUNT({ReaderFunctions.ADD_TO_SET}({Slot(context, pages)}, r.url_id)) FROM responses r WHERE r.is_nofollow <> 0", []);
        command.ExecuteScalar();
        return pages.Seal(GridPlanner.ID_SET_CAP);
    });

    /// <summary>
    /// An empty set for this crawl's URL ids: a bit an id, unless the ids run far past the URLs there are (a crafted
    /// file's), which are then kept one by one, as a tally counts them together: what a file's highest id is can't make
    /// what a links read keeps large.
    /// </summary>
    private static UrlIdSet UrlIds(GridReadContext context) => new(LargestUrlId(context) <= 4 * UrlCount(context) + 4_096 ? LargestUrlId(context) : -1);

    private static long LargestUrlId(GridReadContext context) =>
        Live(context)?.MaxUrlId ?? Held(context, "urls|largest", () => context.Scalar("SELECT COALESCE(MAX(id), 0) FROM urls", []));

    private static long UrlCount(GridReadContext context) => Live(context)?.Urls ?? Held(context, "urls|count", () => context.Scalar("SELECT COUNT(*) FROM urls", []));

    // ---- Counts.

    private static long Total(GridReadContext context) =>
        Live(context)?.Links ?? context.LinkSummary?.Links ?? Held(context, "links|total", () => context.Scalar("SELECT COUNT(*) FROM links", []));

    private static long LargestLinkId(GridReadContext context) =>
        Live(context)?.MaxLinkId ?? context.LinkSummary?.MaxLinkId ?? Held(context, "links|largest", () => context.Scalar("SELECT COALESCE(MAX(rowid), 0) FROM links", []));

    private static long Matching(GridReadContext context, Filters filters, long total) =>
        filters.Conditions.Count == 0 ? total
        : filters.Shaped ? BlocksOf(context, filters).Through.LastOrDefault()
        : filters.Summarised && context.LinkSummary is not null ? Tally(context, filters).Links
        : Ids(context, filters, total).Count;

    /// <summary>
    /// What one pass over the links gives for a filter set the link summary can't answer (there's none, or the filters test
    /// what it doesn't hold): the matching links' ids and their tally. They're kept as one, so the count, the pages, the
    /// facets and a sort by the target are all of the same pass, whatever else is counted again meanwhile. Without a
    /// filter there are no ids: every link matches.
    /// </summary>
    private sealed record Pass(LinkIdSet? Ids, LinkTally Tally);

    private static Pass Passed(GridReadContext context, Filters filters) => HeldHuge(context, "linkpass|" + filters.Key, () =>
    {
        var (ids, tally) = Scan(context, filters, Total(context), ids: filters.Conditions.Count > 0, tally: true);
        return new Pass(ids, tally!);
    });

    /// <summary>
    /// The matching links counted by shape and by target, kept per filter set: from the link summary's rows when it holds
    /// everything the filters test, otherwise from the links, in the pass that also keeps their ids.
    /// </summary>
    private static LinkTally Tally(GridReadContext context, Filters filters) => context.LinkSummary is null || !filters.Summarised
        ? Passed(context, filters).Tally
        : HeldHuge(context, "linktally|" + filters.Key, () =>
    {
        var tally = NewTally(context);
        context.LinkTally = tally;
        try
        {
            var sql = $"SELECT COUNT({ReaderFunctions.LINKS}(c.to_id, c.kind, c.position, c.flags, c.source_nofollow, c.links)) FROM link_summary c{filters.SummaryWhere()}";
            Ran?.Invoke(sql);
            using var command = context.Command(sql, filters.Parameters);
            command.ExecuteScalar();
        }
        finally
        {
            context.LinkTally = null;
        }

        return tally;
    });

    private static LinkTally NewTally(GridReadContext context) => new(LargestUrlId(context), UrlCount(context));

    /// <summary>
    /// The ids of the matching links, kept per filter set. With the link summary, which says which targets they lead to:
    /// every link but the others, where most links match (<see cref="AllBut"/>); else read through those targets, when
    /// they have few links in all or their index holds all the filters ask (<see cref="Drive"/>); otherwise, and without
    /// the summary, in one pass over the links.
    /// </summary>
    private static LinkIdSet Ids(GridReadContext context, Filters filters, long total) => context.LinkSummary is null || !filters.Summarised
        ? Passed(context, filters).Ids!
        : HeldHuge(context, "linkids|" + filters.Key, () =>
            !filters.Shaped && (AllBut(context, filters, total) ?? Drive(context, filters, total, however: true)) is { } driven
                ? driven
                : Scan(context, filters, total, ids: true, tally: false, indexed: false).Ids!);

    /// <summary>
    /// One pass over the links for a filter set: the matching links' ids, their tally, or both. A condition that names
    /// links by an end is found on that end's index; otherwise every link is read, in order.
    /// </summary>
    /// <param name="indexed">
    /// False to read every link in order whatever the conditions name: the link summary has said the ends they name have
    /// many links, each of which the index would fetch from wherever it's stored.
    /// </param>
    /// <param name="bySource">Counts the links by the page they're on rather than by their target: the tally's targets are then the sources.</param>
    private static (LinkIdSet? Ids, LinkTally? Tally) Scan(GridReadContext context, Filters filters, long total, bool ids, bool tally, bool indexed = true, bool bySource = false)
    {
        var largest = LargestLinkId(context);
        LinkIdSet? NewIds() => ids ? new LinkIdSet(largest, total) : null;
        var nofollow = tally ? NofollowPages(context) : null;

        // What a part's tally is sized by, read here, on the read's own thread: a part runs on a thread and a connection
        // of its own, and never touches the read's context, which isn't made to be shared.
        var (largestUrl, urls) = tally ? (LargestUrlId(context), UrlCount(context)) : (0L, 0L);

        // With nothing to count, the id is all that's passed on.
        var each = tally
            ? $"{ReaderFunctions.LINK}(l.rowid, {(bySource ? "l.from_id" : "l.to_id")}, l.from_id, l.kind, l.position, l.flags)"
            : $"{ReaderFunctions.LINK_ID}(l.rowid)";
        var inOrder = !indexed || !filters.Direct;
        var from = inOrder ? "links l NOT INDEXED" : filters.From;

        // One part of the pass, on one read's connection: the links between two ids (all of them, with no bounds), kept
        // and counted by that read.
        (LinkIdSet? Ids, LinkTally? Tally) Part(GridReadContext read, string? bounds)
        {
            var (found, counted) = (NewIds(), tally ? new LinkTally(largestUrl, urls) : null);
            (read.LinkIds, read.LinkTally, read.NofollowPages) = (found, counted, nofollow);
            try
            {
                var sql = $"SELECT COUNT({each}) FROM {from}{filters.Where(bounds is null ? Within(context) : bounds)}";
                Ran?.Invoke(sql);
                using var command = read.Command(sql, filters.Parameters);
                command.ExecuteScalar();
            }
            finally
            {
                (read.LinkIds, read.LinkTally, read.NofollowPages) = (null, null, null);
            }

            return (found, counted);
        }

        // Every link read in order, of a crawl that stands still, and many of them: split by link id over as many
        // connections as the host has room for, each on a thread of its own. A running crawl's pass stays on the one
        // connection and its one snapshot, which is the moment it answers for.
        using var helpers = inOrder && largest >= SplitFrom && context.Stamp is { Live: false } ? context.BorrowHelpers(PassHelpers) : null;
        if (helpers is not { Count: > 0 })
        {
            Split?.Invoke(1);
            var (found, counted) = Part(context, null);
            return (found?.Seal(), counted);
        }

        var parts = helpers.Count + 1;
        string Bounds(int part) => string.Create(CultureInfo.InvariantCulture, $"{(part == 0 ? string.Empty : $"l.rowid > {largest * part / parts}")}{(part is 0 || part == parts - 1 ? string.Empty : " AND ")}{(part == parts - 1 ? string.Empty : $"l.rowid <= {largest * (part + 1) / parts}")}");

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(context.Token);
        var others = Enumerable.Range(1, helpers.Count).Select(part => Task.Factory.StartNew(() =>
        {
            try
            {
                using var alongside = context.OpenAlongside(stop.Token);
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
        }, stop.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();

        (LinkIdSet? Ids, LinkTally? Tally) mine;
        try
        {
            mine = Part(context, Bounds(0));
        }
        catch
        {
            stop.Cancel();
            Task.WaitAll(others.Select(other => other.ContinueWith(_ => { }, TaskScheduler.Default)).ToArray());
            throw;
        }

        try
        {
            Task.WaitAll(others);
        }
        catch (AggregateException failed)
        {
            // What a part failed with is what the pass fails with: a pattern too complex before the parts stopped for it.
            throw failed.Flatten().InnerExceptions.FirstOrDefault(exception => exception is not OperationCanceledException) ?? failed.InnerExceptions[0];
        }

        if (others.Any(other => other.Result is null))
        {
            // A connection found the file changed under the read: the pass again, on this read's own.
            Split?.Invoke(1);
            var (found, counted) = Part(context, null);
            return (found?.Seal(), counted);
        }

        Split?.Invoke(parts);
        foreach (var other in others.Select(other => other.Result!.Value))
        {
            if (other.Ids is not null)
            {
                mine.Ids!.Merge(other.Ids);
            }

            if (other.Tally is not null)
            {
                mine.Tally!.Merge(other.Tally);
            }
        }

        return (mine.Ids?.Seal(), mine.Tally);
    }

    /// <summary>
    /// A filter set's links read through their targets and kept, when that's the way to read them (<see cref="Drive"/>): a
    /// page anywhere in it is then read from them.
    /// </summary>
    private static LinkIdSet? Driven(GridReadContext context, Filters filters, long total)
    {
        if (Drive(context, filters, total) is not { } driven)
        {
            return null;
        }

        HoldHuge(context, "linkids|" + filters.Key, driven);
        return driven;
    }

    /// <summary>
    /// The matching links read through their targets, on the links' index by target, when those targets are few and have
    /// few links in all: the summary names the targets and says how many links lead to them, so what this reads is known
    /// before it's read. Null when that's many (a nofollow link among a home page's hundred thousand; a filter most
    /// targets pass): the links are then walked, or read in one pass.
    /// </summary>
    /// <param name="however">
    /// However many the targets and their links, when the index by target holds all the conditions ask
    /// (<see cref="OnTargetIndex"/>): each matching link is then an entry of the index read in passing, with no link's row,
    /// a fraction of what the pass over the links costs.
    /// </param>
    private static LinkIdSet? Drive(GridReadContext context, Filters filters, long total, bool however = false)
    {
        if (DriveCap < 0)
        {
            return null;
        }

        var onIndex = however && OnTargetIndex(context, filters);
        if (Targets(context, filters, onIndex ? int.MaxValue : DriveCap) is not { } driven)
        {
            return null;
        }

        // What reading through the targets reads: every link to them, which the summary adds up; or, where their index
        // holds all that's asked, the matching links alone, which the tally has counted.
        var few = DriveRows < 0 ? -1 : Math.Max(DriveRows, total / 32);
        var reads = onIndex
            ? Tally(context, filters).Links
            : Held(context, "linkdrive|" + filters.Key, () => context.Scalar(
                "SELECT COALESCE(SUM(c.links), 0) FROM link_summary c WHERE c.to_id IN (SELECT value FROM json_each($driven))", [new("$driven", driven)]));
        if (reads > few && !onIndex)
        {
            return null;
        }

        // The targets are those the conditions on the target pass, so only the rest are tested; where they read nothing but a
        // link's kind, the index by target and kind answers them without the links' rows.
        var index = filters.Indexed && context.HasLinkKindIndex ? "ix_links_to_kind" : "ix_links_to";
        var from = onIndex ? OnIndex(filters, "$driven") : $"links l INDEXED BY {index}{filters.Where("l.to_id IN (SELECT value FROM json_each($driven))", targetsGiven: true)}";
        if (reads > few)
        {
            // Many: a bit an id, each kept as SQLite reads it.
            var found = new LinkIdSet(LargestLinkId(context), total);
            context.LinkIds = found;
            try
            {
                var each = $"SELECT COUNT({ReaderFunctions.LINK_ID}(l.rowid)) FROM {from}";
                Ran?.Invoke(each);
                using var all = context.Command(each, [.. filters.Parameters, new("$driven", driven)]);
                all.ExecuteScalar();
            }
            finally
            {
                context.LinkIds = null;
            }

            return found.Seal();
        }

        var sql = $"SELECT l.rowid FROM {from}";
        Ran?.Invoke(sql);
        using var command = context.Command(sql, [.. filters.Parameters, new("$driven", driven)]);
        using var reader = command.ExecuteReader();
        List<long> ids = [];
        while (reader.Read())
        {
            ids.Add(reader.GetInt64(0));
        }

        return new LinkIdSet(ids);
    }

    /// <summary>
    /// The matching links of a filter on the target that most links pass (the links to the site's own URLs, the hyperlinks
    /// to URLs that answered 200), read as every link but the others: the fewer links are read on the index by target,
    /// and the set is every id but theirs. The others are every link to a target none of the matching links leads to, and,
    /// where one kind of link is asked for, the links of other kinds to the targets they do lead to. That takes a crawl
    /// with no link id missing, whose ids are then 1 to the number of links; null otherwise, and when the index doesn't
    /// hold all the filters ask, or they match the fewer links.
    /// </summary>
    private static LinkIdSet? AllBut(GridReadContext context, Filters filters, long total)
    {
        if (DriveCap < 0 || !OnTargetIndex(context, filters) || LargestLinkId(context) != total)
        {
            return null;
        }

        var matched = Tally(context, filters);
        if (matched.Links <= total / 2 || matched.ToUnknown > 0)
        {
            return null;
        }

        // Every link, by target, to tell the others from: counted only now that most links are known to match.
        var every = Tally(context, new Filters([], HasSummary: true));
        if (every.ToUnknown > 0 || every.Links != total)
        {
            return null;
        }

        var found = new LinkIdSet(total, total);
        context.LinkIds = found;
        try
        {
            void Read(string from, IEnumerable<int> targets)
            {
                var sql = $"SELECT COUNT({ReaderFunctions.LINK_ID}(l.rowid)) FROM links l INDEXED BY {from}";
                Ran?.Invoke(sql);
                using var command = context.Command(sql, [new("$others", $"[{string.Join(",", targets)}]")]);
                command.ExecuteScalar();
            }

            Read("ix_links_to WHERE l.to_id IN (SELECT value FROM json_each($others))", every.Targets().Where(target => matched.To(target.Target) == 0).Select(target => target.Target));
            if (filters.OneKind is { } kind)
            {
                // The other kinds of link to the targets with matching ones, where they have any: the kinds after this
                // one on the index, and those before it, each a stretch of a target's entries.
                List<int> mixed = [.. matched.Targets().Where(target => every.To(target.Target) > target.Links).Select(target => target.Target)];
                Read(string.Create(CultureInfo.InvariantCulture, $"ix_links_to_kind WHERE l.to_id IN (SELECT value FROM json_each($others)) AND l.kind > {kind}"), mixed);
                if (kind > 0)
                {
                    Read(string.Create(CultureInfo.InvariantCulture, $"ix_links_to_kind WHERE l.to_id IN (SELECT value FROM json_each($others)) AND l.kind < {kind}"), mixed);
                }
            }
        }
        finally
        {
            context.LinkIds = null;
        }

        return found.SealAllBut(total) && found.Count == matched.Links ? found : null;
    }

    /// <summary>
    /// The targets a filter set's links lead to, as a list for the links' index by target: those the link summary counts
    /// matching links to. Null when there are more than <paramref name="most"/>, or links to a target that isn't a URL of the
    /// crawl (a crafted file's).
    /// </summary>
    private static string? Targets(GridReadContext context, Filters filters, int most)
    {
        var tally = Tally(context, filters);
        if (tally.ToUnknown > 0)
        {
            return null;
        }

        List<int> targets = [.. tally.Targets().Take(most == int.MaxValue ? most : most + 1).Select(target => target.Target)];
        return targets.Count > most ? null : $"[{string.Join(",", targets)}]";
    }

    /// <summary>The links' index by target holds all the conditions ask of a link, and the file has the index (by kind too, when a kind is asked).</summary>
    private static bool OnTargetIndex(GridReadContext context, Filters filters) => filters.OnTargetIndex && (filters.OneKind is null || context.HasLinkKindIndex);

    // ---- A page on the links' index by target.

    /// <summary>
    /// The matching links of a filter's targets as the links' index by target (and kind) holds them: each target's in crawl
    /// order, so those between two ids are a short stretch of the index for each target, counted or read without a link's
    /// row, and without the links before them.
    /// </summary>
    private sealed class TargetIndex(GridReadContext context, Filters filters, string targets)
    {
        private readonly string _between = $"{OnIndex(filters, "$targets")} AND l.rowid > $low AND l.rowid <= $high";

        /// <summary>How many of them have an id after <paramref name="low"/>, up to <paramref name="high"/>.</summary>
        public long Count(long low, long high)
        {
            var sql = $"SELECT COUNT(*) FROM {_between}";
            Ran?.Invoke(sql);
            return context.Scalar(sql, Parameters(low, high));
        }

        /// <summary>Those with an id after <paramref name="low"/>, up to <paramref name="high"/>, in id order either way, past the first <paramref name="skip"/>.</summary>
        public List<long> Read(long low, long high, bool descending, long skip, int take)
        {
            var sql = $"SELECT l.rowid FROM {_between} ORDER BY l.rowid {(descending ? "DESC" : "ASC")} LIMIT $take OFFSET $skip";
            Ran?.Invoke(sql);
            using var command = context.Command(sql, [.. Parameters(low, high), new("$take", take), new("$skip", skip)]);
            using var reader = command.ExecuteReader();
            List<long> ids = [];
            while (reader.Read())
            {
                ids.Add(reader.GetInt64(0));
            }

            return ids;
        }

        private List<KeyValuePair<string, object>> Parameters(long low, long high) => [new("$targets", targets), new("$low", low), new("$high", high)];
    }

    /// <summary>
    /// The links to a list of targets as the links' index by target holds them, for filters that ask nothing else of a link
    /// but, at most, one kind (<see cref="Filters.OnTargetIndex"/>): the kind is asked for as the index holds it, so only
    /// that kind's entries are read. The condition as the links grid compiles it reads an unknown kind as none, which no
    /// index answers.
    /// </summary>
    private static string OnIndex(Filters filters, string targets) => filters.OneKind is { } kind
        ? string.Create(CultureInfo.InvariantCulture, $"links l INDEXED BY ix_links_to_kind WHERE l.to_id IN (SELECT value FROM json_each({targets})) AND l.kind = {kind}")
        : $"links l INDEXED BY ix_links_to WHERE l.to_id IN (SELECT value FROM json_each({targets}))";

    /// <summary>The index by target for a filter set whose every condition it holds, leading to few enough targets to list; else null.</summary>
    private static TargetIndex? IndexOf(GridReadContext context, Filters filters) =>
        DriveCap >= 0 && OnTargetIndex(context, filters) && Targets(context, filters, DriveCap) is { } targets ? new TargetIndex(context, filters, targets) : null;

    /// <summary>
    /// A page at a place among the links to the targets a filter leads to, found and read on the links' index by target:
    /// no link is read to be passed over, at any depth, and nothing is kept. The index counts the targets' links between
    /// two ids without reading them, so the id the page starts after is closed in on: first where an even spread of the
    /// links would put the place, counting from the nearer end of what's left, then by halves, until the place is a few
    /// links past an id. The page is then read from the ids after that one. A menu's target has a link on every page, so
    /// the first try lands within a few links of the place; whatever the spread, each try costs the index entries it
    /// counts, and they shrink.
    /// </summary>
    /// <param name="first">The place of the page's first link among the matching ones, from 0.</param>
    /// <returns>Null when the index doesn't hold what the filters ask, or they lead to too many targets to list.</returns>
    private static long[]? Placed(GridReadContext context, Filters filters, long matching, long first, int take)
    {
        if (IndexOf(context, filters) is not { } index)
        {
            return null;
        }

        // Of the matching links, `before` have an id up to `low` and `through` one up to `high`; the page's first is between.
        var largest = LargestLinkId(context);
        var (low, before, high, through) = (0L, 0L, largest, matching);
        for (var tries = 0; first - before > PlaceSlack && high - low > 1; tries++)
        {
            var even = tries < EVEN_TRIES ? (double)(first - before) / Math.Max(1, through - before) : 0.5;
            var id = Math.Clamp(low + (long)((high - low) * even), low + 1, high - 1);
            var upTo = id - low <= high - id ? before + index.Count(low, id) : through - index.Count(id, high);
            if (upTo <= first)
            {
                (low, before) = (id, upTo);
            }
            else
            {
                (high, through) = (id, upTo);
            }
        }

        // As many ids on as the spread found here says hold the page, twice over; more when they turn out not to.
        var skip = first - before;
        var span = Span((skip + take) * 2.0 * (high - low) / Math.Max(1, through - before), largest);
        while (true)
        {
            var end = span >= largest - low ? largest : low + span;
            var ids = index.Read(low, end, descending: false, skip, take);
            if (ids.Count == take || end == largest)
            {
                return [.. ids];
            }

            span = span > largest / 4 ? largest : span * 4;
        }
    }

    /// <summary>
    /// The links that follow a cursor's among those to the targets a filter leads to, read on the links' index by target
    /// (<see cref="Placed"/>): from the ids after it, or before it, as many as their spread says hold them, and more
    /// when they turn out not to.
    /// </summary>
    private static long[]? PlacedAfter(GridReadContext context, Filters filters, long matching, bool descending, long cursor, int take)
    {
        if (IndexOf(context, filters) is not { } index)
        {
            return null;
        }

        var largest = LargestLinkId(context);
        var span = Span(take * 2.0 * largest / Math.Max(1, matching), largest);
        while (true)
        {
            var (low, high) = descending
                ? (span >= cursor - 1 ? 0 : cursor - 1 - span, cursor - 1)
                : (cursor, span >= largest - cursor ? largest : cursor + span);
            var ids = index.Read(low, high, descending, skip: 0, take);
            if (ids.Count == take || (descending ? low == 0 : high >= largest))
            {
                return [.. ids];
            }

            span = span > largest / 4 ? largest : span * 4;
        }
    }

    /// <summary>A stretch of ids to read a page from: about as many as asked, at least a few thousand, at most all there are.</summary>
    private static long Span(double ids, long largest) => (long)Math.Clamp(ids, Math.Min(LEAST_SPAN, Math.Max(1, largest)), Math.Max(1, largest));

    // ---- A page in crawl order.

    /// <summary>
    /// The ids of one page's links, in the order asked: from an offset, or after a cursor's link. How it's read is the
    /// cheapest way the filters allow, each bounded:
    /// <list type="bullet">
    /// <item>no filter, and no link id missing: the ids are the places;</item>
    /// <item>a set of the matching ids kept already, or the only way to know them (a filter the link summary doesn't hold):
    /// the set;</item>
    /// <item>filters on what a link is: the blocks that hold the page, found by their counts (<see cref="BlockWalk"/>);</item>
    /// <item>filters on the target too: through the targets, when they have few links in all (<see cref="Drive"/>); else on
    /// the links' index by target, when it holds all the filters ask and the targets are few enough to list
    /// (<see cref="Placed"/>); else by walking from the nearer end when the page is near one, or at any place when a filter
    /// names one target, whose index the walk follows; else, where most of the links the filters could match pass the one
    /// on the target, from the blocks that hold the page, counted as the blocks' own count but for the others
    /// (<see cref="ButTheOthers"/>); else from the set, read once (<see cref="Ids"/>): as every link but the others when
    /// most links match, through the targets again when their index holds all that's asked, however many they are,
    /// otherwise in one pass over the links.</item>
    /// </list>
    /// </summary>
    private static long[] Page(GridReadContext context, Filters filters, long total, long matching, bool descending, int offset, int limit, long? after, out bool more)
    {
        var unfiltered = filters.Conditions.Count == 0;
        var summarised = filters.Summarised && context.LinkSummary is not null;
        var dense = unfiltered && LargestLinkId(context) == total;
        LinkIdSet Set() => unfiltered ? All(context, total) : Ids(context, filters, total);
        LinkIdSet? Kept() => !dense && (!summarised || context.HasHuge(Under(context, "linkids|" + filters.Key))) ? Set() : null;
        LinkIdSet? Through() => summarised && !unfiltered && !filters.Shaped ? Driven(context, filters, total) : null;

        if (after is { } cursor)
        {
            // Read on from the cursor's link, one more than the page to say whether anything follows it.
            long[] next;
            if (dense)
            {
                var from = descending ? Math.Min(cursor - 1, total) : Math.Max(cursor + 1, 1);
                next = [.. Enumerable.Range(0, limit + 1).Select(place => descending ? from - place : from + place).Where(id => id >= 1 && id <= total)];
            }
            else if ((Kept() ?? Through()) is { } set)
            {
                var before = descending ? set.Rank(cursor - 1) : set.Rank(cursor);
                next = descending ? set.Range(Math.Max(0, before - limit - 1), (int)Math.Min(limit + 1, before)) : set.Range(before, limit + 1);
                if (descending)
                {
                    Array.Reverse(next);
                }
            }
            else
            {
                next = filters.Shaped
                    ? BlockWalk(context, filters, descending, cursor, skip: 0, limit + 1)
                    : PlacedAfter(context, filters, matching, descending, cursor, limit + 1) ?? Walk(context, filters, descending, cursor, skip: 0, limit + 1);
            }

            more = next.Length > limit;
            return more ? next[..limit] : next;
        }

        // In ascending terms, the page is the places first to first + take; a descending page is those from the far end, reversed.
        var take = (int)Math.Max(0, Math.Min(limit, matching - offset));
        var first = descending ? matching - offset - take : offset;
        more = offset + take < matching;
        if (take <= 0)
        {
            return [];
        }

        long[] ids;
        if (dense)
        {
            ids = [.. Enumerable.Range(0, take).Select(place => first + place + 1)];
        }
        else if ((Kept() ?? Through()) is { } set)
        {
            ids = set.Range(first, take);
        }
        else if (filters.Shaped)
        {
            ids = BlockWalk(context, filters, descending: false, after: null, first, take);
        }
        else if (Placed(context, filters, matching, first, take) is { } placed)
        {
            ids = placed;
        }
        else
        {
            // Walked from the nearer end: when the filter names one URL as the links' end, whose index the walk follows, in
            // crawl order; or when, spread evenly, the walk would pass few links, unless an earlier one found them bunched
            // far from that end and took long. Otherwise from the set, read once.
            var fromEnd = matching - first - take;
            var passed = (Math.Min(first, fromEnd) + take) * (double)total / Math.Max(1, matching);
            var slow = Under(context, "linkwalk|slow|" + filters.Key);
            if (filters.OneEnd || (WalkRows >= 0 && passed <= Math.Max(WalkRows, total / 16) && !context.Noted(slow)))
            {
                var clock = Stopwatch.GetTimestamp();
                ids = fromEnd < first ? Walk(context, filters, descending: true, after: null, fromEnd, take) : Walk(context, filters, descending: false, after: null, first, take);
                if (fromEnd < first)
                {
                    Array.Reverse(ids);
                }

                if (!filters.OneEnd && Stopwatch.GetElapsedTime(clock) > SlowWalk)
                {
                    context.Note(slow);
                }
            }
            else if (ButTheOthers(context, filters) is { } blocks)
            {
                ids = BlockWalk(context, filters, descending: false, after: null, first, take, blocks);
            }
            else
            {
                ids = Set().Range(first, take);
            }
        }

        if (descending)
        {
            Array.Reverse(ids);
        }

        return ids;
    }

    /// <summary>
    /// The links walked in id order, either way, testing each: after a cursor's link, or from an end, past
    /// <paramref name="skip"/> matching ones. Every link on the way is read, but when a condition names one URL as the links'
    /// end: that end's index holds its links in this order, and the walk reads only them.
    /// </summary>
    private static long[] Walk(GridReadContext context, Filters filters, bool descending, long? after, long skip, int take)
    {
        var sql = $"""
            SELECT l.rowid FROM {filters.Walked}{filters.Where(after is null ? null : $"l.rowid {(descending ? "<" : ">")} $after")}
            ORDER BY l.rowid {(descending ? "DESC" : "ASC")}
            LIMIT {take.ToString(CultureInfo.InvariantCulture)} OFFSET {skip.ToString(CultureInfo.InvariantCulture)}
            """;
        Ran?.Invoke(sql);
        using var command = context.Command(sql, [.. filters.Parameters, new("$after", after ?? 0)]);
        using var reader = command.ExecuteReader();
        List<long> ids = [];
        while (reader.Read())
        {
            ids.Add(reader.GetInt64(0));
        }

        return [.. ids];
    }

    /// <summary>
    /// The matching links counted by block of consecutive ids, in id order, each with the matching links up to and including
    /// its own: from <c>link_blocks</c>, for filters on what a link is. Kept per filter set; it's a few thousand numbers for
    /// a million pages.
    /// </summary>
    private sealed record Blocks(long[] Ids, long[] Through);

    private static Blocks BlocksOf(GridReadContext context, Filters filters) => HeldLarge(context, "linkblocks|" + filters.Key, () =>
    {
        List<long> ids = [];
        List<long> through = [];
        long links = 0;
        var sql = $"SELECT c.block, SUM(c.links) FROM link_blocks c{filters.SummaryWhere()} GROUP BY c.block ORDER BY c.block";
        Ran?.Invoke(sql);
        using var command = context.Command(sql, filters.Parameters);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            ids.Add(reader.GetInt64(0));
            through.Add(links += reader.GetInt64(1));
        }

        return new Blocks([.. ids], [.. through]);
    });

    /// <summary>
    /// A page under filters on what a link is, read from the blocks that hold it. The blocks' counts say which block a place
    /// falls in, or which blocks follow a cursor's link, so only those blocks' links are read and tested, in order, runs of
    /// neighbouring blocks in one statement: at most a block a link of the page, however few links match and however large
    /// the crawl.
    /// </summary>
    /// <param name="after">The link to read on from, in the direction asked; null to start at a place.</param>
    /// <param name="skip">Ascending, without a cursor: the place to start at among the matching links.</param>
    /// <param name="counted">The matching links by block, when they aren't the filters' own count of <c>link_blocks</c> (<see cref="ButTheOthers"/>).</param>
    private static long[] BlockWalk(GridReadContext context, Filters filters, bool descending, long? after, long skip, int take, Blocks? counted = null)
    {
        var blocks = counted ?? BlocksOf(context, filters);
        var shift = context.LinkSummary!.Value.BlockShift;
        var step = descending ? -1 : 1;
        int index;
        if (after is { } cursor)
        {
            // The block the cursor's link is in, or the nearest one past it in the walk's direction.
            var found = Array.BinarySearch(blocks.Ids, cursor >> shift);
            index = found >= 0 ? found : descending ? ~found - 1 : ~found;
            skip = 0;
        }
        else
        {
            // The block the place falls in: the first whose count up to it passes the place.
            var found = Array.BinarySearch(blocks.Through, skip + 1);
            index = found >= 0 ? found : ~found;
            skip -= index == 0 ? 0 : blocks.Through[index - 1];
        }

        long Links(int block) => blocks.Through[block] - (block == 0 ? 0 : blocks.Through[block - 1]);
        var sql = $"SELECT l.rowid FROM links l NOT INDEXED{filters.Where("l.rowid BETWEEN $low AND $high")} ORDER BY l.rowid {(descending ? "DESC" : "ASC")} LIMIT $take OFFSET $skip";
        Ran?.Invoke(sql);
        List<long> ids = [];
        while (index >= 0 && index < blocks.Ids.Length && ids.Count < take)
        {
            // A run of neighbouring blocks, as far as the page needs: their links are one range of ids.
            var last = index;
            var held = Links(index);
            while (held < skip + take - ids.Count && last + step >= 0 && last + step < blocks.Ids.Length && blocks.Ids[last + step] == blocks.Ids[last] + step)
            {
                last += step;
                held += Links(last);
            }

            var (low, high) = (Math.Min(blocks.Ids[index], blocks.Ids[last]) << shift, ((Math.Max(blocks.Ids[index], blocks.Ids[last]) + 1) << shift) - 1);
            if (after is { } bound)
            {
                (low, high) = descending ? (low, Math.Min(high, bound - 1)) : (Math.Max(low, bound + 1), high);
            }

            using var command = context.Command(sql, [.. filters.Parameters, new("$low", low), new("$high", high), new("$take", take - ids.Count), new("$skip", skip)]);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                ids.Add(reader.GetInt64(0));
            }

            skip = 0;
            index = last + step;
        }

        return [.. ids];
    }

    /// <summary>Kept whether or not the blocks could be counted this way, so it's worked out once per filter set.</summary>
    private sealed record OtherBlocks(Blocks? Blocks);

    /// <summary>
    /// The matching links counted by block for a filter on the target that most of the links it could match pass: the links
    /// to the site's own URLs, the hyperlinks to URLs that answered, the navigation links to the pages a click from the
    /// start. <c>link_blocks</c> counts links by what they are, not by where they lead, so it counts the links the rest of
    /// the filters match (all of them, when the target is all that's asked), block by block; the others among those, which
    /// lead to the targets that fail, are the fewer, and are read through those targets on the links' index. A block's
    /// matching links are the difference, so a page anywhere is read from the blocks that hold it (<see cref="BlockWalk"/>),
    /// as under a filter on what a link is: nothing is read for every matching link, no set of ids is kept, and missing
    /// link ids change nothing, where every id but the others' needs a crawl with none missing (<see cref="AllBut"/>).
    /// <para>
    /// Null when the links aren't counted by block, the fewer links pass, the others are too many to read, or the counts
    /// don't add up to what the link summary says match (a summary that isn't the links').
    /// </para>
    /// </summary>
    private static Blocks? ButTheOthers(GridReadContext context, Filters filters) =>
        !filters.HasSummary || !filters.Summarised || filters.Shaped || DriveCap < 0
            ? null
            : HeldLarge(context, "linkblocks|others|" + filters.Key, () => new OtherBlocks(CountButTheOthers(context, filters))).Blocks;

    private static Blocks? CountButTheOthers(GridReadContext context, Filters filters)
    {
        // What the filters ask of a link apart from its target: those links are counted by block, and by target.
        var shape = new Filters([.. filters.Conditions.Where(condition => !condition.OnTarget)], HasSummary: true);
        var matched = Tally(context, filters);
        var every = Tally(context, shape);
        if (matched.ToUnknown > 0 || every.ToUnknown > 0 || matched.Links * 2 <= every.Links)
        {
            return null;
        }

        // A target passes or fails whole: the others are every link of that shape to a target that fails.
        List<int> failing = [];
        foreach (var (target, links) in every.Targets())
        {
            var passing = matched.To(target);
            if (passing == 0)
            {
                failing.Add(target);
            }
            else if (passing != links)
            {
                return null;
            }
        }

        // Read on the index by target: an entry each where the index holds all that's asked of a link (its kind, or
        // nothing), else the rows of every link to those targets, which the summary adds up before any is read.
        var others = $"[{string.Join(",", failing)}]";
        var total = Total(context);
        var onIndex = shape.Indexed && (shape.Conditions.Count == 0 || context.HasLinkKindIndex);
        var reads = onIndex
            ? every.Links - matched.Links
            : context.Scalar("SELECT COALESCE(SUM(c.links), 0) FROM link_summary c WHERE c.to_id IN (SELECT value FROM json_each($others))", [new("$others", others)]);
        if (reads > Math.Max(Math.Max(DriveRows, 0), total / 8))
        {
            return null;
        }

        var shift = context.LinkSummary!.Value.BlockShift;
        Dictionary<long, long> otherLinks = [];
        if (failing.Count > 0)
        {
            var index = onIndex && shape.Conditions.Count > 0 ? "ix_links_to_kind" : "ix_links_to";
            var sql = $"SELECT l.rowid FROM links l INDEXED BY {index}{shape.Where("l.to_id IN (SELECT value FROM json_each($others))")}";
            Ran?.Invoke(sql);
            using var command = context.Command(sql, [.. shape.Parameters, new("$others", others)]);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                CollectionsMarshal.GetValueRefOrAddDefault(otherLinks, reader.GetInt64(0) >> shift, out _)++;
            }
        }

        List<long> ids = [];
        List<long> through = [];
        long counted = 0;
        var blocks = BlocksOf(context, shape);
        for (var index = 0; index < blocks.Ids.Length; index++)
        {
            var links = blocks.Through[index] - (index == 0 ? 0 : blocks.Through[index - 1]) - otherLinks.GetValueOrDefault(blocks.Ids[index]);
            if (links < 0)
            {
                return null;
            }

            if (links > 0)
            {
                ids.Add(blocks.Ids[index]);
                through.Add(counted += links);
            }
        }

        return counted == matched.Links ? new Blocks([.. ids], [.. through]) : null;
    }

    /// <summary>
    /// Every link's id, for a crawl without a link summary whose ids have gaps (a page fetched again had its links stored
    /// again): where each place is can't be told from the ids, so they're read once.
    /// </summary>
    private static LinkIdSet All(GridReadContext context, long total) =>
        HeldHuge(context, "linkids|", () => Scan(context, new Filters([]), total, ids: true, tally: false).Ids!);

    // ---- A page sorted by the target.

    /// <summary>A sort by a column of the target: the URL grid's own sort key, or the target's URL.</summary>
    private sealed record TargetKey(string Column, bool Text, CompiledQuery Query);

    private static TargetKey TargetSort(GridReadContext context, UrlSort sort)
    {
        var column = sort.Column[LinkColumns.TO.Length..];
        var compiled = UrlQueryCompiler.Compile(new UrlQuery { Sort = [new UrlSort(column, sort.Descending)] }, context.Mode, GridScope.AllUrls, context.Reason);
        return new TargetKey(column, compiled.Sort[0].Column.Type == UrlColumnType.Text, compiled);
    }

    /// <summary>
    /// The filter set's targets in the sort's order, each with the matching links up to it: every URL's sort value is read
    /// once (the URLs in their index's order, for a sort by URL), the targets with links picked out and put in order,
    /// unknown values last and ties by id in the key's direction, as the URL grid sorts.
    /// </summary>
    private static LinkTargetOrder Order(GridReadContext context, Filters filters, TargetKey key, bool descending)
    {
        var tally = Tally(context, filters);
        var name = $"linkorder|{key.Column}|{(descending ? "desc" : "asc")}|{filters.Key}";
        var order = HeldHuge(context, name, () => Ordered(context, tally, key, descending));

        // A running crawl's order is kept apart from the tally it was put in order from, and either can be let go and
        // counted again: the page is read by an order of the very tally its count is from.
        if (Live(context) is not null && !ReferenceEquals(order.Of, tally))
        {
            order = Ordered(context, tally, key, descending);
            HoldHuge(context, name, order);
        }

        return order;
    }

    private static LinkTargetOrder Ordered(GridReadContext context, LinkTally tally, TargetKey key, bool descending)
    {
        List<int> targets;
        if (key.Text)
        {
            var byUrl = HeldLarge(context, "urls|by url", () =>
            {
                List<int> ids = [];
                using var command = context.Command("SELECT id FROM urls ORDER BY url", []);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    ids.Add(reader.GetInt32(0));
                }

                return ids.ToArray();
            });
            targets = [.. (descending ? Enumerable.Reverse(byUrl) : byUrl).Where(id => tally.To(id) > 0)];
        }
        else
        {
            // By value, then by id within a value: both as numbers that sort ascending whichever way the sort goes, unknown
            // values after every known one.
            var rows = GridPlanner.Keys(context, key.Query).Where(row => tally.To(row.Id) > 0).ToList();
            var values = new long[rows.Count];
            var ids = new long[rows.Count];
            for (var index = 0; index < rows.Count; index++)
            {
                values[index] = rows[index].Key is { } value ? descending ? unchecked(-value) : value : long.MaxValue;
                ids[index] = descending ? -rows[index].Id : rows[index].Id;
            }

            Array.Sort(values, ids);
            for (var (start, end) = (0, 0); start < values.Length; start = end)
            {
                for (end = start + 1; end < values.Length && values[end] == values[start]; end++)
                {
                }

                Array.Sort(ids, start, end - start);
            }

            targets = [.. ids.Select(id => (int)Math.Abs(id))];
        }

        var through = new long[targets.Count];
        long links = 0;
        for (var index = 0; index < targets.Count; index++)
        {
            through[index] = links += tally.To(targets[index]);
        }

        return new LinkTargetOrder([.. targets], through, tally);
    }

    /// <summary>
    /// One page of a sort by the target: the page's place found among the targets' counts, then each target's matching links
    /// read in crawl order on the links' index by target, from where the page (or the cursor) starts within it.
    /// </summary>
    /// <returns>Null when a target has fewer links than a running crawl's moment counted (<see cref="Settled"/>).</returns>
    private static long[]? TargetPage(GridReadContext context, Filters filters, LinkTargetOrder order, long matching, int offset, int limit, long? after, out bool more)
    {
        // Where the page starts: a target, and how many of its links come before, or the link they come after.
        int index;
        long skip = 0;
        long? afterId = null;
        if (after is { } cursor)
        {
            index = Target(context, cursor) is { } toId ? Array.IndexOf(order.Targets, (int)toId) : -1;
            if (index < 0)
            {
                throw UrlQueryException.For("after", "The row this cursor stopped at isn't in the crawl any more: start from an offset again.");
            }

            afterId = cursor;
        }
        else
        {
            if (offset >= matching || order.Targets.Length == 0)
            {
                more = false;
                return [];
            }

            index = Array.BinarySearch(order.Through, (long)offset + 1);
            index = index < 0 ? ~index : index;
            while (index > 0 && order.Through[index - 1] > offset)
            {
                index--;
            }

            skip = offset - (index == 0 ? 0 : order.Through[index - 1]);
        }

        // One more than the page, to say whether anything follows it. Where the page starts past half way through a target's
        // links (a menu's target has a link from every page), they're read from their far end.
        // Every target here passes the conditions on the target, so only the rest are tested; a target's links of one kind
        // are in crawl order on the index by target and kind, with no link's row to read.
        var within = Within(context) is { } bound ? " AND " + bound : string.Empty;
        var of = filters.OneKind is { } kind && context.HasLinkKindIndex
            ? string.Create(CultureInfo.InvariantCulture, $"links l INDEXED BY ix_links_to_kind WHERE l.to_id = $target AND l.kind = {kind} AND l.rowid > $after{within}")
            : $"links l INDEXED BY ix_links_to{filters.Where("l.to_id = $target AND l.rowid > $after" + within, targetsGiven: true)}";
        var counted = Live(context) is { Shared: true };
        string Sql(bool reverse) => $"SELECT l.rowid FROM {of} ORDER BY l.rowid {(reverse ? "DESC" : "ASC")} LIMIT $take OFFSET $skip";
        Ran?.Invoke(Sql(false));
        List<long> ids = [];
        for (; index < order.Targets.Length && ids.Count <= limit; index++)
        {
            var links = order.Through[index] - (index == 0 ? 0 : order.Through[index - 1]);
            var take = Math.Min(limit + 1 - ids.Count, links - skip);
            var reverse = afterId is null && skip > links / 2;
            using var command = context.Command(Sql(reverse),
            [
                .. filters.Parameters, new("$target", order.Targets[index]), new("$after", afterId ?? long.MinValue),
                new("$take", afterId is null ? take : limit + 1 - ids.Count), new("$skip", reverse ? links - skip - take : skip)
            ]);
            using var reader = command.ExecuteReader();
            var from = ids.Count;
            while (reader.Read())
            {
                ids.Add(reader.GetInt64(0));
            }

            if (reverse)
            {
                ids.Reverse(from, ids.Count - from);
            }

            // Read from its start (or a place in it), a target has the links the moment's tally counted, unless some are gone.
            if (counted && afterId is null && ids.Count - from < take)
            {
                more = false;
                return null;
            }

            (skip, afterId) = (0, null);
        }

        more = ids.Count > limit;
        return [.. ids.Take(limit)];
    }

    /// <summary>The target of a link, or null when the crawl has no such link.</summary>
    private static long? Target(GridReadContext context, long linkId)
    {
        using var command = context.Command("SELECT to_id FROM links WHERE rowid = $id", [new("$id", linkId)]);
        return command.ExecuteScalar() is long toId ? toId : null;
    }

    private static string? UrlOf(GridReadContext context, long urlId)
    {
        using var command = context.Command("SELECT url FROM urls WHERE id = $id", [new("$id", urlId)]);
        return command.ExecuteScalar() as string;
    }

    /// <summary>The query's filters, search and sort, hashed, so a cursor from another query is refused.</summary>
    private static string Hash(GridReadContext context, Filters filters, UrlSort? sort) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"links\n{context.Mode}\n{filters.Key}\n{sort?.Column} {(sort?.Descending == true ? "desc" : "asc")}")))[..16];

    // ---- The rows.

    /// <summary>One column a page shows: a link's own, or one of an end's, with where its value is among that end's.</summary>
    private sealed record Column(string Id, UrlColumnDefinition? Own, string? Prefix, int Index);

    /// <summary>
    /// The columns a page carries, the link's id first, and those asked for that have no values without a URL summary. Each
    /// end's are the URL grid's, compiled by it.
    /// </summary>
    private static (List<Column> Columns, List<string> Unavailable) Shown(GridReadContext context, IReadOnlyList<string> asked)
    {
        List<string> ids = [.. (asked.Count == 0 ? DEFAULT_COLUMNS : asked).Where(id => id != LinkColumns.ID.Id).Distinct(StringComparer.Ordinal)];
        List<Column> columns = [new(LinkColumns.ID.Id, LinkColumns.ID, null, 0)];
        List<string> unavailable = [];
        foreach (var prefix in new[] { LinkColumns.FROM, LinkColumns.TO })
        {
            var compiled = EndColumns(context, [.. ids.Where(id => id.StartsWith(prefix, StringComparison.Ordinal)).Select(id => id[prefix.Length..])]);
            unavailable.AddRange(compiled.Unavailable.Select(id => prefix + id));
        }

        foreach (var id in ids.Where(id => !unavailable.Contains(id)))
        {
            if (LinkColumns.Own(id) is { } own)
            {
                columns.Add(new Column(id, own, null, 0));
            }
            else
            {
                var prefix = LinkColumns.End(id)!.Value.Prefix;
                var compiled = EndColumns(context, [.. ids.Where(other => other.StartsWith(prefix, StringComparison.Ordinal)).Select(other => other[prefix.Length..])]);
                columns.Add(new Column(id, null, prefix, compiled.Columns.Select(column => column.Id).ToList().IndexOf(id[prefix.Length..])));
            }
        }

        return (columns, unavailable);
    }

    private static CompiledQuery EndColumns(GridReadContext context, IReadOnlyList<string> columns) =>
        UrlQueryCompiler.Compile(new UrlQuery { Columns = columns.Count == 0 ? ["id"] : columns }, context.Mode, GridScope.AllUrls, context.Reason);

    /// <summary>The page's rows, in the order of its ids: each link as stored, with the URL grid's values for each end.</summary>
    private static List<LinkGridRow> Values(GridReadContext context, List<Column> columns, long[] ids)
    {
        if (ids.Length == 0)
        {
            return [];
        }

        Dictionary<long, (long From, long To, long Kind, string? Text, long Position, long Flags, string? Fragment)> links = [];
        using (var command = context.Command(
                   $"SELECT l.rowid, l.from_id, l.to_id, l.kind, l.text, l.position, l.flags, l.fragment FROM links l WHERE l.rowid IN ({string.Join(",", ids)})", []))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                links[reader.GetInt64(0)] = (reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetInt64(5),
                    reader.GetInt64(6), reader.IsDBNull(7) ? null : reader.GetString(7));
            }
        }

        Dictionary<long, object?[]> Ends(string prefix, Func<(long From, long To, long Kind, string? Text, long Position, long Flags, string? Fragment), long> end)
        {
            List<string> shown = [.. columns.Where(column => column.Prefix == prefix).Select(column => column.Id[prefix.Length..])];
            return shown.Count == 0 ? [] : UrlGridReader.Values(context, EndColumns(context, shown), [.. links.Values.Select(end).Distinct()]);
        }

        var from = Ends(LinkColumns.FROM, link => link.From);
        var to = Ends(LinkColumns.TO, link => link.To);
        // Whether each link's page says nofollow, for the pages on this page alone.
        HashSet<long> nofollow = [];
        if (columns.Any(column => column.Own == LinkColumns.FOLLOWED))
        {
            using var command = context.Command(
                $"SELECT r.url_id FROM responses r WHERE r.url_id IN ({string.Join(",", links.Values.Select(link => link.From).Distinct())}) AND r.is_nofollow <> 0", []);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                nofollow.Add(reader.GetInt64(0));
            }
        }

        List<LinkGridRow> rows = [];
        foreach (var id in ids)
        {
            if (!links.TryGetValue(id, out var link))
            {
                continue;
            }

            var values = new object?[columns.Count];
            for (var index = 0; index < values.Length; index++)
            {
                var column = columns[index];
                values[index] = column.Own is { } own
                    ? LinkColumns.Value(own, id, link.From, link.To, link.Kind, link.Text, link.Position, link.Flags, link.Fragment, nofollow.Contains(link.From))
                    : (column.Prefix == LinkColumns.FROM ? from.GetValueOrDefault(link.From) : to.GetValueOrDefault(link.To))?[column.Index];
            }

            rows.Add(new LinkGridRow(id, values));
        }

        return rows;
    }

    // ---- Facets.

    private static long Sum(LinkTally tally, Func<int, int> part, int value)
    {
        long sum = 0;
        for (var shape = 0; shape < LinkTally.SHAPES; shape++)
        {
            if (part(shape) == value)
            {
                sum += tally.ByShape[shape];
            }
        }

        return sum;
    }

    /// <summary>A facet on an enum of a link's own: each value with links, in order, and those this engine has no name for last, as unknown.</summary>
    private static List<FacetBucket> Shapes(LinkTally tally, UrlColumnDefinition column, Func<int, int> part, int unknown)
    {
        List<FacetBucket> buckets =
        [
            .. column.Values.Select((name, value) => (name, Count: Sum(tally, part, value))).Where(bucket => bucket.Count > 0)
                .Select(bucket => new FacetBucket(bucket.name, bucket.Count, new UrlFilter(column.Id, FilterOperator.In) { Values = [FilterValue.Of(bucket.name)] }))
        ];
        if (Sum(tally, part, unknown) is > 0 and var count)
        {
            buckets.Add(new FacetBucket(null, count, new UrlFilter(column.Id, FilterOperator.IsEmpty)));
        }

        return buckets;
    }

    /// <summary>
    /// What the target's facets need of every URL, read once per crawl: its status class and whether it's the site's own,
    /// by the URL grid's own columns, a byte a URL. As many as a tally counts one by one, both sized by the same count of
    /// the crawl's URLs: a running crawl's by its moment's, so a tally's every target has its facts.
    /// </summary>
    private static byte[] UrlFacts(GridReadContext context) => HeldLarge(context, "links|url facts", () =>
    {
        var none = UrlQueryCompiler.Compile(new UrlQuery(), context.Mode, GridScope.AllUrls, context.Reason);
        var statusClass = UrlColumns.STATUS_CLASS.Sql(context.Mode)!.Replace(UrlColumns.REASON, context.Reason, StringComparison.Ordinal);
        var isInternal = UrlColumns.Find("isInternal", issueColumns: false)!.Sql(context.Mode)!;
        var packed = $"(COALESCE({statusClass}, {NO_STATUS_CLASS}) * 2 + ({isInternal} <> 0))";
        context.Kept.Clear();
        using (var command = context.Command(GridPlanner.Over(context, none, $"COUNT({ReaderFunctions.KEEP}({packed}, {GridPlanner.Id(none)}))", groupBy: 0, packed), []))
        {
            command.ExecuteScalar();
        }

        // A target the crawl has no URL for (a crafted file's) is of no class, and not the site's.
        var limit = NewTally(context).TargetLimit;
        var facts = new byte[limit];
        Array.Fill(facts, (byte)(NO_STATUS_CLASS * 2));
        foreach (var (key, id) in context.Kept)
        {
            if (id > 0 && id < limit)
            {
                facts[id] = (byte)(key ?? NO_STATUS_CLASS * 2);
            }
        }

        context.Kept.Clear();
        return facts;
    });

    private static List<FacetBucket> StatusClasses(GridReadContext context, LinkTally tally)
    {
        var facts = UrlFacts(context);
        var counts = new long[NO_STATUS_CLASS + 1];
        foreach (var (target, links) in tally.Targets())
        {
            counts[Math.Min(NO_STATUS_CLASS, facts[target] >> 1)] += links;
        }

        var column = LinkColumns.TO + UrlColumns.STATUS_CLASS.Id;
        List<FacetBucket> buckets =
        [
            .. UrlColumns.STATUS_CLASSES.Select((name, value) => (name, Count: counts[value])).Where(bucket => bucket.Count > 0)
                .Select(bucket => new FacetBucket(bucket.name, bucket.Count, new UrlFilter(column, FilterOperator.In) { Values = [FilterValue.Of(bucket.name)] }))
        ];
        if (counts[NO_STATUS_CLASS] > 0)
        {
            buckets.Add(new FacetBucket(null, counts[NO_STATUS_CLASS], new UrlFilter(column, FilterOperator.IsEmpty)));
        }

        return buckets;
    }

    private static List<FacetBucket> Internal(GridReadContext context, LinkTally tally)
    {
        var facts = UrlFacts(context);
        var counts = new long[2];
        foreach (var (target, links) in tally.Targets())
        {
            counts[facts[target] & 1] += links;
        }

        return
        [
            .. new[] { true, false }.Select(value => (value, Count: counts[value ? 1 : 0])).Where(bucket => bucket.Count > 0)
                .Select(bucket => new FacetBucket(bucket.value, bucket.Count, new UrlFilter(LinkColumns.TO + "isInternal", FilterOperator.Is) { Value = FilterValue.Of(bucket.value) }))
        ];
    }
}
