using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Storage.Models;
using static ShoutingIguana.Engine.Storage.Models.FilterOperator;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// Checks a grid query and compiles it to SQL for either mode. Each condition comes in two forms, one free to use an index
/// and one kept off every index, so <see cref="GridPlanner"/> can choose which index drives the query: SQLite has no
/// histograms to tell a rare value from a common one. With a URL summary, a condition on the crawl's own tables (a title,
/// the canonical) is a subquery giving URL ids, so the summary's narrow table is never joined to them to filter.
/// </summary>
internal static class UrlQueryCompiler
{
    public const int MAX_FILTERS = 20;
    public const int MAX_VALUES = 100;
    public const int MAX_TEXT = 1_000;
    public const int MAX_SORT = 3;

    /// <summary>The sitemap a grid is narrowed to, as a parameter of <see cref="SITEMAPS_UNDER"/>.</summary>
    public const string SITEMAP_PARAMETER = "$m0";

    /// <summary>The ids of a sitemap and every sitemap under it, however deep, each once.</summary>
    public const string SITEMAPS_UNDER =
        $"WITH RECURSIVE under(id) AS (SELECT {SITEMAP_PARAMETER} UNION SELECT c.id FROM sitemaps c JOIN under ON c.parent_id = under.id) SELECT id FROM under";

    /// <summary>Stands for the column's SQL in a condition, so one condition gives both its forms.</summary>
    private const string COLUMN = "@column";

    /// <summary>The field the hidden checks are refused under.</summary>
    public const string HIDDEN_CHECKS = "hiddenChecks";

    /// <summary>The start of each hidden check's parameter: <c>$hide0</c>, <c>$hide1</c>, and so on.</summary>
    private const string HIDDEN_PARAMETER = "$hide";

    /// <summary>
    /// The issue counts of the URL summary, each with the severity it counts: <c>issues</c> every one. Each is the number of
    /// the URL's issues whose check the crawl lists in <c>issue_types</c>, as <see cref="UrlSummaryBuilder"/> counts them.
    /// </summary>
    private static readonly Dictionary<string, string?> ISSUE_COUNTS = new(StringComparer.Ordinal)
    {
        ["issues"] = null,
        ["errors"] = nameof(IssueSeverity.Error),
        ["warnings"] = nameof(IssueSeverity.Warning),
        ["notices"] = nameof(IssueSeverity.Notice)
    };

    /// <summary>The URL summary row's own URL id, which most sets of URL ids are tested against.</summary>
    private const string ROW_ID = "s.url_id";

    private static readonly IReadOnlyDictionary<UrlColumnType, FilterOperator[]> OPERATORS = new Dictionary<UrlColumnType, FilterOperator[]>
    {
        [UrlColumnType.Number] = [Eq, Ne, Lt, Lte, Gt, Gte, Between, In, NotIn, IsEmpty, IsNotEmpty],
        [UrlColumnType.Text] = [Contains, NotContains, StartsWith, EndsWith, Eq, Ne, FilterOperator.Regex, NotRegex, IsEmpty, IsNotEmpty],
        [UrlColumnType.Enum] = [In, NotIn, IsEmpty, IsNotEmpty],
        [UrlColumnType.Boolean] = [Is, IsNot, IsEmpty, IsNotEmpty]
    };

    /// <summary>The operators an index can serve, when the column has one.</summary>
    private static readonly HashSet<FilterOperator> INDEXABLE = [Eq, Lt, Lte, Gt, Gte, Between, In, Is, IsEmpty, IsNotEmpty];

    /// <summary>A negative operator's positive one: a negative matches every row the positive doesn't, unknown ones too.</summary>
    private static readonly IReadOnlyDictionary<FilterOperator, FilterOperator> POSITIVE = new Dictionary<FilterOperator, FilterOperator>
    {
        [Ne] = Eq, [NotIn] = In, [NotContains] = Contains, [NotRegex] = FilterOperator.Regex, [IsNot] = Is
    };

    /// <summary>
    /// Field errors in a query, whatever crawl it runs on: columns that don't exist, operators their type doesn't take,
    /// values of the wrong type, patterns that don't compile, paging out of range. Keyed by the field's path.
    /// </summary>
    public static Dictionary<string, List<string>> Validate(UrlQuery query, GridScope scope)
    {
        Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);
        for (var index = 0; index < query.Columns.Count; index++)
        {
            if (query.Columns[index] is null)
            {
                Add(errors, $"columns[{index}]", "Give a column's id.");
            }
            else if (UrlColumns.Find(query.Columns[index], scope.IsCheck) is null)
            {
                Add(errors, $"columns[{index}]", $"There's no column called '{query.Columns[index]}'.");
            }
        }

        ValidateFilters(query.Filters, query.Search, scope, errors);
        LinkGridReader.ValidateEnds(query.LinkEnds, errors);
        if (query.Sort.Count > MAX_SORT)
        {
            Add(errors, "sort", $"Sort by at most {MAX_SORT} columns.");
        }

        HashSet<string> sorted = new(StringComparer.Ordinal);
        for (var index = 0; index < query.Sort.Count; index++)
        {
            if (query.Sort[index] is null)
            {
                Add(errors, $"sort[{index}]", "Give a column to sort by.");
                continue;
            }

            var key = query.Sort[index].Column;
            var column = UrlColumns.Find(key, scope.IsCheck);
            if (column is null)
            {
                Add(errors, $"sort[{index}].column", $"There's no column called '{key}'.");
            }
            else if (!column.Sortable)
            {
                Add(errors, $"sort[{index}].column", $"The grid can't sort by '{key}': filter by it instead.");
            }
            else if (!sorted.Add(key))
            {
                Add(errors, $"sort[{index}].column", $"The sort names '{key}' twice.");
            }
        }

        ValidatePaging(query.Offset, query.Limit, query.After, errors);
        ValidateHidden(query.HiddenChecks, errors);
        return errors;
    }

    /// <summary>The most checks a query hides: more than the catalog has, as the app's settings may keep ids of checks gone since.</summary>
    public const int MAX_HIDDEN_CHECKS = 1_000;

    /// <summary>The longest id a hidden check may have.</summary>
    public const int MAX_HIDDEN_CHECK_LENGTH = 200;

    /// <summary>
    /// The checks a query hides: at most <see cref="MAX_HIDDEN_CHECKS"/> ids, none null or longer than
    /// <see cref="MAX_HIDDEN_CHECK_LENGTH"/>. An id the catalog doesn't have is taken, as the report takes it (a check hidden
    /// by another version): it hides the issues stored under it, if any, so a stale id never breaks a grid.
    /// </summary>
    public static void ValidateHidden(IReadOnlyList<string> hidden, Dictionary<string, List<string>> errors)
    {
        if (hidden.Count > MAX_HIDDEN_CHECKS)
        {
            Add(errors, HIDDEN_CHECKS, $"Hide at most {MAX_HIDDEN_CHECKS:N0} checks.");
        }
        else if (hidden.Any(id => id is null))
        {
            Add(errors, HIDDEN_CHECKS, "Give each hidden check's id.");
        }
        else if (hidden.Any(id => id.Length > MAX_HIDDEN_CHECK_LENGTH))
        {
            Add(errors, HIDDEN_CHECKS, $"A check's id is at most {MAX_HIDDEN_CHECK_LENGTH} characters.");
        }
    }

    /// <summary>
    /// The hidden checks' ids as the parameters the issue counts' SQL names (<see cref="Counting"/>): each once, in order,
    /// so the same checks given in another order are the same query. None when nothing is hidden.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, object>> Hidden(IReadOnlyList<string> hidden) =>
    [
        .. hidden.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select((id, index) => new KeyValuePair<string, object>(HIDDEN_PARAMETER + index.ToString(CultureInfo.InvariantCulture), id))
    ];

    /// <summary>
    /// A column as it reads with checks hidden: an issue count less the URL's issues of those checks (of its severity, for
    /// a severity's count), counted as the summary counts them. Such a count has no index, so the plan never walks one for
    /// it. Any other column, or none hidden, is the column itself, with its SQL unchanged.
    /// </summary>
    public static UrlColumnDefinition Counting(UrlColumnDefinition column, IReadOnlyList<KeyValuePair<string, object>> hidden)
    {
        if (hidden.Count == 0 || column.Source != ColumnSource.Summary || !ISSUE_COUNTS.TryGetValue(column.Id, out var severity))
        {
            return column;
        }

        var ids = string.Join(", ", hidden.Select(parameter => parameter.Key));
        var ofSeverity = severity is null ? string.Empty : $" AND ht.severity = '{severity}'";
        return column with
        {
            SummarySql =
                $"({column.SummarySql} - (SELECT COUNT(*) FROM issues hi JOIN issue_types ht ON ht.id = hi.issue WHERE hi.url_id = s.url_id AND hi.issue IN ({ids}){ofSeverity}))",
            Indexed = false
        };
    }

    /// <summary>The filters and search of a grid query or a facet query.</summary>
    public static void ValidateFilters(IReadOnlyList<UrlFilter> filters, string? search, GridScope scope, Dictionary<string, List<string>> errors)
    {
        if (filters.Count > MAX_FILTERS)
        {
            Add(errors, "filters", $"Use at most {MAX_FILTERS} filters.");
        }

        for (var index = 0; index < filters.Count; index++)
        {
            if (filters[index] is null)
            {
                Add(errors, $"filters[{index}]", "Give a filter: a column, an operator and what it takes.");
                continue;
            }

            ValidateFilter(filters[index], $"filters[{index}]", scope, errors);
        }

        if (search is { Length: > MAX_TEXT })
        {
            Add(errors, "search", $"Search for at most {MAX_TEXT:N0} characters.");
        }
    }

    public static void ValidatePaging(int offset, int limit, string? after, Dictionary<string, List<string>> errors)
    {
        if (limit is < 1 or > UrlGridReader.MAX_LIMIT)
        {
            Add(errors, "limit", $"limit must be from 1 to {UrlGridReader.MAX_LIMIT:N0}.");
        }

        if (offset < 0)
        {
            Add(errors, "offset", "offset must be 0 or more.");
        }

        if (after is not null && offset > 0)
        {
            Add(errors, "after", "Give offset or after, not both.");
        }
    }

    /// <summary>
    /// The columns a query filters or sorts on that need the URL summary, each once, in the order they appear. The check
    /// filter needs only the issues, which a crawl analysed and changed since still has (<paramref name="hasIssues"/>).
    /// </summary>
    public static List<string> NeedingSummary(IReadOnlyList<UrlFilter> filters, IReadOnlyList<UrlSort> sort, GridScope scope, bool hasIssues = false) =>
    [
        .. filters.Select(filter => filter.Column).Concat(sort.Select(key => key.Column))
            .Where(id => id == UrlColumns.CHECK ? !hasIssues : UrlColumns.Find(id, scope.IsCheck)?.NeedsAnalysis == true)
            .Distinct(StringComparer.Ordinal)
    ];

    /// <summary>The query's filters with this regular expression, refused: its matches took too long on the crawl's text.</summary>
    public static UrlQueryException TooComplex(IReadOnlyList<UrlFilter> filters, string pattern)
    {
        List<string> fields =
        [
            .. filters.Select((filter, index) => (filter, index))
                .Where(item => item.filter.Operator is FilterOperator.Regex or NotRegex && item.filter.Value?.Text == pattern)
                .Select(item => $"filters[{item.index}].value")
        ];
        return new UrlQueryException((fields.Count == 0 ? ["filters"] : fields)
            .ToDictionary(field => field, _ => (IReadOnlyList<string>)[ReaderFunctions.TOO_COMPLEX], StringComparer.Ordinal));
    }

    /// <param name="reason">The crawl's indexability reason in crawl SQL: the analysis's where it judged differently.</param>
    public static CompiledQuery Compile(UrlQuery query, GridMode mode, GridScope scope, string reason)
    {
        var hidden = Hidden(query.HiddenChecks);
        var predicates = Filters(query.Filters, query.Search, mode, scope, reason, hidden);
        if (scope.SitemapId is { } sitemap)
        {
            predicates.Insert(0, Sitemap(sitemap, mode));
        }

        if (query.LinkEnds is { } ends)
        {
            predicates.Add(LinkEnds(ends, hidden));
        }

        var sort = query.Sort.Select(key => SortKey(key, mode, scope, reason, hidden)).ToList();
        List<UrlColumnDefinition> columns = [UrlColumns.ID];
        List<string> unavailable = [];
        foreach (var id in query.Columns.Count == 0 ? (scope.IsCheck ? ["url", "detail", "relatedUrl"] : ["url"]) : query.Columns)
        {
            var column = Counting(UrlColumns.Find(id, scope.IsCheck)!, hidden);
            if (columns.Contains(column) || unavailable.Contains(id))
            {
                continue;
            }

            if (column.Source == ColumnSource.Details || column.Sql(mode) is not null)
            {
                columns.Add(column);
            }
            else
            {
                unavailable.Add(id);
            }
        }

        var scopePredicate = scope.IsCheck ? Scope(scope, mode) : null;
        return new CompiledQuery(mode, scopePredicate, predicates, sort, columns, unavailable, Hash(mode, scopePredicate, predicates, sort, hidden)) { Hidden = hidden };
    }

    /// <summary>The filters' and the search's conditions, in order.</summary>
    /// <param name="hidden">The checks hidden (<see cref="Hidden"/>), which a filter on an issue count leaves out.</param>
    public static List<GridPredicate> Filters(IReadOnlyList<UrlFilter> filters, string? search, GridMode mode, GridScope scope, string reason,
        IReadOnlyList<KeyValuePair<string, object>> hidden)
    {
        List<GridPredicate> predicates = [.. filters.Select((filter, index) => Filter(filter, $"$f{index}_", mode, scope, reason, hidden))];
        if (!string.IsNullOrEmpty(search))
        {
            predicates.Add(Search(search, mode));
        }

        return predicates;
    }

    /// <summary>The scope's own condition: a check's rows, and one group's.</summary>
    public static GridPredicate Scope(GridScope scope, GridMode mode)
    {
        List<KeyValuePair<string, object>> parameters = [new("$s0", scope.CheckId!)];
        var condition = "issue = $s0";
        (string Sql, IdSource Source)? stored = null;
        if (scope.Group is { } group)
        {
            stored = StoredGroup(scope, group.Detail, group.RelatedUrlId, "$s0", value =>
            {
                var name = $"$s{parameters.Count}";
                parameters.Add(new(name, value));
                return name;
            });

            if (stored is null && scope.Grouping is IssueGrouping.PageDetail or IssueGrouping.ResponseDetail)
            {
                condition += " AND detail IS $s1";
                parameters.Add(new("$s1", (object?)group.Detail ?? DBNull.Value));
            }

            if (stored is null)
            {
                condition += " AND related_url_id IS $s2";
                parameters.Add(new("$s2", group.RelatedUrlId is { } related ? related : DBNull.Value));
            }
        }

        if (mode == GridMode.Crawl)
        {
            var inline = string.Join(" AND ", condition.Split(" AND ").Select(term => "i." + term)) + (stored is null ? string.Empty : $" AND i.url_id IN ({stored.Value.Sql})");
            return new GridPredicate(inline, inline, parameters, Driving: true, Key(inline, parameters));
        }

        var ids = stored?.Sql ?? $"SELECT url_id FROM issues WHERE {condition}";
        var sql = $"s.url_id IN ({ids})";
        return new GridPredicate(sql, "+" + sql, parameters, Driving: true, Key(sql, parameters))
        {
            Source = stored?.Source ?? Issues(condition, negated: false)
        };
    }

    /// <summary>
    /// The URLs a sitemap lists, and those of every sitemap under it: with a URL summary, read into a set, on the key of
    /// what each sitemap lists; without one, matched against the crawl's own URLs.
    /// </summary>
    public static GridPredicate Sitemap(int sitemapId, GridMode mode)
    {
        List<KeyValuePair<string, object>> parameters = [new(SITEMAP_PARAMETER, sitemapId)];
        const string LISTED = $"m.sitemap_id IN ({SITEMAPS_UNDER})";
        const string IDS = $"SELECT m.url_id FROM sitemap_urls m WHERE {LISTED}";
        if (mode == GridMode.Crawl)
        {
            return Plain($"u.id IN ({IDS})", parameters);
        }

        const string SQL = $"s.url_id IN ({IDS})";
        return new GridPredicate(SQL, "+" + SQL, parameters, Driving: true, Key(SQL, parameters))
        {
            Ids = IDS,
            Source = new IdSource([new IdScan("sitemap_urls m", "m.url_id", LISTED, Combinable: false)], ROW_ID, Negated: false)
        };
    }

    /// <summary>
    /// The URL ids of one group of a check, as its count on the Issues screen came from the analysis's targets or faults:
    /// those naming the other URL, a page in each group of the URLs it names (no URL being those naming none); or those
    /// that are instances of the fault. Null when the group is the issues' own (one by detail, an earlier engine's analysis).
    /// </summary>
    /// <param name="check">The check's id, as a parameter already bound.</param>
    /// <returns>The subquery and how its URL ids are read into a set; null when the group is the issues' own.</returns>
    private static (string Sql, IdSource Source)? StoredGroup(GridScope scope, string? detail, int? relatedUrlId, string check, Func<object, string> parameter)
    {
        if (!scope.TargetsStored)
        {
            return null;
        }

        var definition = IssueCatalog.Find(scope.CheckId!);
        if (definition?.Grouping == IssueGrouping.RelatedUrl)
        {
            var where = $"g.issue = {check} AND g.related_url_id = {parameter(relatedUrlId ?? IssueHit.NO_OTHER_URL)}";
            return GroupIds("issue_targets", where);
        }

        // A fault's issues name no other URL, so a group with one has none of them.
        if (definition is not { Grouping: IssueGrouping.PageDetail, FaultOf: not null })
        {
            return null;
        }

        var faultWhere = $"g.issue = {check} AND g.fault IS {parameter((object?)detail ?? DBNull.Value)}{(relatedUrlId is null ? string.Empty : " AND 0")}";
        return GroupIds("issue_faults", faultWhere);
    }

    /// <summary>One group's URL ids, from the analysis table that stores them, and the scan that reads those ids into a set.</summary>
    private static (string Sql, IdSource Source) GroupIds(string table, string where) =>
        ($"SELECT g.url_id FROM {table} g WHERE {where}", new IdSource([new IdScan($"{table} g", "g.url_id", where, Combinable: false)], ROW_ID, Negated: false));

    /// <summary>One key of the sort. A status class sorts as its status code.</summary>
    public static GridSortKey SortKey(UrlSort sort, GridMode mode, GridScope scope, string reason, IReadOnlyList<KeyValuePair<string, object>> hidden)
    {
        var column = UrlColumns.Find(sort.Column, scope.IsCheck)!;
        var sorted = Counting(column == UrlColumns.STATUS_CLASS ? UrlColumns.STATUS_CODE : column, hidden);
        var sql = Resolve(sorted.Sql(mode)!, reason);
        var indexed = mode == GridMode.Summary ? sorted.Indexed : sorted == UrlColumns.ID || sorted == UrlColumns.URL;
        return new GridSortKey(sorted, sql, "+" + sql, sort.Descending, sorted.Nullable, indexed);
    }

    /// <summary>One filter of a grid query, its errors keyed under <paramref name="field"/> (<c>filters[2]</c>).</summary>
    internal static void ValidateFilter(UrlFilter filter, string field, GridScope scope, Dictionary<string, List<string>> errors)
    {
        if (filter.Column == UrlColumns.CHECK)
        {
            ValidateCheck(filter, field, errors);
            return;
        }

        if (filter.Group is not null)
        {
            Add(errors, $"{field}.group", "Only the check filter takes a group.");
        }

        var column = UrlColumns.Find(filter.Column, scope.IsCheck);
        if (column is null)
        {
            Add(errors, $"{field}.column", $"There's no column called '{filter.Column}'.");
            return;
        }

        ValidateOperator(filter, column, field, errors);
    }

    /// <summary>A filter on a column that exists: that the column filters, takes the operator, and is given what the operator needs.</summary>
    internal static void ValidateOperator(UrlFilter filter, UrlColumnDefinition column, string field, Dictionary<string, List<string>> errors)
    {
        if (!column.Filterable)
        {
            Add(errors, $"{field}.column", $"The grid can't filter by '{filter.Column}'.");
            return;
        }

        var allowed = OPERATORS[column.Type];
        if (!allowed.Contains(filter.Operator))
        {
            Add(errors, $"{field}.op", $"'{filter.Column}' takes {string.Join(", ", allowed.Select(Name))}, not {Name(filter.Operator)}.");
            return;
        }

        ValidateUnused(filter, field, errors);
        switch (filter.Operator)
        {
            case Between:
                if (filter.Min is null && filter.Max is null)
                {
                    Add(errors, $"{field}.min", "between needs a min, a max or both.");
                }
                else if (filter.Min > filter.Max)
                {
                    Add(errors, $"{field}.min", "min is more than max.");
                }

                break;
            case In or NotIn:
                ValidateValues(filter, column, field, errors);
                break;
            case IsEmpty or IsNotEmpty:
                break;
            default:
                ValidateValue(filter, column, field, errors);
                break;
        }
    }

    private static void ValidateValue(UrlFilter filter, UrlColumnDefinition column, string field, Dictionary<string, List<string>> errors)
    {
        var value = filter.Value;
        switch (column.Type)
        {
            case UrlColumnType.Number when value?.Number is null:
                Add(errors, $"{field}.value", $"{Name(filter.Operator)} needs a number.");
                break;
            case UrlColumnType.Boolean when value?.Boolean is null:
                Add(errors, $"{field}.value", $"{Name(filter.Operator)} needs true or false.");
                break;
            case UrlColumnType.Text when value?.Text is null:
                Add(errors, $"{field}.value", $"{Name(filter.Operator)} needs text.");
                break;
            case UrlColumnType.Text when value.Value.Text!.Length > MAX_TEXT:
                Add(errors, $"{field}.value", $"Use at most {MAX_TEXT:N0} characters.");
                break;
            case UrlColumnType.Text when filter.Operator is FilterOperator.Regex or NotRegex && ReaderFunctions.CheckPattern(value.Value.Text!) is { } problem:
                Add(errors, $"{field}.value", problem);
                break;
        }
    }

    private static void ValidateValues(UrlFilter filter, UrlColumnDefinition column, string field, Dictionary<string, List<string>> errors)
    {
        if (filter.Values is not { Count: > 0 and <= MAX_VALUES } values)
        {
            Add(errors, $"{field}.values", $"{Name(filter.Operator)} needs 1 to {MAX_VALUES} values.");
            return;
        }

        if (column.Type == UrlColumnType.Number && values.Any(value => value.Number is null))
        {
            Add(errors, $"{field}.values", "Every value must be a number.");
        }
        else if (column.Type == UrlColumnType.Enum && values.Where(value => value.Text is null || !column.Values.Contains(value.Text)).ToList() is [var wrong, ..])
        {
            Add(errors, $"{field}.values", $"'{filter.Column}' is one of {string.Join(", ", column.Values)}, not {Describe(wrong)}.");
        }
    }

    /// <summary>A value the operator doesn't take is refused, not ignored: the filter wouldn't mean what was sent.</summary>
    private static void ValidateUnused(UrlFilter filter, string field, Dictionary<string, List<string>> errors)
    {
        var op = filter.Operator;
        if (filter.Value is not null && op is Between or In or NotIn or IsEmpty or IsNotEmpty)
        {
            Add(errors, $"{field}.value", $"{Name(op)} doesn't take a value{(op is In or NotIn ? ": give values" : string.Empty)}.");
        }

        if (filter.Values is not null && op is not (In or NotIn))
        {
            Add(errors, $"{field}.values", $"{Name(op)} doesn't take values{(op is Between or IsEmpty or IsNotEmpty ? string.Empty : ": give a value")}.");
        }

        if (op != Between)
        {
            if (filter.Min is not null)
            {
                Add(errors, $"{field}.min", $"{Name(op)} doesn't take a min: only between does.");
            }

            if (filter.Max is not null)
            {
                Add(errors, $"{field}.max", $"{Name(op)} doesn't take a max: only between does.");
            }
        }
    }

    private static void ValidateCheck(UrlFilter filter, string field, Dictionary<string, List<string>> errors)
    {
        if (filter.Operator is not (In or NotIn))
        {
            Add(errors, $"{field}.op", $"'check' takes in or notIn, not {Name(filter.Operator)}.");
            return;
        }

        ValidateUnused(filter, field, errors);

        if (filter.Values is not { Count: > 0 and <= MAX_VALUES } values || values.Any(value => value.Text is null))
        {
            Add(errors, $"{field}.values", $"Give 1 to {MAX_VALUES} check ids.");
            return;
        }

        if (values.FirstOrDefault(value => IssueCatalog.Find(value.Text!) is null) is { Text: { } unknown })
        {
            Add(errors, $"{field}.values", $"There's no check called '{unknown}'.");
            return;
        }

        if (filter.Group is not null)
        {
            if (filter.Operator != In || values.Count != 1)
            {
                Add(errors, $"{field}.group", "A group narrows one check: give it with in and a single check.");
            }
            else if (IssueCatalog.Find(values[0].Text!)!.Grouping == IssueGrouping.None)
            {
                Add(errors, $"{field}.group", $"The check '{values[0].Text}' lists its issues without groups.");
            }
        }
    }

    private static GridPredicate Filter(UrlFilter filter, string prefix, GridMode mode, GridScope scope, string reason, IReadOnlyList<KeyValuePair<string, object>> hidden)
    {
        List<KeyValuePair<string, object>> parameters = [];
        string Parameter(object value)
        {
            var name = prefix + parameters.Count.ToString(CultureInfo.InvariantCulture);
            parameters.Add(new(name, value));
            return name;
        }

        if (filter.Column == UrlColumns.CHECK)
        {
            return Check(filter, mode, Parameter, parameters, scope);
        }

        var column = UrlColumns.Find(filter.Column, scope.IsCheck)!;
        if (mode == GridMode.Crawl)
        {
            return Inline(filter, column, Resolve(column.CrawlSql!, reason), column.UrlIdSql is { } id ? Resolve(id, reason) : null, indexed: false, Parameter, parameters);
        }

        if (column is { Source: ColumnSource.Summary, UrlIdSql: { } other } && filter.Operator is not (IsEmpty or IsNotEmpty))
        {
            return OtherUrl(filter, column, other, Parameter, parameters);
        }

        if (column == UrlColumns.STATUS_CLASS && filter.Operator == In)
        {
            return StatusClasses(filter, parameters);
        }

        if (column == UrlColumns.INDEXABLE)
        {
            // Indexable is the reason none, which the indexability index serves: the summary's two columns agree.
            var condition = filter.Operator switch
            {
                Is => filter.Value!.Value.Boolean!.Value ? $"{COLUMN} = 0" : $"{COLUMN} > 0",
                // Anything but: the other value, or no reason at all (not judged).
                IsNot => filter.Value!.Value.Boolean!.Value ? $"({COLUMN} IS NULL OR {COLUMN} > 0)" : $"({COLUMN} IS NULL OR {COLUMN} = 0)",
                IsEmpty => $"{COLUMN} IS NULL",
                _ => $"{COLUMN} IS NOT NULL"
            };
            // "Is not" matches most rows, as a negative does: tested per row, never driving the read off the index.
            return filter.Operator == IsNot
                ? Plain(condition.Replace(COLUMN, "s.indexability_reason", StringComparison.Ordinal), parameters)
                : Indexed(condition, "s.indexability_reason", parameters);
        }

        if (Counting(column, hidden) is var counting && !ReferenceEquals(counting, column))
        {
            // An issue count less the hidden checks' issues: tested per row, off every index, with the hidden checks among
            // its parameters, so its key, and every count kept by it, is its own.
            parameters.AddRange(hidden);
            return Inline(filter, counting, counting.SummarySql!, null, indexed: false, Parameter, parameters);
        }

        return column.Source switch
        {
            ColumnSource.Summary => Inline(filter, column, column.SummarySql!, column.UrlIdSql, column.Indexed, Parameter, parameters),
            _ => Subquery(filter, column, Parameter, parameters, scope)
        };
    }

    /// <summary>A condition on the row the query reads: the URL summary's, or the crawl's tables joined.</summary>
    internal static GridPredicate Inline(UrlFilter filter, UrlColumnDefinition column, string sql, string? urlId, bool indexed, Func<object, string> parameter,
        List<KeyValuePair<string, object>> parameters)
    {
        if (POSITIVE.TryGetValue(filter.Operator, out var positive))
        {
            var matched = Condition(filter, positive, column, urlId, parameter).Replace(COLUMN, sql, StringComparison.Ordinal);
            var subject = urlId ?? sql;
            // A boolean's "is not" is the other value or unknown, whatever the column says of its nulls.
            var negated = column.Nullable || urlId is not null || filter.Operator == IsNot
                ? $"({subject} IS NULL OR NOT ({matched}))"
                : $"NOT ({matched})";
            return new GridPredicate(negated, negated, parameters, Driving: false, Key(negated, parameters));
        }

        var condition = Condition(filter, filter.Operator, column, urlId, parameter);
        return indexed && urlId is null && INDEXABLE.Contains(filter.Operator)
            ? Indexed(condition, sql, parameters)
            : Plain(condition.Replace(COLUMN, sql, StringComparison.Ordinal), parameters);
    }

    /// <summary>
    /// A condition on a crawl table (a title, a response's outcome, an issue's detail) with a URL summary: the URL ids it
    /// gives drive the query, or a positive form's ids are looked up per row. A negative one, and "is empty", matches the
    /// URLs the positive form doesn't give, those with no row there at all among them.
    /// </summary>
    private static GridPredicate Subquery(UrlFilter filter, UrlColumnDefinition column, Func<object, string> parameter, List<KeyValuePair<string, object>> parameters,
        GridScope scope)
    {
        var (from, id) = column.Source switch
        {
            ColumnSource.Urls => ("urls u", "u.id"),
            ColumnSource.Responses => ("responses r", "r.url_id"),
            _ => ("issues i", "i.url_id")
        };

        var negative = filter.Operator == IsEmpty || POSITIVE.ContainsKey(filter.Operator);
        var matched = negative ? filter.Operator == IsEmpty ? IsNotEmpty : POSITIVE[filter.Operator] : filter.Operator;
        var condition = Condition(filter, matched, column, column.UrlIdSql, parameter).Replace(COLUMN, column.Sql(GridMode.Summary)!, StringComparison.Ordinal);
        var check = column.Source == ColumnSource.Issues ? $"i.issue = {parameter(scope.CheckId!)} AND " : string.Empty;
        var subquery = $"SELECT {id} FROM {from} WHERE {check}{condition}";
        var source = new IdSource([new IdScan(from, id, check + condition, Combinable: column.Source != ColumnSource.Issues)], ROW_ID, negative);
        if (negative)
        {
            return Plain($"s.url_id NOT IN ({subquery})", parameters) with { Source = source };
        }

        var sql = $"s.url_id IN ({subquery})";
        return new GridPredicate(sql, "+" + sql, parameters, Driving: true, Key(sql, parameters)) { Ids = subquery, Source = source };
    }

    /// <summary>
    /// A condition on another URL's text, which the URL summary names by id (where the row's redirects end): the URLs whose
    /// text matches are read into a set, which each row's id for that URL is tested against.
    /// </summary>
    private static GridPredicate OtherUrl(UrlFilter filter, UrlColumnDefinition column, string urlId, Func<object, string> parameter,
        List<KeyValuePair<string, object>> parameters)
    {
        var negated = POSITIVE.TryGetValue(filter.Operator, out var positive);
        var text = Condition(filter, negated ? positive : filter.Operator, column, null, parameter).Replace(COLUMN, "u.url", StringComparison.Ordinal);
        var matched = $"{urlId} IN (SELECT u.id FROM urls u WHERE {text})";
        var sql = negated ? $"({urlId} IS NULL OR NOT ({matched}))" : matched;
        return Plain(sql, parameters) with { Source = new IdSource([new IdScan("urls u", "u.id", text, Combinable: true)], urlId, negated) };
    }

    /// <summary>The URLs a check flagged, or one group of them, as their issues' key finds them.</summary>
    private static IdSource Issues(string condition, bool negated) => new([new IdScan("issues i", "i.url_id", condition, Combinable: false)], ROW_ID, negated);

    /// <summary>
    /// The positive condition an operator states, with <see cref="COLUMN"/> for the column. A column whose value is
    /// another URL's text matches that URL through <c>urls</c>, by id.
    /// </summary>
    private static string Condition(UrlFilter filter, FilterOperator op, UrlColumnDefinition column, string? urlId, Func<object, string> parameter)
    {
        if (urlId is not null)
        {
            return op switch
            {
                IsEmpty => $"{urlId} IS NULL",
                IsNotEmpty => $"{urlId} IS NOT NULL",
                _ => $"{urlId} IN (SELECT t.id FROM urls t WHERE {Condition(filter, op, column, null, parameter).Replace(COLUMN, "t.url", StringComparison.Ordinal)})"
            };
        }

        var text = column.Type == UrlColumnType.Text;
        return op switch
        {
            Eq => $"{COLUMN} = {parameter(Value(filter.Value!.Value))}",
            Lt => $"{COLUMN} < {parameter(Value(filter.Value!.Value))}",
            Lte => $"{COLUMN} <= {parameter(Value(filter.Value!.Value))}",
            Gt => $"{COLUMN} > {parameter(Value(filter.Value!.Value))}",
            Gte => $"{COLUMN} >= {parameter(Value(filter.Value!.Value))}",
            Between => $"({string.Join(" AND ", new[]
            {
                filter.Min is { } min ? $"{COLUMN} >= {parameter(Number(min))}" : null,
                filter.Max is { } max ? $"{COLUMN} <= {parameter(Number(max))}" : null
            }.OfType<string>())})",
            In => $"{COLUMN} IN ({string.Join(", ", filter.Values!.Select(value => parameter(column.Type == UrlColumnType.Enum ? EnumNumber(column, value.Text!) : Value(value))))})",
            Contains => Like(parameter, $"%{Escape(filter.Value!.Value.Text!)}%"),
            StartsWith => Like(parameter, $"{Escape(filter.Value!.Value.Text!)}%"),
            EndsWith => Like(parameter, $"%{Escape(filter.Value!.Value.Text!)}"),
            FilterOperator.Regex => $"{ReaderFunctions.REGEX}({parameter(filter.Value!.Value.Text!)}, {COLUMN})",
            Is => $"{COLUMN} = {(filter.Value!.Value.Boolean!.Value ? 1 : 0)}",
            IsEmpty => text ? $"({COLUMN} IS NULL OR {COLUMN} = '')" : $"{COLUMN} IS NULL",
            IsNotEmpty => text ? $"({COLUMN} IS NOT NULL AND {COLUMN} <> '')" : $"{COLUMN} IS NOT NULL",
            _ => throw new ArgumentOutOfRangeException(nameof(op), op, "A negative operator has no condition of its own.")
        };
    }

    /// <summary>Status classes from their status ranges, so the status index serves them; no HTTP answer is a null status, fetched.</summary>
    private static GridPredicate StatusClasses(UrlFilter filter, List<KeyValuePair<string, object>> parameters)
    {
        var classes = filter.Values!.Select(value => value.Text switch
        {
            "2xx" => $"({COLUMN} >= 200 AND {COLUMN} <= 299)",
            "3xx" => $"({COLUMN} >= 300 AND {COLUMN} <= 399)",
            "4xx" => $"({COLUMN} >= 400 AND {COLUMN} <= 499)",
            "5xx" => $"({COLUMN} >= 500 AND {COLUMN} <= 599)",
            "other" => $"({COLUMN} < 200 OR {COLUMN} > 599)",
            _ => $"({COLUMN} IS NULL AND s.indexability_reason IS NOT NULL)"
        }).Distinct();
        return Indexed($"({string.Join(" OR ", classes)})", "s.status_code", parameters);
    }

    /// <summary>
    /// The URLs a check flagged (or one group of them), from the last analysis's issues, or from the targets and faults it
    /// stored: with a URL summary, read into a set; without one (a crawl continued since its analysis), matched against the
    /// crawl's own URLs, as a check's rows are.
    /// </summary>
    private static GridPredicate Check(UrlFilter filter, GridMode mode, Func<object, string> parameter, List<KeyValuePair<string, object>> parameters, GridScope scope)
    {
        var checks = filter.Values!.Select(value => value.Text!).ToList();
        string condition;
        (string Sql, IdSource Source)? stored = null;
        if (filter.Group is { } group)
        {
            var check = parameter(checks[0]);
            condition = $"issue = {check}";
            var grouping = IssueCatalog.Find(checks[0])!.Grouping;
            stored = StoredGroup(new GridScope(checks[0], group, grouping) { TargetsStored = scope.TargetsStored }, group.Detail, group.RelatedUrlId, check, parameter);
            if (stored is null && grouping is IssueGrouping.PageDetail or IssueGrouping.ResponseDetail)
            {
                condition += $" AND detail IS {parameter((object?)group.Detail ?? DBNull.Value)}";
            }

            if (stored is null)
            {
                condition += $" AND related_url_id IS {parameter(group.RelatedUrlId is { } related ? related : DBNull.Value)}";
            }
        }
        else
        {
            condition = $"issue IN ({string.Join(", ", checks.Select(parameter))})";
        }

        var subquery = stored?.Sql ?? $"SELECT url_id FROM issues WHERE {condition}";
        if (mode == GridMode.Crawl)
        {
            return Plain($"u.id {(filter.Operator == NotIn ? "NOT IN" : "IN")} ({subquery})", parameters);
        }

        var source = stored?.Source ?? Issues(condition, negated: false);
        if (filter.Operator == NotIn)
        {
            return Plain($"s.url_id NOT IN ({subquery})", parameters) with { Source = source with { Negated = true } };
        }

        var sql = $"s.url_id IN ({subquery})";
        return new GridPredicate(sql, "+" + sql, parameters, Driving: true, Key(sql, parameters)) { Ids = subquery, Source = source };
    }

    /// <summary>
    /// The URLs at one end of the links some link filters match: a condition to be read from the links grid, which gives
    /// the set of those URLs, once the rest is compiled (<see cref="UrlGridReader.WithLinkEnds"/>). Its key is the filters
    /// as asked, so the query's hash, and what's kept for it, tell one set of links from another; with checks hidden, which
    /// a link filter on an end's issue count leaves out, those too.
    /// </summary>
    public static GridPredicate LinkEnds(UrlLinkEnds ends, IReadOnlyList<KeyValuePair<string, object>> hidden)
    {
        var filters = ends.Filters.Select(filter => string.Join(" ", filter.Column, filter.Operator, Describe(filter.Value),
            $"[{string.Join(",", (filter.Values ?? []).Select(value => Describe(value)))}]", Describe(filter.Min), Describe(filter.Max),
            filter.Group is { } group ? $"{Describe(group.Detail)}/{Describe(group.RelatedUrlId)}" : string.Empty));
        var key = $"linkends|{ends.End}|{string.Join("&", filters)}|{Describe(ends.Search)}";
        return new GridPredicate("1", "1", [], Driving: false, hidden.Count == 0 ? key : $"{key}|{HiddenKey(hidden)}") { LinkEnds = ends };
    }

    /// <summary>The URL or the title contains the text.</summary>
    private static GridPredicate Search(string search, GridMode mode)
    {
        List<KeyValuePair<string, object>> parameters = [new("$q", $"%{Escape(search)}%")];
        const string URL = @"u.url LIKE $q ESCAPE '\'";
        const string TITLE = @"r.title LIKE $q ESCAPE '\'";
        if (mode == GridMode.Crawl)
        {
            return Plain($"({URL} OR {TITLE})", parameters);
        }

        const string IDS = $"SELECT u.id FROM urls u WHERE {URL} UNION SELECT r.url_id FROM responses r WHERE {TITLE}";
        const string SQL = $"s.url_id IN ({IDS})";
        return new GridPredicate(SQL, "+" + SQL, parameters, Driving: true, Key(SQL, parameters))
        {
            Ids = IDS,
            Source = new IdSource([new IdScan("urls u", "u.id", URL, Combinable: true), new IdScan("responses r", "r.url_id", TITLE, Combinable: true)], ROW_ID, Negated: false)
        };
    }

    /// <summary>A condition on an indexed column: free to use the index, or kept off it with a unary plus.</summary>
    private static GridPredicate Indexed(string condition, string column, List<KeyValuePair<string, object>> parameters)
    {
        var sql = condition.Replace(COLUMN, column, StringComparison.Ordinal);
        return new GridPredicate(sql, condition.Replace(COLUMN, "+" + column, StringComparison.Ordinal), parameters, Driving: true, Key(sql, parameters));
    }

    private static GridPredicate Plain(string sql, List<KeyValuePair<string, object>> parameters) => new(sql, sql, parameters, Driving: false, Key(sql, parameters));

    private static string Like(Func<object, string> parameter, string pattern) => $@"{COLUMN} LIKE {parameter(pattern)} ESCAPE '\'";

    /// <summary>LIKE's wildcards in the user's text, matched as themselves.</summary>
    internal static string Escape(string text) =>
        text.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("%", @"\%", StringComparison.Ordinal).Replace("_", @"\_", StringComparison.Ordinal);

    private static object Value(FilterValue value) => value.Number is { } number ? Number(number) : (object?)value.Text ?? value.Boolean!.Value;

    /// <summary>Integers as integers, so they compare exactly with the columns, which all hold integers.</summary>
    private static object Number(double number) =>
        Math.Floor(number) == number && number is >= long.MinValue and <= long.MaxValue ? (long)number : number;

    private static long EnumNumber(UrlColumnDefinition column, string name) => column.Values.ToList().IndexOf(name);

    private static string Resolve(string sql, string reason) => sql.Replace(UrlColumns.REASON, reason, StringComparison.Ordinal);

    private static string Key(string sql, IReadOnlyList<KeyValuePair<string, object>> parameters) =>
        $"{sql}|{string.Join("|", parameters.Select(parameter => $"{parameter.Key}={Describe(parameter.Value)}"))}";

    /// <param name="hidden">The checks hidden: a sort by an issue count is another order with them, so a cursor of one isn't read on by the other.</param>
    private static string Hash(GridMode mode, GridPredicate? scope, IReadOnlyList<GridPredicate> predicates, IReadOnlyList<GridSortKey> sort,
        IReadOnlyList<KeyValuePair<string, object>> hidden)
    {
        var text = string.Join("\n", new[] { mode.ToString(), scope?.Key ?? string.Empty }
            .Concat(predicates.Select(predicate => predicate.Key))
            .Concat(sort.Select(key => $"{key.Sql} {(key.Descending ? "desc" : "asc")}"))
            .Concat(hidden.Count == 0 ? [] : [HiddenKey(hidden)]));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    }

    /// <summary>The hidden checks as the keys and hashes hold them; empty when none are.</summary>
    public static string HiddenKey(IReadOnlyList<KeyValuePair<string, object>> hidden) =>
        hidden.Count == 0 ? string.Empty : "hidden|" + string.Join(",", hidden.Select(parameter => Describe(parameter.Value)));

    /// <summary>A value as the keys and hashes hold it: text quoted, its quotes doubled, so no text can pass for more conditions.</summary>
    internal static string Describe(object? value) => value switch
    {
        null or DBNull => "null",
        string text => $"'{text.Replace("'", "''", StringComparison.Ordinal)}'",
        FilterValue filterValue => Describe(filterValue.Text ?? (object?)filterValue.Number ?? filterValue.Boolean),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null"
    };

    private static string Name(FilterOperator op) => char.ToLowerInvariant(op.ToString()[0]) + op.ToString()[1..];

    internal static void Add(Dictionary<string, List<string>> errors, string field, string message)
    {
        if (!errors.TryGetValue(field, out var messages))
        {
            errors[field] = messages = [];
        }

        messages.Add(message);
    }
}
