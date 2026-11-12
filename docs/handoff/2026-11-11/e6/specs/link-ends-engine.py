E = 'src/ShoutingIguana.Engine/Storage/'
M = E + 'Models/'

WRITES = [
(M + 'LinkEnd.cs', '''namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One end of a link: the page it's on, or the URL it leads to.</summary>
public enum LinkEnd
{
    /// <summary>The page the link is on: its source.</summary>
    From,

    /// <summary>The URL the link leads to: its target.</summary>
    To
}
'''),
(M + 'UrlLinkEnds.cs', '''namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// A filter of the URL grid by the links grid: the URLs that are one end of at least one link a set of link filters
/// matches. With <see cref="LinkEnd.From"/>, the pages that have such a link on them; with <see cref="LinkEnd.To"/>, the
/// URLs such a link leads to. So a view of the links grid opens its pages, or its targets, in the URL grid.
/// </summary>
/// <param name="Filters">The links grid's filters (<see cref="LinkGridReader.Columns"/>); none for every link.</param>
/// <param name="Search">The links grid's search.</param>
public sealed record UrlLinkEnds(LinkEnd End, IReadOnlyList<UrlFilter> Filters, string? Search = null);
'''),
(M + 'LinkEndQuery.cs', '''namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One page of the URLs at one end of the links a set of link filters matches (<see cref="LinkGridReader.Ends"/>).</summary>
public sealed record LinkEndQuery
{
    public LinkEnd End { get; init; } = LinkEnd.To;

    /// <summary>The links grid's filters; none for every link.</summary>
    public IReadOnlyList<UrlFilter> Filters { get; init; } = [];

    /// <summary>The links grid's search.</summary>
    public string? Search { get; init; }

    public int Offset { get; init; }

    public int Limit { get; init; } = UrlGridReader.DEFAULT_LIMIT;
}
'''),
(M + 'LinkEndRow.cs', '''namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A URL at one end of the links a view matches.</summary>
/// <param name="Links">How many of the view's links have it at that end.</param>
public sealed record LinkEndRow(int Id, string Url, long Links);
'''),
(M + 'LinkEndPage.cs', '''namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// One page of the URLs at one end of the links a view matches, each once, in the order the crawl found the URLs.
/// </summary>
/// <param name="Total">
/// How many URLs there are at that end: the URL grid's <c>matching</c> under the same links as its
/// <see cref="UrlQuery.LinkEnds"/>.
/// </param>
/// <param name="Links">The links the view matches: the links grid's <c>matching</c>.</param>
public sealed record LinkEndPage(LinkEnd End, long Total, long Links, int Offset, IReadOnlyList<LinkEndRow> Rows, SummaryState Summary);
'''),
]

EDITS = [
# --- The URL grid's query takes the filter.
(M + 'UrlQuery.cs',
'''    /// <summary>Text the URL or the title contains, ignoring the case of ASCII letters.</summary>
    public string? Search { get; init; }
''',
'''    /// <summary>Text the URL or the title contains, ignoring the case of ASCII letters.</summary>
    public string? Search { get; init; }

    /// <summary>Only the URLs at one end of the links these link filters match, beside the filters above; null for no such narrowing.</summary>
    public UrlLinkEnds? LinkEnds { get; init; }
'''),
(M + 'UrlFacetQuery.cs',
'''    public string? Search { get; init; }
''',
'''    public string? Search { get; init; }

    /// <summary>Only the URLs at one end of the links these link filters match (<see cref="UrlQuery.LinkEnds"/>).</summary>
    public UrlLinkEnds? LinkEnds { get; init; }
'''),
(M + 'GridPredicate.cs',
'''    /// <summary>Once read: the set of the rows it leaves out, by their own URL id; it matches every other row.</summary>
    public UrlIdSet? Excluded { get; init; }
''',
'''    /// <summary>Once read: the set of the rows it leaves out, by their own URL id; it matches every other row.</summary>
    public UrlIdSet? Excluded { get; init; }

    /// <summary>
    /// The links whose ends it matches, for a condition still to be read from the links grid (<see cref="UrlGridReader"/>
    /// reads it once the query is compiled); null for any other condition, and once it has been.
    /// </summary>
    public UrlLinkEnds? LinkEnds { get; init; }
'''),
(E + 'UrlQueryCompiler.cs',
'''        ValidateFilters(query.Filters, query.Search, scope, errors);
        if (query.Sort.Count > MAX_SORT)
''',
'''        ValidateFilters(query.Filters, query.Search, scope, errors);
        LinkGridReader.ValidateEnds(query.LinkEnds, errors);
        if (query.Sort.Count > MAX_SORT)
'''),
(E + 'UrlQueryCompiler.cs',
'''        var predicates = Filters(query.Filters, query.Search, mode, scope, reason);
        if (scope.SitemapId is { } sitemap)
        {
            predicates.Insert(0, Sitemap(sitemap, mode));
        }
''',
'''        var predicates = Filters(query.Filters, query.Search, mode, scope, reason);
        if (scope.SitemapId is { } sitemap)
        {
            predicates.Insert(0, Sitemap(sitemap, mode));
        }

        if (query.LinkEnds is { } ends)
        {
            predicates.Add(LinkEnds(ends));
        }
'''),
(E + 'UrlQueryCompiler.cs',
'''    /// <summary>The URL or the title contains the text.</summary>
    private static GridPredicate Search(string search, GridMode mode)
''',
'''    /// <summary>
    /// The URLs at one end of the links some link filters match: a condition to be read from the links grid, which gives
    /// the set of those URLs, once the rest is compiled (<see cref="UrlGridReader.WithLinkEnds"/>). Its key is the filters
    /// as asked, so the query's hash, and what's kept for it, tell one set of links from another.
    /// </summary>
    public static GridPredicate LinkEnds(UrlLinkEnds ends)
    {
        var filters = ends.Filters.Select(filter => string.Join(" ", filter.Column, filter.Operator, Describe(filter.Value),
            $"[{string.Join(",", (filter.Values ?? []).Select(value => Describe(value)))}]", Describe(filter.Min), Describe(filter.Max),
            filter.Group is { } group ? $"{Describe(group.Detail)}/{Describe(group.RelatedUrlId)}" : string.Empty));
        return new GridPredicate("1", "1", [], Driving: false, $"linkends|{ends.End}|{string.Join("&", filters)}|{Describe(ends.Search)}") { LinkEnds = ends };
    }

    /// <summary>The URL or the title contains the text.</summary>
    private static GridPredicate Search(string search, GridMode mode)
'''),
(E + 'GridPlanner.cs',
'''    private static string SetKey(GridPredicate predicate) => "ids|" + predicate.Key;
''',
'''    private static string SetKey(GridPredicate predicate) => "ids|" + predicate.Key;

    /// <summary>
    /// A condition as a set of the rows it matches, read by someone else (the URLs at one end of some links), with or
    /// without a URL summary: a few ids drive the query through them where there's a summary to drive, and each row is
    /// otherwise tested against the set. Its count is the set's.
    /// </summary>
    public static GridPredicate InSet(GridReadContext context, GridMode mode, GridPredicate predicate, UrlIdSet set)
    {
        var slot = context.Use(set).ToString(CultureInfo.InvariantCulture);
        var test = $"{ReaderFunctions.IN_SET}({slot}, {(mode == GridMode.Summary ? ROW : "u.id")})";
        if (mode == GridMode.Summary && set.Json is { } json)
        {
            var name = "$ids" + slot;
            return predicate with
            {
                Sql = $"{ROW} IN (SELECT value FROM json_each({name}))", Disabled = test, Parameters = [.. predicate.Parameters, new(name, json)],
                Driving = true, KnownCount = set.Count, Matched = set, LinkEnds = null
            };
        }

        return predicate with { Sql = test, Disabled = test, Driving = false, KnownCount = set.Count, Matched = set, LinkEnds = null };
    }

    /// <summary>The URL summary row's own URL id.</summary>
    private const string ROW = "s.url_id";
'''),
(E + 'UrlGridReader.cs',
'''        UrlQueryCompiler.ValidateFilters(query.Filters, query.Search, GridScope.AllUrls, errors);
        for (var index = 0; index < query.Facets.Count; index++)
''',
'''        UrlQueryCompiler.ValidateFilters(query.Filters, query.Search, GridScope.AllUrls, errors);
        LinkGridReader.ValidateEnds(query.LinkEnds, errors);
        for (var index = 0; index < query.Facets.Count; index++)
'''),
(E + 'UrlGridReader.cs',
'''        var compiled = UrlQueryCompiler.Compile(new UrlQuery { Filters = query.Filters, Search = query.Search }, context.Mode, GridScope.AllUrls, context.Reason);
        compiled = GridPlanner.Materialize(context, compiled);
''',
'''        var compiled = UrlQueryCompiler.Compile(new UrlQuery { Filters = query.Filters, Search = query.Search, LinkEnds = query.LinkEnds }, context.Mode, GridScope.AllUrls, context.Reason);
        compiled = WithLinkEnds(context, GridPlanner.Materialize(context, compiled));
'''),
(E + 'UrlGridReader.cs',
'''        compiled = GridPlanner.Materialize(context, compiled);
        var total = GridPlanner.Total(context, compiled);
        var matching = GridPlanner.Matching(context, compiled, total);
        if (scope.SitemapId is { } sitemap)
''',
'''        compiled = WithLinkEnds(context, GridPlanner.Materialize(context, compiled));
        var total = GridPlanner.Total(context, compiled);
        var matching = GridPlanner.Matching(context, compiled, total);
        if (scope.SitemapId is { } sitemap)
'''),
(E + 'UrlGridReader.cs',
'''    internal static void ThrowIfInvalid(Dictionary<string, List<string>> errors)
''',
'''    /// <summary>
    /// The query with its condition on the ends of some links, if it has one, read: the links grid gives the set of the
    /// URLs at that end of the links its filters match (kept, per set of link filters), which each row is tested against,
    /// with a URL summary or without.
    /// </summary>
    internal static CompiledQuery WithLinkEnds(GridReadContext context, CompiledQuery query) =>
        query.Predicates.All(predicate => predicate.LinkEnds is null)
            ? query
            : query with
            {
                Predicates =
                [
                    .. query.Predicates.Select(predicate => predicate.LinkEnds is { } ends
                        ? GridPlanner.InSet(context, query.Mode, predicate, LinkGridReader.EndIds(context, ends))
                        : predicate)
                ]
            };

    internal static void ThrowIfInvalid(Dictionary<string, List<string>> errors)
'''),
# --- The links grid gives the ends.
(E + 'LinkGridReader.cs',
'''    /// <returns>Null when a running crawl's moment no longer holds (<see cref="Settled"/>).</returns>
    private static LinkGridPage? Read(GridReadContext context, UrlQuery query)
''',
'''    /// <summary>
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
'''),
(E + 'LinkGridReader.cs',
'''    private static (LinkIdSet? Ids, LinkTally? Tally) Scan(GridReadContext context, Filters filters, long total, bool ids, bool tally, bool indexed = true)
    {
        var largest = LargestLinkId(context);
        LinkIdSet? NewIds() => ids ? new LinkIdSet(largest, total) : null;
        var nofollow = tally ? NofollowPages(context) : null;

        // With nothing to count, the id is all that's passed on.
        var each = tally ? $"{ReaderFunctions.LINK}(l.rowid, l.to_id, l.from_id, l.kind, l.position, l.flags)" : $"{ReaderFunctions.LINK_ID}(l.rowid)";
''',
'''    /// <param name="bySource">Counts the links by the page they're on rather than by their target: the tally's targets are then the sources.</param>
    private static (LinkIdSet? Ids, LinkTally? Tally) Scan(GridReadContext context, Filters filters, long total, bool ids, bool tally, bool indexed = true, bool bySource = false)
    {
        var largest = LargestLinkId(context);
        LinkIdSet? NewIds() => ids ? new LinkIdSet(largest, total) : null;
        var nofollow = tally ? NofollowPages(context) : null;

        // With nothing to count, the id is all that's passed on.
        var each = tally
            ? $"{ReaderFunctions.LINK}(l.rowid, {(bySource ? "l.from_id" : "l.to_id")}, l.from_id, l.kind, l.position, l.flags)"
            : $"{ReaderFunctions.LINK_ID}(l.rowid)";
'''),
]
