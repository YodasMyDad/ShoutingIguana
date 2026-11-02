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

    /// <summary>Stands for the column's SQL in a condition, so one condition gives both its forms.</summary>
    private const string COLUMN = "@column";

    private static readonly IReadOnlyDictionary<UrlColumnType, FilterOperator[]> OPERATORS = new Dictionary<UrlColumnType, FilterOperator[]>
    {
        [UrlColumnType.Number] = [Eq, Ne, Lt, Lte, Gt, Gte, Between, In, NotIn, IsEmpty, IsNotEmpty],
        [UrlColumnType.Text] = [Contains, NotContains, StartsWith, EndsWith, Eq, Ne, FilterOperator.Regex, NotRegex, IsEmpty, IsNotEmpty],
        [UrlColumnType.Enum] = [In, NotIn, IsEmpty, IsNotEmpty],
        [UrlColumnType.Boolean] = [Is, IsEmpty, IsNotEmpty]
    };

    /// <summary>The operators an index can serve, when the column has one.</summary>
    private static readonly HashSet<FilterOperator> INDEXABLE = [Eq, Lt, Lte, Gt, Gte, Between, In, Is, IsEmpty, IsNotEmpty];

    /// <summary>A negative operator's positive one: a negative matches every row the positive doesn't, unknown ones too.</summary>
    private static readonly IReadOnlyDictionary<FilterOperator, FilterOperator> POSITIVE = new Dictionary<FilterOperator, FilterOperator>
    {
        [Ne] = Eq, [NotIn] = In, [NotContains] = Contains, [NotRegex] = FilterOperator.Regex
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
            if (UrlColumns.Find(query.Columns[index], scope.IsCheck) is null)
            {
                Add(errors, $"columns[{index}]", $"There's no column called '{query.Columns[index]}'.");
            }
        }

        ValidateFilters(query.Filters, query.Search, scope, errors);
        if (query.Sort.Count > MAX_SORT)
        {
            Add(errors, "sort", $"Sort by at most {MAX_SORT} columns.");
        }

        HashSet<string> sorted = new(StringComparer.Ordinal);
        for (var index = 0; index < query.Sort.Count; index++)
        {
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
        return errors;
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

    /// <summary>The columns a query filters or sorts on that need the URL summary, each once, in the order they appear.</summary>
    public static List<string> NeedingSummary(IReadOnlyList<UrlFilter> filters, IReadOnlyList<UrlSort> sort, GridScope scope) =>
    [
        .. filters.Select(filter => filter.Column).Concat(sort.Select(key => key.Column))
            .Where(id => id == UrlColumns.CHECK || UrlColumns.Find(id, scope.IsCheck)?.NeedsAnalysis == true)
            .Distinct(StringComparer.Ordinal)
    ];

    /// <param name="reason">The crawl's indexability reason in crawl SQL: the analysis's where it judged differently.</param>
    public static CompiledQuery Compile(UrlQuery query, GridMode mode, GridScope scope, string reason)
    {
        var predicates = Filters(query.Filters, query.Search, mode, scope, reason);
        var sort = query.Sort.Select(key => SortKey(key, mode, scope, reason)).ToList();
        List<UrlColumnDefinition> columns = [UrlColumns.ID];
        List<string> unavailable = [];
        foreach (var id in query.Columns.Count == 0 ? (scope.IsCheck ? ["url", "detail", "relatedUrl"] : ["url"]) : query.Columns)
        {
            var column = UrlColumns.Find(id, scope.IsCheck)!;
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
        return new CompiledQuery(mode, scopePredicate, predicates, sort, columns, unavailable, Hash(mode, scopePredicate, predicates, sort));
    }

    /// <summary>The filters' and the search's conditions, in order.</summary>
    public static List<GridPredicate> Filters(IReadOnlyList<UrlFilter> filters, string? search, GridMode mode, GridScope scope, string reason)
    {
        List<GridPredicate> predicates = [.. filters.Select((filter, index) => Filter(filter, $"$f{index}_", mode, scope, reason))];
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
        string? stored = null;
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
            var inline = string.Join(" AND ", condition.Split(" AND ").Select(term => "i." + term)) + (stored is null ? string.Empty : $" AND i.url_id IN ({stored})");
            return new GridPredicate(inline, inline, parameters, Driving: true, Key(inline, parameters));
        }

        var sql = $"s.url_id IN ({stored ?? $"SELECT url_id FROM issues WHERE {condition}"})";
        return new GridPredicate(sql, "+" + sql, parameters, Driving: true, Key(sql, parameters));
    }

    /// <summary>
    /// The URL ids of one group of a check, as its count on the Issues screen came from the analysis's targets or faults:
    /// those naming the other URL, a page in each group of the URLs it names (no URL being those naming none); or those
    /// that are instances of the fault. Null when the group is the issues' own (one by detail, an earlier engine's analysis).
    /// </summary>
    /// <param name="check">The check's id, as a parameter already bound.</param>
    private static string? StoredGroup(GridScope scope, string? detail, int? relatedUrlId, string check, Func<object, string> parameter)
    {
        if (!scope.TargetsStored)
        {
            return null;
        }

        var definition = IssueCatalog.Find(scope.CheckId!);
        if (definition?.Grouping == IssueGrouping.RelatedUrl)
        {
            return $"SELECT g.url_id FROM issue_targets g WHERE g.issue = {check} AND g.related_url_id = {parameter(relatedUrlId ?? IssueHit.NO_OTHER_URL)}";
        }

        // A fault's issues name no other URL, so a group with one has none of them.
        return definition is { Grouping: IssueGrouping.PageDetail, FaultOf: not null }
            ? $"SELECT g.url_id FROM issue_faults g WHERE g.issue = {check} AND g.fault IS {parameter((object?)detail ?? DBNull.Value)}{(relatedUrlId is null ? string.Empty : " AND 0")}"
            : null;
    }

    /// <summary>One key of the sort. A status class sorts as its status code.</summary>
    public static GridSortKey SortKey(UrlSort sort, GridMode mode, GridScope scope, string reason)
    {
        var column = UrlColumns.Find(sort.Column, scope.IsCheck)!;
        var sorted = column == UrlColumns.STATUS_CLASS ? UrlColumns.STATUS_CODE : column;
        var sql = Resolve(sorted.Sql(mode)!, reason);
        var indexed = mode == GridMode.Summary ? sorted.Indexed : sorted == UrlColumns.ID || sorted == UrlColumns.URL;
        return new GridSortKey(sorted, sql, "+" + sql, sort.Descending, sorted.Nullable, indexed);
    }

    private static void ValidateFilter(UrlFilter filter, string field, GridScope scope, Dictionary<string, List<string>> errors)
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
                Add(errors, $"{field}.value", "is needs true or false.");
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

    private static void ValidateCheck(UrlFilter filter, string field, Dictionary<string, List<string>> errors)
    {
        if (filter.Operator is not (In or NotIn))
        {
            Add(errors, $"{field}.op", $"'check' takes in or notIn, not {Name(filter.Operator)}.");
            return;
        }

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

    private static GridPredicate Filter(UrlFilter filter, string prefix, GridMode mode, GridScope scope, string reason)
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
            return Check(filter, Parameter, parameters, scope);
        }

        var column = UrlColumns.Find(filter.Column, scope.IsCheck)!;
        if (mode == GridMode.Crawl)
        {
            return Inline(filter, column, Resolve(column.CrawlSql!, reason), column.UrlIdSql is { } id ? Resolve(id, reason) : null, indexed: false, Parameter, parameters);
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
                IsEmpty => $"{COLUMN} IS NULL",
                _ => $"{COLUMN} IS NOT NULL"
            };
            return Indexed(condition, "s.indexability_reason", parameters);
        }

        return column.Source switch
        {
            ColumnSource.Summary => Inline(filter, column, column.SummarySql!, column.UrlIdSql, column.Indexed, Parameter, parameters),
            _ => Subquery(filter, column, Parameter, parameters, scope)
        };
    }

    /// <summary>A condition on the row the query reads: the URL summary's, or the crawl's tables joined.</summary>
    private static GridPredicate Inline(UrlFilter filter, UrlColumnDefinition column, string sql, string? urlId, bool indexed, Func<object, string> parameter,
        List<KeyValuePair<string, object>> parameters)
    {
        if (POSITIVE.TryGetValue(filter.Operator, out var positive))
        {
            var matched = Condition(filter, positive, column, urlId, parameter).Replace(COLUMN, sql, StringComparison.Ordinal);
            var subject = urlId ?? sql;
            var negated = column.Nullable || urlId is not null ? $"({subject} IS NULL OR NOT ({matched}))" : $"NOT ({matched})";
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
        if (negative)
        {
            return Plain($"s.url_id NOT IN ({subquery})", parameters);
        }

        var sql = $"s.url_id IN ({subquery})";
        return new GridPredicate(sql, "+" + sql, parameters, Driving: true, Key(sql, parameters)) { Ids = subquery };
    }

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

    private static GridPredicate Check(UrlFilter filter, Func<object, string> parameter, List<KeyValuePair<string, object>> parameters, GridScope scope)
    {
        var checks = filter.Values!.Select(value => value.Text!).ToList();
        string condition;
        string? stored = null;
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

        var subquery = stored ?? $"SELECT url_id FROM issues WHERE {condition}";
        if (filter.Operator == NotIn)
        {
            return Plain($"s.url_id NOT IN ({subquery})", parameters);
        }

        var sql = $"s.url_id IN ({subquery})";
        return new GridPredicate(sql, "+" + sql, parameters, Driving: true, Key(sql, parameters)) { Ids = subquery };
    }

    /// <summary>The URL or the title contains the text.</summary>
    private static GridPredicate Search(string search, GridMode mode)
    {
        List<KeyValuePair<string, object>> parameters = [new("$q", $"%{Escape(search)}%")];
        if (mode == GridMode.Crawl)
        {
            return Plain(@"(u.url LIKE $q ESCAPE '\' OR r.title LIKE $q ESCAPE '\')", parameters);
        }

        const string IDS = @"SELECT u.id FROM urls u WHERE u.url LIKE $q ESCAPE '\' UNION SELECT r.url_id FROM responses r WHERE r.title LIKE $q ESCAPE '\'";
        const string SQL = $"s.url_id IN ({IDS})";
        return new GridPredicate(SQL, "+" + SQL, parameters, Driving: true, Key(SQL, parameters)) { Ids = IDS };
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
    private static string Escape(string text) =>
        text.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("%", @"\%", StringComparison.Ordinal).Replace("_", @"\_", StringComparison.Ordinal);

    private static object Value(FilterValue value) => value.Number is { } number ? Number(number) : (object?)value.Text ?? value.Boolean!.Value;

    /// <summary>Integers as integers, so they compare exactly with the columns, which all hold integers.</summary>
    private static object Number(double number) =>
        Math.Floor(number) == number && number is >= long.MinValue and <= long.MaxValue ? (long)number : number;

    private static long EnumNumber(UrlColumnDefinition column, string name) => column.Values.ToList().IndexOf(name);

    private static string Resolve(string sql, string reason) => sql.Replace(UrlColumns.REASON, reason, StringComparison.Ordinal);

    private static string Key(string sql, IReadOnlyList<KeyValuePair<string, object>> parameters) =>
        $"{sql}|{string.Join("|", parameters.Select(parameter => $"{parameter.Key}={Describe(parameter.Value)}"))}";

    private static string Hash(GridMode mode, GridPredicate? scope, IReadOnlyList<GridPredicate> predicates, IReadOnlyList<GridSortKey> sort)
    {
        var text = string.Join("\n", new[] { mode.ToString(), scope?.Key ?? string.Empty }
            .Concat(predicates.Select(predicate => predicate.Key))
            .Concat(sort.Select(key => $"{key.Sql} {(key.Descending ? "desc" : "asc")}")));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    }

    /// <summary>A value as the keys and hashes hold it: text quoted, its quotes doubled, so no text can pass for more conditions.</summary>
    private static string Describe(object? value) => value switch
    {
        null or DBNull => "null",
        string text => $"'{text.Replace("'", "''", StringComparison.Ordinal)}'",
        FilterValue filterValue => Describe(filterValue.Text ?? (object?)filterValue.Number ?? filterValue.Boolean),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null"
    };

    private static string Name(FilterOperator op) => char.ToLowerInvariant(op.ToString()[0]) + op.ToString()[1..];

    private static void Add(Dictionary<string, List<string>> errors, string field, string message)
    {
        if (!errors.TryGetValue(field, out var messages))
        {
            errors[field] = messages = [];
        }

        messages.Add(message);
    }
}
