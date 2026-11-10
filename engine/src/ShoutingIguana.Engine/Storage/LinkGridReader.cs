using System.Diagnostics;
using System.Globalization;
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
    /// A filter whose links lead to at most this many targets has them read through those targets, when the targets have few
    /// links in all (<see cref="DRIVE_ROWS"/>). Tests change it, to read every page each way: below 0, none are.
    /// </summary>
    internal static int DriveCap { get; set; } = 20_000;

    /// <summary>The most links read through a filter's targets to find its own, or a thirty-second of the crawl's if that's more.</summary>
    private const int DRIVE_ROWS = 100_000;

    /// <summary>
    /// A walk that takes longer than this found its links bunched far from the end it started at (a filter on deep targets,
    /// whose links are all on pages crawled late): the filter set's next pages are read from a set of its ids instead.
    /// </summary>
    private static readonly TimeSpan SLOW_WALK = TimeSpan.FromMilliseconds(100);

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

    private static Dictionary<string, List<string>> Validate(UrlQuery query)
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
    private sealed record Condition(string Key, string LinkSql, string? SummarySql, IReadOnlyList<KeyValuePair<string, object>> Parameters, bool Direct = false,
        bool OnTarget = false, bool Indexed = false, long? Kind = null);

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
                    Indexed: inSummary);
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

            var matched = new UrlIdSet(LargestUrlId(context));
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
        if (listed && set.Json is { } json)
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
            var matched = new UrlIdSet(LargestUrlId(context));
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
        var pages = new UrlIdSet(LargestUrlId(context));
        using var command = context.Command($"SELECT COUNT({ReaderFunctions.ADD_TO_SET}({Slot(context, pages)}, r.url_id)) FROM responses r WHERE r.is_nofollow <> 0", []);
        command.ExecuteScalar();
        return pages.Seal(GridPlanner.ID_SET_CAP);
    });

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
    /// The ids of the matching links, kept per filter set: read through the targets of a filter that matches few links,
    /// when the link summary can say which those are, otherwise in one pass over the links.
    /// </summary>
    private static LinkIdSet Ids(GridReadContext context, Filters filters, long total) => context.LinkSummary is null || !filters.Summarised
        ? Passed(context, filters).Ids!
        : HeldHuge(context, "linkids|" + filters.Key, () =>
            !filters.Shaped && Drive(context, filters, total) is { } driven ? driven : Scan(context, filters, total, ids: true, tally: false).Ids!);

    /// <summary>
    /// One pass over the links for a filter set: the matching links' ids, their tally, or both. A condition that names
    /// links by an end is found on that end's index; otherwise every link is read, in order.
    /// </summary>
    private static (LinkIdSet? Ids, LinkTally? Tally) Scan(GridReadContext context, Filters filters, long total, bool ids, bool tally)
    {
        var found = ids ? new LinkIdSet(LargestLinkId(context), total) : null;
        var counted = tally ? NewTally(context) : null;
        (context.LinkIds, context.LinkTally, context.NofollowPages) = (found, counted, tally ? NofollowPages(context) : null);
        try
        {
            var sql = $"SELECT COUNT({ReaderFunctions.LINK}(l.rowid, l.to_id, l.from_id, l.kind, l.position, l.flags)) FROM {filters.From}{filters.Where(Within(context))}";
            Ran?.Invoke(sql);
            using var command = context.Command(sql, filters.Parameters);
            command.ExecuteScalar();
        }
        finally
        {
            (context.LinkIds, context.LinkTally, context.NofollowPages) = (null, null, null);
        }

        return (found?.Seal(), counted);
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
    private static LinkIdSet? Drive(GridReadContext context, Filters filters, long total)
    {
        var tally = Tally(context, filters);
        if (DriveCap < 0 || tally.ToUnknown > 0)
        {
            return null;
        }

        List<int> targets = [.. tally.Targets().Take(DriveCap + 1).Select(target => target.Target)];
        if (targets.Count > DriveCap)
        {
            return null;
        }

        var driven = $"[{string.Join(",", targets)}]";
        var reads = Held(context, "linkdrive|" + filters.Key, () => context.Scalar(
            "SELECT COALESCE(SUM(c.links), 0) FROM link_summary c WHERE c.to_id IN (SELECT value FROM json_each($driven))", [new("$driven", driven)]));
        if (reads > Math.Max(DRIVE_ROWS, total / 32))
        {
            return null;
        }

        // The targets are those the conditions on the target pass, so only the rest are tested; where they read nothing but a
        // link's kind, the index by target and kind answers them without the links' rows.
        var index = filters.Indexed && context.HasLinkKindIndex ? "ix_links_to_kind" : "ix_links_to";
        var sql = $"SELECT l.rowid FROM links l INDEXED BY {index}{filters.Where("l.to_id IN (SELECT value FROM json_each($driven))", targetsGiven: true)}";
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

    // ---- A page in crawl order.

    /// <summary>
    /// The ids of one page's links, in the order asked: from an offset, or after a cursor's link. How it's read is the
    /// cheapest way the filters allow, each bounded:
    /// <list type="bullet">
    /// <item>no filter, and no link id missing: the ids are the places;</item>
    /// <item>a set of the matching ids kept already, or the only way to know them (a filter the link summary doesn't hold):
    /// the set;</item>
    /// <item>filters on what a link is: the blocks that hold the page, found by their counts (<see cref="BlockWalk"/>);</item>
    /// <item>filters on the target too: through the targets, when they have few links in all (<see cref="Drive"/>), else by
    /// walking from the nearer end when the page is near one, else from the set, read once.</item>
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
                    : Walk(context, filters, descending, cursor, skip: 0, limit + 1);
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
        else
        {
            // Walked from the nearer end: when the filter names the links' end, whose index the walk follows; or when, spread
            // evenly, the walk would pass few links, unless an earlier one found them bunched far from that end and took long.
            // Otherwise from the set, read once.
            var fromEnd = matching - first - take;
            var passed = (Math.Min(first, fromEnd) + take) * (double)total / Math.Max(1, matching);
            var slow = Under(context, "linkwalk|slow|" + filters.Key);
            if (filters.Direct || (WalkRows >= 0 && passed <= Math.Max(WalkRows, total / 16) && !context.Noted(slow)))
            {
                var clock = Stopwatch.GetTimestamp();
                ids = fromEnd < first ? Walk(context, filters, descending: true, after: null, fromEnd, take) : Walk(context, filters, descending: false, after: null, first, take);
                if (fromEnd < first)
                {
                    Array.Reverse(ids);
                }

                if (!filters.Direct && Stopwatch.GetElapsedTime(clock) > SLOW_WALK)
                {
                    context.Note(slow);
                }
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

    /// <summary>The links walked in id order, either way, testing each: after a cursor's link, or from an end, past <paramref name="skip"/> matching ones.</summary>
    private static long[] Walk(GridReadContext context, Filters filters, bool descending, long? after, long skip, int take)
    {
        var sql = $"""
            SELECT l.rowid FROM {filters.From}{filters.Where(after is null ? null : $"l.rowid {(descending ? "<" : ">")} $after")}
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
    private static long[] BlockWalk(GridReadContext context, Filters filters, bool descending, long? after, long skip, int take)
    {
        var blocks = BlocksOf(context, filters);
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
