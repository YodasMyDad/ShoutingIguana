using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing;
using ShoutingIguana.Engine.Serialization;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// The URL grid's reads: a page of URLs with the columns a view shows, the facets its filter chips and Overview count, and
/// a URL found by its text. Each read takes one snapshot of the file and holds it for that read only; an analysis or a
/// continue starting in this process stops the reads of its crawl for the moment it switches the file to WAL mode, and
/// they read again after (<see cref="CrawlReadGate"/>). With a URL summary that describes the crawl, the grid reads it; without one (a crawl
/// running, never analysed, or continued since) it reads the crawl's own tables, and refuses filters and sorts on the
/// summary's columns rather than ignoring them (<see cref="NeedsAnalysisException"/>).
/// </summary>
public static class UrlGridReader
{
    public const int DEFAULT_LIMIT = 100;

    public const int MAX_LIMIT = 1_000;

    /// <summary>The facets <see cref="Facets"/> counts, in order.</summary>
    public static readonly IReadOnlyList<string> FACETS = ["statusClass", "statusCode", "indexability", "type", "depth", "responseTime", "inSitemap", "severity"];

    /// <summary>The facets that need the URL summary.</summary>
    private static readonly HashSet<string> SUMMARY_FACETS = ["inSitemap", "severity"];

    /// <summary>Response time buckets, in ms: the lowest value of each, and the highest but for the last.</summary>
    private static readonly (long Min, long? Max)[] RESPONSE_TIMES = [(0, 249), (250, 499), (500, 999), (1_000, 1_999), (2_000, null)];

    /// <summary>
    /// The severity facet's values: URLs with at least one issue of each severity (so a URL counts in each it has), and
    /// those with none; each with the filter that selects them, and the test of its column that tells (<see cref="Has"/>).
    /// </summary>
    private static readonly (string Value, string Column, FilterOperator Operator, double Number, string Test)[] SEVERITIES =
    [
        ("error", "errors", FilterOperator.Gte, 1, " > 0"),
        ("warning", "warnings", FilterOperator.Gte, 1, " > 0"),
        ("notice", "notices", FilterOperator.Gte, 1, " > 0"),
        ("none", "issues", FilterOperator.Eq, 0, " = 0")
    ];

    /// <summary>With a filter, this many facets or more are counted in one pass over the matching rows.</summary>
    private const int ONE_PASS_FACETS = 3;

    /// <summary>Bits for an enum's value in one pass: up to 15 values, and unknown.</summary>
    private const int ENUM_BITS = 4;

    /// <summary>Bits for a status code or a depth in one pass: a depth is under <see cref="Crawling.CrawlDepth.UNLINKED"/>.</summary>
    private const int NUMBER_BITS = 20;

    /// <summary>The URL grid's columns.</summary>
    public static IReadOnlyList<UrlColumnInfo> Columns { get; } = UrlColumns.Describe(issueColumns: false);

    /// <summary>The columns of a check's rows: the URL grid's, and the issue's own.</summary>
    public static IReadOnlyList<UrlColumnInfo> IssueRowColumns { get; } = UrlColumns.Describe(issueColumns: true);

    /// <summary>One page of the URL grid.</summary>
    /// <param name="cache">Where counts are kept between reads, so scrolling doesn't count again; none counts each time.</param>
    /// <param name="cancellationToken">Stops the read within milliseconds, the statement running included.</param>
    /// <exception cref="UrlQueryException">
    /// The query can't be run: the errors say why, by field. A regular expression too complex for the crawl's text is one.
    /// </exception>
    /// <exception cref="NeedsAnalysisException">It filters or sorts on the URL summary, and there's none that describes the crawl.</exception>
    /// <exception cref="InvalidOperationException">The file isn't a crawl, or was written with another schema.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static UrlPage Query(string databasePath, UrlQuery query, ReadCache? cache = null, CancellationToken cancellationToken = default)
    {
        ThrowIfInvalid(UrlQueryCompiler.Validate(query, GridScope.AllUrls));
        return Refusing(query.Filters, () => GridReadContext.Read(databasePath, cache, cancellationToken, context => Read(context, query, GridScope.AllUrls)));
    }

    /// <summary>
    /// One page of the URL grid for one sitemap: the URLs it lists (an index: those of every sitemap under it), with every
    /// column, filter and sort of the URL grid. <see cref="UrlPage.Total"/> is the URLs the sitemap lists, each once. Null
    /// when the crawl has no sitemap with this id.
    /// </summary>
    /// <inheritdoc cref="Query" path="/exception"/>
    public static UrlPage? QuerySitemap(string databasePath, int sitemapId, UrlQuery query, ReadCache? cache = null, CancellationToken cancellationToken = default)
    {
        var scope = GridScope.AllUrls with { SitemapId = sitemapId };
        ThrowIfInvalid(UrlQueryCompiler.Validate(query, scope));
        return Refusing(query.Filters, () => GridReadContext.Read(databasePath, cache, cancellationToken, context =>
            context.Scalar("SELECT COUNT(*) FROM sitemaps WHERE id = $id", [new("$id", sitemapId)]) == 0 ? null : Read(context, query, scope)));
    }

    /// <summary>
    /// The matching URLs counted by the values of each facet, each value with the filter that selects its URLs: for the
    /// filter chips, with a query's filters, and for Overview's breakdowns, with none.
    /// </summary>
    /// <inheritdoc cref="Query" path="/exception"/>
    public static UrlFacets Facets(string databasePath, UrlFacetQuery query, ReadCache? cache = null, CancellationToken cancellationToken = default)
    {
        Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);
        UrlQueryCompiler.ValidateFilters(query.Filters, query.Search, GridScope.AllUrls, errors);
        LinkGridReader.ValidateEnds(query.LinkEnds, errors);
        UrlQueryCompiler.ValidateHidden(query.HiddenChecks, errors);
        for (var index = 0; index < query.Facets.Count; index++)
        {
            if (!FACETS.Contains(query.Facets[index]))
            {
                errors[$"facets[{index}]"] = [$"There's no facet called '{query.Facets[index]}': there are {string.Join(", ", FACETS)}."];
            }
        }

        ThrowIfInvalid(errors);
        return Refusing(query.Filters, () => GridReadContext.Read(databasePath, cache, cancellationToken, context => Facets(context, query)));
    }

    private static UrlFacets Facets(GridReadContext context, UrlFacetQuery query)
    {
        RefuseWithoutSummary(context, query.Filters, [], GridScope.AllUrls);
        var compiled = UrlQueryCompiler.Compile(new UrlQuery { Filters = query.Filters, Search = query.Search, LinkEnds = query.LinkEnds, HiddenChecks = query.HiddenChecks },
            context.Mode, GridScope.AllUrls, context.Reason);
        context.Bind(compiled.Hidden);
        compiled = WithLinkEnds(context, GridPlanner.Materialize(context, compiled));
        var total = GridPlanner.Total(context, compiled);
        var matching = GridPlanner.Matching(context, compiled, total);
        var asked = (query.Facets.Count == 0 ? FACETS : query.Facets).Distinct(StringComparer.Ordinal).ToList();
        var unavailable = context.Mode == GridMode.Summary ? [] : asked.Where(SUMMARY_FACETS.Contains).ToList();
        var wanted = asked.Except(unavailable).ToList();
        // The severity facet counts without the hidden checks: its counts are kept apart from those with none hidden.
        var conditions = string.Join("&", compiled.All.Select(predicate => predicate.Key).Append(compiled.HiddenKey).Where(key => key.Length > 0));
        Dictionary<string, IReadOnlyList<FacetBucket>> facets = new(StringComparer.Ordinal);
        if (matching == 0)
        {
            wanted.ForEach(facet => facets[facet] = []);
        }
        else if ((compiled.All.Any() || context.Mode == GridMode.Crawl) && wanted.Count >= ONE_PASS_FACETS)
        {
            // Filtered, or without a summary (whose own tables have no index for a facet), each facet would be a scan of
            // its own: one pass counts them all.
            var groups = context.Cached($"facets|{string.Join(",", wanted)}|{conditions}", () => OnePass(context, compiled, wanted));
            wanted.ForEach(facet => facets[facet] = Buckets(facet, groups[facet]));
        }
        else
        {
            wanted.ForEach(facet => facets[facet] = Buckets(facet, context.Cached($"facet|{facet}|{conditions}", () => Groups(context, compiled, facet))));
        }

        return new UrlFacets(matching, total, context.Summary, unavailable, facets);
    }

    /// <summary>
    /// The crawl's URL with this text, normalised as the crawl stores URLs (the fragment goes, the scheme and host are
    /// lowercased). A path starting with <c>/</c> is taken on the site's origin (where the start URL's redirects end, then
    /// the start URL's), and a URL without a scheme (<c>www.example.com/a</c>) takes that origin's. A URL on the site that
    /// the crawl doesn't have as asked is looked for on the site's other versions (www or not, http or https), which the
    /// engine treats as one site, and <see cref="UrlLookup.Requested"/> then says what was asked. Null when the crawl has none.
    /// </summary>
    /// <exception cref="UrlQueryException">The text isn't a URL.</exception>
    public static UrlLookup? Lookup(string databasePath, string text, CancellationToken cancellationToken = default)
    {
        // Two lookups by key: nothing to interrupt once it's started.
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = CrawlDatabase.OpenReader(databasePath);
        using var transaction = connection.BeginTransaction(deferred: true);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT start_url FROM crawl WHERE id = 1";
        var start = command.ExecuteScalar() is string startUrl && UrlNormalizer.TryNormalize(startUrl, out var normalized) ? normalized : null;
        if (UrlLookupCandidates.Of(text, start is null ? null : Home(command, start), start) is not var (requested, candidates))
        {
            throw UrlQueryException.For("url", "This isn't a URL: give one, with or without http:// or https://, or a path that starts with /.");
        }

        foreach (var candidate in candidates)
        {
            command.CommandText = "SELECT id, url FROM urls WHERE url = $url";
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$url", candidate);
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                var url = reader.GetString(1);
                return new UrlLookup(reader.GetInt32(0), url, url == requested ? null : requested);
            }
        }

        return null;
    }

    /// <summary>Where the start URL's HTTP redirects end, as many hops as browsers follow: the site's home page.</summary>
    private static string Home(SqliteCommand command, string start)
    {
        var current = start;
        HashSet<string> seen = [start];
        for (var hop = 0; hop < UrlTable.MAX_REDIRECT_HOPS; hop++)
        {
            command.CommandText = $"""
                SELECT t.url FROM urls u JOIN responses r ON r.url_id = u.id JOIN urls t ON t.id = r.redirect_url_id
                WHERE u.url = $url AND r.outcome = {(int)FetchOutcome.Redirect}
                """;
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$url", current);
            if (command.ExecuteScalar() is not string next || !seen.Add(next))
            {
                break;
            }

            current = next;
        }

        return current;
    }

    /// <summary>One page of a grid, the URL grid or a check's rows, through a context already open.</summary>
    internal static UrlPage Read(GridReadContext context, UrlQuery query, GridScope scope)
    {
        scope = scope with { TargetsStored = context.HasIssueTargets };
        RefuseWithoutSummary(context, query.Filters, query.Sort, scope);
        var compiled = UrlQueryCompiler.Compile(query, context.Mode, scope, context.Reason);
        context.Bind(compiled.Hidden);
        if (compiled.Scope is { } own)
        {
            // A check's rows (or one group's) counted from the issues alone, which the scope's index serves.
            var inline = UrlQueryCompiler.Scope(scope, GridMode.Crawl);
            compiled = compiled with
            {
                Scope = own with { KnownCount = context.Count("scope|" + inline.Key, () => context.Scalar($"SELECT COUNT(*) FROM issues i WHERE {inline.Sql}", inline.Parameters)) }
            };
        }

        compiled = WithLinkEnds(context, GridPlanner.Materialize(context, compiled));
        var total = GridPlanner.Total(context, compiled);
        var matching = GridPlanner.Matching(context, compiled, total);
        if (scope.SitemapId is { } sitemap)
        {
            // A sitemap's grid is the URLs it lists. With no other condition those are the rows that match, counted once: two
            // counts of them, each kept for its own while, differ as a running crawl reads the sitemap. Otherwise the first
            // condition's own count, which its set gives with a URL summary.
            total = compiled.All.Count() == 1 ? matching : compiled.Predicates[0].KnownCount ?? context.Count($"sitemap|{sitemap}", () => context.Scalar(
                $"SELECT COUNT(DISTINCT url_id) FROM sitemap_urls WHERE sitemap_id IN ({UrlQueryCompiler.SITEMAPS_UNDER})", [new(UrlQueryCompiler.SITEMAP_PARAMETER, sitemap)]));
        }

        var after = query.After is { } cursor ? GridCursor.Decode(cursor, compiled.Hash, id => GridPlanner.KeysOf(context, compiled, id)) : null;
        var rows = GridPlanner.Page(context, compiled, matching, query.Offset, query.Limit, after, out var more);
        var values = Values(context, compiled, [.. rows.Select(row => row.Id)]);
        var next = more && rows.Count > 0 ? GridCursor.Encode(compiled.Hash, rows[^1].Keys, rows[^1].Id) : null;
        return new UrlPage(
            [.. compiled.Columns.Select(column => column.Id)],
            [.. rows.Select(row => new UrlRow((int)row.Id, values[row.Id]))],
            after is null ? query.Offset : null,
            matching,
            total,
            next,
            context.Summary,
            compiled.Unavailable);
    }

    /// <summary>
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
                        ? GridPlanner.InSet(context, query.Mode, predicate, LinkGridReader.EndIds(context, ends, query.HiddenChecks))
                        : predicate)
                ]
            };

    internal static void ThrowIfInvalid(Dictionary<string, List<string>> errors)
    {
        if (errors.Count > 0)
        {
            throw new UrlQueryException(errors.ToDictionary(error => error.Key, error => (IReadOnlyList<string>)error.Value, StringComparer.Ordinal));
        }
    }

    /// <summary>A read, a regular expression it gave up on (too complex for the crawl's text) refused on the filters with it.</summary>
    internal static T Refusing<T>(IReadOnlyList<UrlFilter> filters, Func<T> read)
    {
        try
        {
            return read();
        }
        catch (RegexTooComplexException exception)
        {
            throw UrlQueryCompiler.TooComplex(filters, exception.Pattern);
        }
    }

    /// <summary>A filter or sort on the URL summary without one is refused; the check filter needs only the last analysis's issues.</summary>
    private static void RefuseWithoutSummary(GridReadContext context, IReadOnlyList<UrlFilter> filters, IReadOnlyList<UrlSort> sort, GridScope scope)
    {
        if (!context.Summary.Available && UrlQueryCompiler.NeedingSummary(filters, sort, scope, hasIssues: context.Analysis is not null) is { Count: > 0 } needing)
        {
            throw new NeedsAnalysisException(context.Summary.Reason!.Value, needing);
        }
    }

    /// <summary>The columns' values of the page's rows, by URL id, in one read.</summary>
    internal static Dictionary<long, object?[]> Values(GridReadContext context, CompiledQuery query, IReadOnlyList<long> ids)
    {
        Dictionary<long, object?[]> values = [];
        if (ids.Count == 0)
        {
            return values;
        }

        var summary = query.Mode == GridMode.Summary;
        var id = GridPlanner.Id(query);
        var selects = query.Columns.Select(column => column.Source == ColumnSource.Details
            ? "d.data"
            : column.Sql(query.Mode)!.Replace(UrlColumns.REASON, context.Reason, StringComparison.Ordinal)).ToList();
        bool Uses(ColumnSource source) => query.Columns.Any(column => column.Source == source);

        var from = summary ? "url_summary s" : "urls u LEFT JOIN responses r ON r.url_id = u.id";
        if (summary && Uses(ColumnSource.Urls))
        {
            from += " LEFT JOIN urls u ON u.id = s.url_id";
        }

        if (summary && Uses(ColumnSource.Responses))
        {
            from += " LEFT JOIN responses r ON r.url_id = s.url_id";
        }

        if (!summary && context.HasEffectiveIndexability && selects.Any(sql => sql.Contains("e.indexability_reason", StringComparison.Ordinal)))
        {
            from += " LEFT JOIN effective_indexability e ON e.url_id = u.id";
        }

        if (Uses(ColumnSource.Issues))
        {
            from += $" LEFT JOIN issues i ON i.issue = $s0 AND i.url_id = {id}";
        }

        if (Uses(ColumnSource.Details))
        {
            from += $" LEFT JOIN response_details d ON d.url_id = {id}";
        }

        var robotsToken = Uses(ColumnSource.Details) ? context.RobotsToken : string.Empty;
        using var command = context.Command(
            $"SELECT {id}, {string.Join(", ", selects)} FROM {from} WHERE {id} IN ({string.Join(",", ids)})",
            query.Scope?.Parameters.Where(parameter => parameter.Key == "$s0") ?? []);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var row = new object?[query.Columns.Count];
            for (var index = 0; index < row.Length; index++)
            {
                row[index] = Value(reader, index + 1, query.Columns[index], robotsToken);
            }

            values[reader.GetInt64(0)] = row;
        }

        return values;
    }

    private static object? Value(SqliteDataReader reader, int ordinal, UrlColumnDefinition column, string robotsToken)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return column.Type switch
        {
            UrlColumnType.Number => reader.GetInt64(ordinal),
            UrlColumnType.Boolean => reader.GetInt64(ordinal) != 0,
            UrlColumnType.Enum => reader.GetInt64(ordinal) is var number && number >= 0 && number < column.Values.Count ? column.Values[(int)number] : null,
            _ when column.Source == ColumnSource.Details => FromDetails(column.Id, (byte[])reader.GetValue(ordinal), robotsToken),
            _ => reader.GetString(ordinal)
        };
    }

    /// <summary>The robots directives a page sent: its robots meta tags' content, or its X-Robots-Tag header, as they were.</summary>
    private static string? FromDetails(string column, byte[] data, string robotsToken)
    {
        ResponseDetails details;
        try
        {
            details = BlobCodec.DecodeDetails(data);
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            // One damaged blob in a shared file blanks its cell, not the page.
            return null;
        }

        IEnumerable<string> values = column == "metaRobots"
            ? (details.Page?.MetaTags ?? []).Where(tag => RobotsMetaParser.AppliesToMetaName(tag.Name, robotsToken)).Select(tag => tag.Content)
            : details.Headers.Where(header => header.Name == "x-robots-tag").Select(header => header.Value);
        return values.ToList() is { Count: > 0 } found ? string.Join("; ", found) : null;
    }

    /// <summary>The column a facet counts URLs by.</summary>
    private static UrlColumnDefinition Column(string facet) => facet switch
    {
        "statusClass" => UrlColumns.STATUS_CLASS,
        "statusCode" => UrlColumns.STATUS_CODE,
        "indexability" => UrlColumns.INDEXABILITY,
        "responseTime" => UrlColumns.Find("responseMs", issueColumns: false)!,
        _ => UrlColumns.Find(facet, issueColumns: false)!
    };

    private static string Key(GridReadContext context, CompiledQuery query, string facet) =>
        Column(facet).Sql(query.Mode)!.Replace(UrlColumns.REASON, context.Reason, StringComparison.Ordinal);

    /// <summary>A severity's test of its issue count, the checks the query hides left out of the count: <c>s.errors &gt; 0</c>.</summary>
    private static string Has(CompiledQuery query, string column, string test) =>
        UrlQueryCompiler.Counting(UrlColumns.Find(column, issueColumns: false)!, query.Hidden).SummarySql + test;

    /// <summary>One facet's values with their counts, in one query; the severities numbered by their place in <see cref="SEVERITIES"/>.</summary>
    private static List<(long? Value, long Count)> Groups(GridReadContext context, CompiledQuery query, string facet)
    {
        var parameters = query.All.SelectMany(predicate => predicate.Parameters).ToList();
        if (facet == "severity")
        {
            var sums = string.Join(", ", SEVERITIES.Select(severity => $"SUM({Has(query, severity.Column, severity.Test)})"));
            using var counted = context.Command(GridPlanner.Over(context, query, sums, groupBy: 0), parameters);
            using var reader = counted.ExecuteReader();
            reader.Read();
            return [.. SEVERITIES.Select((_, index) => ((long?)index, reader.IsDBNull(index) ? 0 : reader.GetInt64(index)))];
        }

        var key = Key(context, query, facet);
        List<(long? Value, long Count)> groups = [];
        using var command = context.Command(GridPlanner.Over(context, query, $"{key}, COUNT(*)", groupBy: 1, key), parameters);
        using var rows = command.ExecuteReader();
        while (rows.Read())
        {
            groups.Add((rows.IsDBNull(0) ? null : rows.GetInt64(0), rows.GetInt64(1)));
        }

        return groups;
    }

    /// <summary>
    /// Several facets' values with their counts, in one pass over the matching rows: filtered, each facet's own query
    /// would scan the rows again. Each row's values are packed into one number, a few bits each, and the rows grouped by
    /// it: sorting a million rows by one number takes half the time of sorting them by a column per value (0.55 s against
    /// 1 s, every facet of 1M URLs). Each facet's counts are summed from the groups. The status class follows from the
    /// status code and whether the URL was fetched, as its column's SQL says.
    /// </summary>
    private static Dictionary<string, List<(long? Value, long Count)>> OnePass(GridReadContext context, CompiledQuery query, IReadOnlyList<string> facets)
    {
        // Each field gives 0 to its largest value, which is unknown: a null, or a value no column has (a crafted file's, which
        // counts as unknown as it does when one facet is counted on its own).
        List<(string Name, string Sql, int Bits)> fields = [];
        List<(string Alias, string Sql)> heavy = [];

        // A bare column is named again (reading it is free). A function or a CASE is named once, below, and every comparison
        // uses that: otherwise si_resource_type runs per comparison and the crawl-mode facets miss their budget.
        string Bind(string sql, string use)
        {
            if (!sql.Contains("CASE", StringComparison.Ordinal) && !sql.Contains("si_", StringComparison.Ordinal))
            {
                return use.Replace("v", sql, StringComparison.Ordinal);
            }

            var alias = "k" + heavy.Count.ToString(CultureInfo.InvariantCulture);
            heavy.Add((alias, sql));
            return use.Replace("v", alias, StringComparison.Ordinal);
        }

        string Small(string facet, int bits, int values) =>
            Bind(Key(context, query, facet), $"CASE WHEN v >= 0 AND v < {Math.Min(values, Largest(bits))} THEN v ELSE {Largest(bits)} END");

        // A number as itself plus one, 0 for null, and the largest for one that can't be packed (negative, or too large):
        // status codes and depths are far smaller than the bits allow.
        string Number(string facet) =>
            Bind(Key(context, query, facet), $"CASE WHEN v IS NULL THEN 0 WHEN v >= 0 AND v < {Largest(NUMBER_BITS) - 1} THEN v + 1 ELSE {Largest(NUMBER_BITS)} END");
        if (facets.Contains("statusClass"))
        {
            fields.Add(("fetched", UrlColumns.Find("fetched", issueColumns: false)!.Sql(query.Mode)!, 1));
        }

        if (facets.Contains("statusClass") || facets.Contains("statusCode"))
        {
            fields.Add(("statusCode", Number("statusCode"), NUMBER_BITS));
        }

        foreach (var facet in facets)
        {
            switch (facet)
            {
                case "indexability" or "type":
                    fields.Add((facet, Small(facet, ENUM_BITS, Column(facet).Values.Count), ENUM_BITS));
                    break;
                case "inSitemap":
                    fields.Add((facet, Small(facet, 2, 2), 2));
                    break;
                case "depth":
                    fields.Add((facet, Number(facet), NUMBER_BITS));
                    break;
                case "responseTime":
                    // The bucket's place in RESPONSE_TIMES, by the lowest values it's past; unknown for null, or below the first.
                    var responseMs = Key(context, query, facet);
                    var buckets = string.Join(" + ", RESPONSE_TIMES.Skip(1).Select(range => $"(v >= {range.Min})"));
                    fields.Add((facet, Bind(responseMs, $"CASE WHEN v >= 0 THEN {buckets} ELSE {Largest(3)} END"), 3));
                    break;
                case "severity":
                    fields.Add((facet, string.Join(" | ", SEVERITIES.Select((severity, index) => $"(({Has(query, severity.Column, severity.Test)}) << {index})")), SEVERITIES.Length));
                    break;
            }
        }

        Dictionary<string, (int Shift, int Bits)> layout = new(StringComparer.Ordinal);
        foreach (var (name, _, bits) in fields)
        {
            layout[name] = (layout.Values.Sum(field => field.Bits), bits);
        }

        // Counted as they're read (si_tally), rather than grouped by SQLite, which sorts every row to group them: a third
        // of the time on a million. A crawl without a summary computes each heavy value once, then packs it.
        var packed = string.Join(" | ", fields.Select(field => $"(({field.Sql}) << {layout[field.Name].Shift})"));
        if (heavy.Count > 0)
        {
            packed = $"(SELECT {packed} FROM (SELECT {string.Join(", ", heavy.Select(item => $"{item.Sql} AS {item.Alias}"))}))";
        }
        var tallies = facets.ToDictionary(facet => facet, _ => new Dictionary<long, long>(), StringComparer.Ordinal);
        Dictionary<string, long> unknown = new(StringComparer.Ordinal);
        context.Tallies.Clear();
        using (var command = context.Command(GridPlanner.Over(context, query, $"COUNT({ReaderFunctions.TALLY}({packed}))", groupBy: 0, packed),
                   query.All.SelectMany(predicate => predicate.Parameters).ToList()))
        {
            command.ExecuteScalar();
            foreach (var (key, count) in context.Tallies)
            {
                long Field(string name) => (key >> layout[name].Shift) & Largest(layout[name].Bits);
                foreach (var facet in facets)
                {
                    if (facet == "severity")
                    {
                        for (var index = 0; index < SEVERITIES.Length; index++)
                        {
                            if ((Field(facet) & (1L << index)) != 0)
                            {
                                tallies[facet][index] = tallies[facet].GetValueOrDefault(index) + count;
                            }
                        }

                        continue;
                    }

                    var value = facet switch
                    {
                        // A status code too far out to pack is still a status code: another class.
                        "statusClass" => Field("statusCode") == Largest(NUMBER_BITS) ? 4 : StatusClass(Unpacked(Field("statusCode")), Field("fetched") != 0),
                        "statusCode" or "depth" => Unpacked(Field(facet)),
                        "responseTime" => Field(facet) is var bucket && bucket < RESPONSE_TIMES.Length ? RESPONSE_TIMES[bucket].Min : (long?)null,
                        _ => Field(facet) is var small && small != Largest(layout[facet].Bits) ? small : (long?)null
                    };

                    if (value is { } known)
                    {
                        tallies[facet][known] = tallies[facet].GetValueOrDefault(known) + count;
                    }
                    else
                    {
                        unknown[facet] = unknown.GetValueOrDefault(facet) + count;
                    }
                }
            }
        }

        return facets.ToDictionary(facet => facet, facet => facet == "severity"
            ? [.. SEVERITIES.Select((_, index) => ((long?)index, tallies[facet].GetValueOrDefault(index)))]
            : tallies[facet].Select(entry => ((long?)entry.Key, entry.Value)).Concat(unknown.TryGetValue(facet, out var count) ? [(null, count)] : []).ToList(),
            StringComparer.Ordinal);
    }

    private static long Largest(int bits) => (1L << bits) - 1;

    /// <summary>A number packed as itself plus one: 0 is null, and the largest a value too large to pack, as good as unknown.</summary>
    private static long? Unpacked(long packed) => packed == 0 || packed == Largest(NUMBER_BITS) ? null : packed - 1;

    /// <summary>A status class from the status code, numbered as <see cref="UrlColumns.STATUS_CLASSES"/>, as the column's SQL gives it.</summary>
    private static long? StatusClass(long? status, bool fetched) => status switch
    {
        >= 200 and <= 299 => 0,
        >= 300 and <= 399 => 1,
        >= 400 and <= 499 => 2,
        >= 500 and <= 599 => 3,
        not null => 4,
        null when fetched => 5,
        _ => null
    };

    /// <summary>A facet's values, known ones in order and unknown last, each with the filter that selects its URLs.</summary>
    private static List<FacetBucket> Buckets(string facet, IReadOnlyList<(long? Value, long Count)> groups)
    {
        if (facet == "severity")
        {
            return
            [
                .. groups.Where(group => group.Count > 0).Select(group => (Severity: SEVERITIES[(int)group.Value!.Value], group.Count))
                    .Select(group => new FacetBucket(group.Severity.Value, group.Count,
                        new UrlFilter(group.Severity.Column, group.Severity.Operator) { Value = FilterValue.Of(group.Severity.Number) }))
            ];
        }

        var sorted = groups.OrderBy(group => group.Value is null).ThenBy(group => group.Value).ToList();
        if (facet == "responseTime")
        {
            return
            [
                .. RESPONSE_TIMES.Select(range => (range, Count: sorted.Where(group => group.Value >= range.Min && (range.Max is null || group.Value <= range.Max)).Sum(group => group.Count)))
                    .Where(bucket => bucket.Count > 0)
                    .Select(bucket => new FacetBucket(
                        bucket.range.Max is { } max ? $"{bucket.range.Min}-{max}" : $"{bucket.range.Min}+",
                        bucket.Count,
                        bucket.range.Max is { } highest
                            ? new UrlFilter("responseMs", FilterOperator.Between) { Min = bucket.range.Min, Max = highest }
                            : new UrlFilter("responseMs", FilterOperator.Gte) { Value = FilterValue.Of(bucket.range.Min) })
                    {
                        Min = bucket.range.Min,
                        Max = bucket.range.Max
                    }),
                .. sorted.Where(group => group.Value is null or < 0).GroupBy(_ => true)
                    .Select(unknown => new FacetBucket(null, unknown.Sum(group => group.Count), new UrlFilter("responseMs", FilterOperator.IsEmpty)))
            ];
        }

        // A value no column has (a crafted file's) counts as unknown, with the unknown ones, last.
        var column = Column(facet);
        return
        [
            .. sorted.Select(group => Bucket(column, group.Value, group.Count))
                .GroupBy(bucket => bucket.Value)
                .Select(same => same.Count() == 1 ? same.First() : same.First() with { Count = same.Sum(bucket => bucket.Count) })
                .OrderBy(bucket => bucket.Value is null)
        ];
    }

    /// <summary>One value of a facet, with the filter that selects its URLs.</summary>
    private static FacetBucket Bucket(UrlColumnDefinition column, long? value, long count)
    {
        // A value no column has (a crafted file's: an enum's number past its values, a negative depth or status, or one too large
        // to pack, a boolean that's neither) is as good as unknown, as it is when facets are counted in one pass.
        if (value is not { } known || known < 0 || (column.Type == UrlColumnType.Enum && known >= column.Values.Count) ||
            (column.Type == UrlColumnType.Boolean && known > 1) || (column.Type == UrlColumnType.Number && known >= Largest(NUMBER_BITS) - 1))
        {
            return new FacetBucket(null, count, new UrlFilter(column.Id, FilterOperator.IsEmpty));
        }

        return column.Type switch
        {
            UrlColumnType.Enum => new FacetBucket(column.Values[(int)known], count, new UrlFilter(column.Id, FilterOperator.In) { Values = [FilterValue.Of(column.Values[(int)known])] }),
            UrlColumnType.Boolean => new FacetBucket(known != 0, count, new UrlFilter(column.Id, FilterOperator.Is) { Value = FilterValue.Of(known != 0) }),
            _ => new FacetBucket(known, count, new UrlFilter(column.Id, FilterOperator.Eq) { Value = FilterValue.Of(known) })
        };
    }
}
