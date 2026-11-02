using System.Globalization;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// Counts a grid query's rows and reads one page of them, choosing how. The bundled SQLite has no histograms (STAT4), so
/// its planner can't tell a rare value from a common one: it would walk a sort's index through a million rows to find the
/// 400 with status 500, or sort 900,000 pages it could have walked in order. So each condition that could drive the query
/// is counted first (up to a cap, and kept), and one of three plans runs, each with a bounded worst case:
/// <list type="bullet">
/// <item>a rare condition (under <see cref="PROBE_CAP"/> rows, and under a tenth of the URLs) drives through its index, or
/// its URL ids, and what it finds is sorted: at most that many rows read, however the filter and the sort correlate;</item>
/// <item>otherwise the sort's index is walked, each row tested: at most the table, without a sort;</item>
/// <item>otherwise (a sort without an index) the narrow summary table is scanned and sorted.</item>
/// </list>
/// The others are kept off their indexes with a unary plus (<c>+s.status_code</c>), which is what makes the choice stick.
/// A page past half way is read from the other end in reverse, so the last page costs what the first does. A condition
/// on the crawl's text (a title, a search) is read once into a set of URL ids, kept while it's small.
/// </summary>
internal static class GridPlanner
{
    /// <summary>A condition's rows are counted up to this many: past it, the condition is common.</summary>
    public const int PROBE_CAP = 50_000;

    /// <summary>The most URL ids kept for a condition on the crawl's text; with more, its subquery runs in each read.</summary>
    public const int ID_SET_CAP = 50_000;

    private enum Plan
    {
        Walk,
        Drive,
        Scan
    }

    /// <summary>Tests only: each page query as it runs, with its parameters, so its plan can be asked for.</summary>
    internal static Action<string, IReadOnlyList<KeyValuePair<string, object>>>? Ran { get; set; }

    /// <summary>Every row the query could list: the crawl's URLs, or the check's issues.</summary>
    public static long Total(GridReadContext context, CompiledQuery query) =>
        query.Scope?.KnownCount ?? context.Count("total", () => context.Scalar(
            query.Mode == GridMode.Summary ? "SELECT COUNT(*) FROM url_summary" : "SELECT COUNT(*) FROM urls", []));

    /// <summary>The rows every condition matches, counted through the rarest one's index.</summary>
    public static long Matching(GridReadContext context, CompiledQuery query, long total)
    {
        var predicates = query.All.ToList();
        if (predicates.Count == 0)
        {
            return total;
        }

        if (predicates is [{ KnownCount: { } known }])
        {
            return known;
        }

        return context.Count("matching|" + Keys(predicates), () =>
        {
            var parameters = predicates.SelectMany(predicate => predicate.Parameters);
            if (query.Mode == GridMode.Crawl)
            {
                return context.Scalar($"SELECT COUNT(*) FROM {CrawlFrom(context, query, sorting: false)} WHERE {Conditions(predicates, null, enabled: true)}", parameters);
            }

            // One condition's index, even a common one's, is counted without its table.
            if (predicates is [{ Driving: true } only])
            {
                return context.Scalar($"SELECT COUNT(*) FROM url_summary s WHERE {only.Sql}", parameters);
            }

            var best = Rarest(context, predicates);
            return best is not null
                ? context.Scalar($"SELECT COUNT(*) FROM url_summary s WHERE {Conditions(predicates, best, enabled: false)}", parameters)
                : context.Scalar($"SELECT COUNT(*) FROM url_summary s NOT INDEXED WHERE {Conditions(predicates, null, enabled: false)}", parameters);
        });
    }

    /// <summary>
    /// The query with each condition on the crawl's text read into the URL ids it matches, kept while there are at most
    /// <see cref="ID_SET_CAP"/>: the scan of every title or URL then runs once for the count and every page that follows,
    /// not once per query, and the few ids drive the plan. With more, the condition stays a subquery.
    /// </summary>
    public static CompiledQuery Materialize(GridReadContext context, CompiledQuery query)
    {
        if (query.Mode != GridMode.Summary || query.Predicates.All(predicate => predicate.Ids is null))
        {
            return query;
        }

        var index = 0;
        return query with { Predicates = [.. query.Predicates.Select(predicate => predicate.Ids is null ? predicate : IdSet(context, predicate, index++))] };
    }

    private static GridPredicate IdSet(GridReadContext context, GridPredicate predicate, int index)
    {
        var set = context.CachedLarge("ids|" + predicate.Key, () =>
        {
            HashSet<long> ids = [];
            using var command = context.Command(predicate.Ids!, predicate.Parameters);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                // Reading stops as soon as there are too many: a common text filter costs a part of a scan, not all of it.
                if (ids.Add(reader.GetInt64(0)) && ids.Count > ID_SET_CAP)
                {
                    return new IdSet(null, 0);
                }
            }

            return new IdSet($"[{string.Join(",", ids.Order())}]", ids.Count);
        });

        if (set.Json is null)
        {
            return predicate;
        }

        var name = "$ids" + index.ToString(CultureInfo.InvariantCulture);
        var sql = $"s.url_id IN (SELECT value FROM json_each({name}))";
        return predicate with { Sql = sql, Disabled = "+" + sql, Parameters = [new(name, set.Json)], KnownCount = set.Count };
    }

    /// <summary>
    /// One page of the query's URL ids, with each one's sort key values for the cursor: from <paramref name="offset"/>, or
    /// after <paramref name="after"/>. <paramref name="more"/> says whether rows follow it.
    /// </summary>
    public static List<(long Id, object?[] Keys)> Page(GridReadContext context, CompiledQuery query, long matching, int offset, int limit,
        GridCursorPosition? after, out bool more)
    {
        if (after is null)
        {
            if (offset >= matching)
            {
                more = false;
                return [];
            }

            // Past half way, the same rows read backwards from the far end: walked or sorted, the first rows are the cheap ones.
            var take = (int)Math.Min(limit, matching - offset);
            var reverse = offset > matching / 2;
            var skip = reverse ? matching - offset - take : offset;
            var rows = Run(context, query, Choose(context, query), null, reverse, skip, take);
            if (reverse)
            {
                rows.Reverse();
            }

            more = offset + rows.Count < matching;
            return rows;
        }

        var plan = Choose(context, query);
        List<(long Id, object?[] Keys)> page = [];
        foreach (var section in Sections(query, after, plan == Plan.Walk, RowId(query, plan)))
        {
            page.AddRange(Run(context, query, plan, section, reverse: false, skip: 0, take: limit + 1 - page.Count));
            if (page.Count > limit)
            {
                break;
            }
        }

        more = page.Count > limit;
        return more ? page[..limit] : page;
    }

    /// <summary>
    /// A rare condition drives, as it reads at most <see cref="PROBE_CAP"/> rows wherever they fall in the sort; otherwise
    /// the sort's index is walked, which reads at most the table and never sorts; otherwise the table is scanned and sorted.
    /// Choosing by estimated costs instead would trust a walk to find its rows early, which a filter correlated with the
    /// sort (broken pages have few links to them) defeats.
    /// </summary>
    private static Plan Choose(GridReadContext context, CompiledQuery query)
    {
        if (query.Mode == GridMode.Crawl)
        {
            return Plan.Walk;
        }

        var predicates = query.All.ToList();
        if (predicates.Count > 0 && Rarest(context, predicates) is not null)
        {
            return Plan.Drive;
        }

        return query.Sort.FirstOrDefault() is not { Indexed: false } ? Plan.Walk : Plan.Scan;
    }

    /// <summary>
    /// The driving condition with the fewest rows, if it's rare: fewer than <see cref="PROBE_CAP"/> rows, and under a
    /// tenth of the table (so that on a small crawl a filter most rows match still walks). Each is counted up to the cap
    /// (and the count kept), or its count is already known (a check's rows, an id set's).
    /// </summary>
    private static GridPredicate? Rarest(GridReadContext context, IReadOnlyList<GridPredicate> predicates)
    {
        var total = context.Count("total", () => context.Scalar("SELECT COUNT(*) FROM url_summary", []));
        GridPredicate? best = null;
        var fewest = Math.Min(PROBE_CAP, (total + 9) / 10);
        foreach (var predicate in predicates.Where(predicate => predicate.Driving))
        {
            var rows = predicate.KnownCount ?? context.Count("probe|" + predicate.Key, () =>
                context.Scalar($"SELECT COUNT(*) FROM (SELECT 1 FROM url_summary s WHERE {predicate.Sql} LIMIT {PROBE_CAP})", predicate.Parameters));
            if (rows < fewest)
            {
                best = predicate;
                fewest = rows;
            }
        }

        return best;
    }

    private static List<(long Id, object?[] Keys)> Run(GridReadContext context, CompiledQuery query, Plan plan, (string Condition, List<KeyValuePair<string, object>> Parameters)? section,
        bool reverse, long skip, long take)
    {
        var predicates = query.All.ToList();
        var walk = plan == Plan.Walk;
        var best = plan == Plan.Drive ? Rarest(context, predicates) : null;
        var id = RowId(query, plan);
        var urlsOnly = id == "u.id" && query.Mode == GridMode.Summary;
        var where = Conditions(predicates, best, enabled: query.Mode == GridMode.Crawl);
        if (section is { } extra)
        {
            where = where.Length == 0 ? extra.Condition : $"{where} AND {extra.Condition}";
        }

        var keys = query.Sort.Select(key => walk ? key.Sql : key.Disabled).ToList();
        var lastDescending = query.Sort.Count > 0 && query.Sort[^1].Descending;
        var order = query.Sort.Select((key, index) => $"{keys[index]} {Direction(key.Descending, reverse)} NULLS {(reverse ? "FIRST" : "LAST")}")
            .Append($"{(walk ? id : "+" + id)} {Direction(lastDescending, reverse)}");

        var sql = $"""
            SELECT {string.Join(", ", keys.Prepend(id))}
            FROM {(urlsOnly ? "urls u" : From(context, query, plan))}
            {(where.Length == 0 ? string.Empty : "WHERE " + where)}
            ORDER BY {string.Join(", ", order)}
            LIMIT {take.ToString(CultureInfo.InvariantCulture)} OFFSET {skip.ToString(CultureInfo.InvariantCulture)}
            """;

        var parameters = predicates.SelectMany(predicate => predicate.Parameters).Concat(section?.Parameters ?? []).ToList();
        Ran?.Invoke(sql, parameters);
        using var command = context.Command(sql, parameters);
        using var reader = command.ExecuteReader();
        List<(long Id, object?[] Keys)> rows = [];
        while (reader.Read())
        {
            var values = new object?[query.Sort.Count];
            for (var index = 0; index < values.Length; index++)
            {
                values[index] = reader.IsDBNull(index + 1) ? null : query.Sort[index].Column.Type == UrlColumnType.Text ? reader.GetString(index + 1) : reader.GetInt64(index + 1);
            }

            rows.Add((reader.GetInt64(0), values));
        }

        return rows;
    }

    /// <summary>
    /// Where rows after a cursor are, as conditions, one section after the other: with a nullable first key, the rows with
    /// a value (from the cursor on) and then those without, as unknown values come last. Each section is a range of the
    /// first key's index, so reading on from a cursor costs a page, however deep.
    /// </summary>
    private static List<(string Condition, List<KeyValuePair<string, object>> Parameters)> Sections(CompiledQuery query, GridCursorPosition after, bool enabled,
        string rowId)
    {
        List<KeyValuePair<string, object>> parameters = [];
        string Parameter(object value)
        {
            var name = "$k" + parameters.Count.ToString(CultureInfo.InvariantCulture);
            parameters.Add(new(name, value));
            return name;
        }

        var id = enabled ? rowId : "+" + rowId;
        var keys = query.Sort.Select(key => enabled ? key.Sql : key.Disabled).ToList();
        var idDescending = query.Sort.Count > 0 && query.Sort[^1].Descending;

        string After(int index, bool firstHasValue)
        {
            if (index == keys.Count)
            {
                return $"{id} {(idDescending ? "<" : ">")} {Parameter(after.Id)}";
            }

            var key = query.Sort[index];
            if (after.Values[index] is not { } value)
            {
                return $"({keys[index]} IS NULL AND {After(index + 1, firstHasValue)})";
            }

            var beyond = key.Descending ? "<" : ">";
            List<string> terms = [$"{keys[index]} {beyond} {Parameter(value)}"];
            if (key.Nullable && !(index == 0 && firstHasValue))
            {
                terms.Add($"{keys[index]} IS NULL");
            }

            terms.Add($"({keys[index]} = {Parameter(value)} AND {After(index + 1, firstHasValue)})");
            return $"({string.Join(" OR ", terms)})";
        }

        if (keys.Count == 0)
        {
            return [(After(0, false), parameters)];
        }

        var first = query.Sort[0];
        if (after.Values[0] is { } start)
        {
            // From the cursor on, as a range of the first key's index; with one key, a row value is that range exactly.
            var rangeStart = first.Descending ? "<=" : ">=";
            var range = keys.Count == 1
                ? $"({keys[0]}, {id}) {(first.Descending ? "<" : ">")} ({Parameter(start)}, {Parameter(after.Id)})"
                : $"{keys[0]} {rangeStart} {Parameter(start)} AND {After(0, firstHasValue: true)}";
            var withValues = first.Nullable ? $"{keys[0]} IS NOT NULL AND {range}" : range;
            var sections = new List<(string, List<KeyValuePair<string, object>>)> { (withValues, parameters) };
            if (first.Nullable)
            {
                sections.Add(($"{keys[0]} IS NULL", []));
            }

            return sections;
        }

        return [($"{keys[0]} IS NULL AND {After(1, false)}", parameters)];
    }

    private static string From(GridReadContext context, CompiledQuery query, Plan plan)
    {
        if (query.Mode == GridMode.Crawl)
        {
            return CrawlFrom(context, query, sorting: true);
        }

        var byUrl = query.Sort.Any(key => key.Column == UrlColumns.URL);
        var issues = query.Scope is not null && query.Sort.Any(key => key.Column.IsIssueColumn) ? " JOIN issues i ON i.issue = $s0 AND i.url_id = s.url_id" : string.Empty;
        if (plan == Plan.Walk && query.Sort.FirstOrDefault()?.Column == UrlColumns.URL)
        {
            // The URL index, in order; the summary row looked up for what the conditions test.
            return $"urls u CROSS JOIN url_summary s ON s.url_id = u.id{issues}";
        }

        var summary = plan == Plan.Scan ? "url_summary s NOT INDEXED" : "url_summary s";
        return byUrl ? $"{summary} CROSS JOIN urls u ON u.id = s.url_id{issues}" : summary + issues;
    }

    /// <summary>
    /// A query over the rows the conditions match, read as their count is: for facets, which group them by one column or
    /// sum over them. A common condition's index walked would look up most rows, so then the narrow table is scanned.
    /// </summary>
    /// <param name="groupBy">How many of the selected columns, from the first, to group by: none for sums.</param>
    /// <param name="key">The columns grouped by, which a crawl without a URL summary may need joined.</param>
    public static string Over(GridReadContext context, CompiledQuery query, string select, int groupBy, string? key = null)
    {
        var predicates = query.All.ToList();
        var group = groupBy == 0 ? string.Empty : " GROUP BY " + string.Join(", ", Enumerable.Range(1, groupBy));
        if (query.Mode == GridMode.Crawl)
        {
            var conditions = Conditions(predicates, null, enabled: true);
            return $"SELECT {select} FROM {CrawlFrom(context, query, sorting: false, key)}{(conditions.Length == 0 ? string.Empty : " WHERE " + conditions)}{group}";
        }

        if (predicates.Count == 0)
        {
            return $"SELECT {select} FROM url_summary s{group}";
        }

        var best = Rarest(context, predicates);
        return best is not null
            ? $"SELECT {select} FROM url_summary s WHERE {Conditions(predicates, best, enabled: false)}{group}"
            : $"SELECT {select} FROM url_summary s NOT INDEXED WHERE {Conditions(predicates, null, enabled: false)}{group}";
    }

    /// <summary>The crawl's own tables, as a grid without a URL summary reads them.</summary>
    /// <param name="extra">More SQL that reads them, such as a facet's column.</param>
    public static string CrawlFrom(GridReadContext context, CompiledQuery query, bool sorting, string? extra = null)
    {
        var sql = string.Join(" ", query.All.Select(predicate => predicate.Sql).Concat(sorting ? query.Sort.Select(key => key.Sql) : []).Append(extra ?? string.Empty));
        var effective = context.HasEffectiveIndexability && sql.Contains("e.indexability_reason", StringComparison.Ordinal)
            ? " LEFT JOIN effective_indexability e ON e.url_id = u.id"
            : string.Empty;
        return query.Scope is not null
            ? $"issues i JOIN urls u ON u.id = i.url_id LEFT JOIN responses r ON r.url_id = u.id{effective}"
            : $"urls u LEFT JOIN responses r ON r.url_id = u.id{effective}";
    }

    /// <summary>Every condition, joined: the driving one free to use its index, the rest kept off theirs (unless all are free).</summary>
    private static string Conditions(IReadOnlyList<GridPredicate> predicates, GridPredicate? driving, bool enabled) =>
        string.Join(" AND ", predicates.Select(predicate => enabled || ReferenceEquals(predicate, driving) ? predicate.Sql : predicate.Disabled));

    public static string Id(CompiledQuery query) => query.Mode == GridMode.Summary ? "s.url_id" : "u.id";

    /// <summary>
    /// The row's URL id in a plan's query: every URL by URL walks the URL index alone, which holds the ids, with nothing
    /// to look up in the summary.
    /// </summary>
    private static string RowId(CompiledQuery query, Plan plan) =>
        plan == Plan.Walk && query.Mode == GridMode.Summary && !query.All.Any() && query.Sort is [{ } only] && only.Column == UrlColumns.URL
            ? "u.id"
            : Id(query);

    private static string Keys(IEnumerable<GridPredicate> predicates) => string.Join("&", predicates.Select(predicate => predicate.Key));

    private static string Direction(bool descending, bool reverse) => descending != reverse ? "DESC" : "ASC";
}
