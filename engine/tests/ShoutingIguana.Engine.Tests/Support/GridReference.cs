using System.Globalization;
using System.Text.RegularExpressions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Tests.Support;

/// <summary>
/// Every URL's grid values worked out from a crawl's own rows and its URL summary by plain SQL and C#, without the grid
/// readers' compiler or planner, with a filter evaluator and a sort of its own: the readers' pages are checked against it.
/// </summary>
internal sealed class GridReference
{
    private static readonly string[] STATUS_CLASSES = ["2xx", "3xx", "4xx", "5xx", "other", "noResponse"];

    private GridReference(Dictionary<int, Dictionary<string, object?>> rows, Dictionary<int, List<(string Issue, string? Detail, int? Related)>> issues,
        Dictionary<(string Issue, int Url), List<int>> targets, Dictionary<(string Issue, int Url), string> faults)
    {
        Rows = rows;
        Issues = issues;
        Targets = targets;
        Faults = faults;
    }

    public IReadOnlyDictionary<int, Dictionary<string, object?>> Rows { get; }

    /// <summary>Each URL's issues: the check, its detail and the other URL involved.</summary>
    public IReadOnlyDictionary<int, List<(string Issue, string? Detail, int? Related)>> Issues { get; }

    /// <summary>
    /// Every URL each issue is about, as the analysis stored them (<c>issue_targets</c>), by check and URL flagged, in id order:
    /// 0 is no other URL.
    /// </summary>
    public IReadOnlyDictionary<(string Issue, int Url), List<int>> Targets { get; }

    /// <summary>The fault each issue of a check grouped by one is an instance of (<c>issue_faults</c>), by check and URL flagged.</summary>
    public IReadOnlyDictionary<(string Issue, int Url), string> Faults { get; }

    public static GridReference Load(string path)
    {
        var summary = UrlSummaryReader.ReadAll(path).ToDictionary(row => row.UrlId);
        var urls = TestCrawls.Rows(path, "SELECT id, url FROM urls").Select(line => line.Split(" | ", 2))
            .ToDictionary(parts => int.Parse(parts[0], CultureInfo.InvariantCulture), parts => parts[1]);
        Dictionary<int, Dictionary<string, object?>> rows = [];
        Dictionary<int, (bool Fetched, int? Status)> statuses = [];

        using (var connection = CrawlDatabase.OpenReader(path))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT u.id, u.url, u.is_internal, u.source, u.depth, r.url_id IS NOT NULL, r.error, r.outcome, r.status_code, r.content_type, r.title,
                       r.meta_description, r.h1, r.h1_count, r.word_count, r.size_bytes, r.response_ms, r.ttfb_ms, r.is_noindex, r.is_nofollow,
                       r.redirect_url_id, r.canonical_url_id, r.outlink_count
                FROM urls u LEFT JOIN responses r ON r.url_id = u.id
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetInt32(0);
                var hasResponse = reader.GetBoolean(5);
                var notChecked = hasResponse && !reader.IsDBNull(6) && reader.GetString(6).StartsWith(RetryingFetcher.NOT_CHECKED_ERROR, StringComparison.Ordinal);
                var fetched = hasResponse && !notChecked;
                var outcome = hasResponse ? (FetchOutcome?)reader.GetInt32(7) : null;
                var status = fetched && !reader.IsDBNull(8) && reader.GetInt32(8) > 0 ? reader.GetInt32(8) : (int?)null;
                var requested = fetched && outcome != FetchOutcome.RobotsDisallowed;
                var read = fetched && !reader.IsDBNull(14);
                var depth = reader.GetInt32(4);
                var s = summary[id];
                string? Text(int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
                long? Number(int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);

                statuses[id] = (fetched, status);
                rows[id] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["id"] = (long)id,
                    ["url"] = reader.GetString(1),
                    ["fetched"] = fetched,
                    ["isInternal"] = reader.GetBoolean(2),
                    ["source"] = Name<DiscoverySource>(reader.GetInt32(3)),
                    ["depth"] = depth >= CrawlDepth.UNLINKED ? null : (long)depth,
                    ["type"] = Name(s.ResourceType),
                    ["statusCode"] = (long?)status,
                    ["statusClass"] = status switch
                    {
                        >= 200 and <= 299 => STATUS_CLASSES[0],
                        >= 300 and <= 399 => STATUS_CLASSES[1],
                        >= 400 and <= 499 => STATUS_CLASSES[2],
                        >= 500 and <= 599 => STATUS_CLASSES[3],
                        not null => STATUS_CLASSES[4],
                        null when fetched => STATUS_CLASSES[5],
                        _ => null
                    },
                    ["outcome"] = !hasResponse ? null : notChecked ? "notChecked" : Name(outcome!.Value),
                    ["redirectTargetId"] = reader.IsDBNull(20) ? null : reader.GetInt32(20),
                    ["indexable"] = s.IsIndexable,
                    ["indexability"] = s.IndexabilityReason is { } reason ? Name(reason) : null,
                    ["noindex"] = !reader.IsDBNull(18) && reader.GetBoolean(18) ? true : read ? false : null,
                    ["nofollow"] = !reader.IsDBNull(19) && reader.GetBoolean(19) ? true : read ? false : null,
                    ["canonicalUrl"] = reader.IsDBNull(21) ? null : urls[reader.GetInt32(21)],
                    ["contentType"] = fetched ? Text(9) : null,
                    ["title"] = Text(10),
                    ["description"] = Text(11),
                    ["h1"] = Text(12),
                    ["h1Count"] = read ? Number(13) : null,
                    ["wordCount"] = fetched ? Number(14) : null,
                    ["outlinks"] = read ? Number(22) : null,
                    ["sizeBytes"] = fetched ? Number(15) : null,
                    ["responseMs"] = requested ? Number(16) : null,
                    ["ttfbMs"] = requested ? Number(17) : null,
                    ["titleLength"] = fetched && Text(10) is { } title ? (long)DisplayWidth.Measure(title).Width : null,
                    ["descriptionLength"] = fetched && Text(11) is { } description ? (long)DisplayWidth.Measure(description).Width : null,
                    ["finalUrl"] = urls[s.FinalUrlId],
                    ["finalStatusCode"] = (long?)s.FinalStatusCode,
                    ["inSitemap"] = s.InSitemap,
                    ["inlinks"] = (long)s.Inlinks,
                    ["followedInlinks"] = (long)s.FollowedInlinks,
                    ["nofollowInlinks"] = (long)s.NofollowInlinks,
                    ["linkingPages"] = (long)s.LinkingPages,
                    ["issues"] = (long)s.Issues,
                    ["errors"] = (long)s.Errors,
                    ["warnings"] = (long)s.Warnings,
                    ["notices"] = (long)s.Notices
                };
            }
        }

        // An HTTP redirect's first hop, and the status there.
        foreach (var row in rows.Values)
        {
            var target = (int?)row["redirectTargetId"];
            row["redirectTarget"] = target is { } hop ? urls[hop] : null;
            row["redirectTargetStatusCode"] = target is { } at ? (long?)statuses[at].Status : null;
            row.Remove("redirectTargetId");
        }

        Dictionary<int, List<(string, string?, int?)>> issues = [];
        using (var connection = CrawlDatabase.OpenReader(path))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT issue, url_id, detail, related_url_id FROM issues";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetInt32(1);
                if (!issues.TryGetValue(id, out var list))
                {
                    issues[id] = list = [];
                }

                list.Add((reader.GetString(0), reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetInt32(3)));
            }
        }

        Dictionary<(string, int), List<int>> targets = [];
        Dictionary<(string, int), string> faults = [];
        using (var connection = CrawlDatabase.OpenReader(path))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT issue, url_id, related_url_id FROM issue_targets ORDER BY issue, url_id, related_url_id";
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var key = (reader.GetString(0), reader.GetInt32(1));
                    if (!targets.TryGetValue(key, out var list))
                    {
                        targets[key] = list = [];
                    }

                    list.Add(reader.GetInt32(2));
                }
            }

            command.CommandText = "SELECT issue, url_id, fault FROM issue_faults";
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    faults[(reader.GetString(0), reader.GetInt32(1))] = reader.GetString(2);
                }
            }
        }

        return new GridReference(rows, issues, targets, faults);
    }

    /// <summary>The ids of the rows a filter set matches, in a sort's order: unknown values last, crawl order breaking ties.</summary>
    public List<int> Order(IReadOnlyList<UrlSort> sort, IEnumerable<int> ids)
    {
        var keys = sort.ToList();
        var lastDescending = keys.Count > 0 && keys[^1].Descending;
        return [.. ids.Order(Comparer<int>.Create((a, b) =>
        {
            foreach (var key in keys)
            {
                var compared = Compare(SortValue(key.Column, Rows[a]), SortValue(key.Column, Rows[b]), key.Descending);
                if (compared != 0)
                {
                    return compared;
                }
            }

            return lastDescending ? b.CompareTo(a) : a.CompareTo(b);
        }))];
    }

    /// <summary>The rows every filter matches, as the contract defines each operator.</summary>
    public IEnumerable<int> Matching(IReadOnlyList<UrlFilter> filters, string? search = null) =>
        Rows.Keys.Where(id => filters.All(filter => Matches(filter, id)) &&
                              (search is null || Contains((string?)Rows[id]["url"], search) || Contains((string?)Rows[id]["title"], search)));

    private bool Matches(UrlFilter filter, int id)
    {
        if (filter.Column == "check")
        {
            var flagged = (Issues.GetValueOrDefault(id) ?? []).Where(issue => filter.Values!.Any(value => value.Text == issue.Issue));
            if (filter.Group is { } group)
            {
                var grouping = IssueCatalog.Find(filter.Values![0].Text!)!.Grouping;
                flagged = flagged.Where(issue => issue.Related == group.RelatedUrlId &&
                                                 (grouping == IssueGrouping.RelatedUrl || issue.Detail == group.Detail));
            }

            return filter.Operator == FilterOperator.In ? flagged.Any() : !flagged.Any();
        }

        var value = Rows[id][filter.Column];
        bool Positive(FilterOperator op) => op switch
        {
            FilterOperator.Eq => value is not null && Equal(value, filter.Value!.Value),
            FilterOperator.Lt => value is not null && Number(value) < filter.Value!.Value.Number,
            FilterOperator.Lte => value is not null && Number(value) <= filter.Value!.Value.Number,
            FilterOperator.Gt => value is not null && Number(value) > filter.Value!.Value.Number,
            FilterOperator.Gte => value is not null && Number(value) >= filter.Value!.Value.Number,
            FilterOperator.Between => value is not null && (filter.Min is null || Number(value) >= filter.Min) && (filter.Max is null || Number(value) <= filter.Max),
            FilterOperator.In => value is not null && filter.Values!.Any(candidate => Equal(value, candidate)),
            FilterOperator.Contains => Contains(value as string, filter.Value!.Value.Text!),
            FilterOperator.StartsWith => value is string start && Lower(start).StartsWith(Lower(filter.Value!.Value.Text!), StringComparison.Ordinal),
            FilterOperator.EndsWith => value is string end && Lower(end).EndsWith(Lower(filter.Value!.Value.Text!), StringComparison.Ordinal),
            FilterOperator.Regex => value is string text && new Regex(filter.Value!.Value.Text!, RegexOptions.NonBacktracking).IsMatch(text),
            FilterOperator.Is => value is bool flag && flag == filter.Value!.Value.Boolean,
            FilterOperator.IsEmpty => value is null || value as string == string.Empty,
            FilterOperator.IsNotEmpty => !(value is null || value as string == string.Empty),
            _ => throw new ArgumentOutOfRangeException(nameof(op), op, null)
        };

        return filter.Operator switch
        {
            FilterOperator.Ne => !Positive(FilterOperator.Eq),
            FilterOperator.NotIn => !Positive(FilterOperator.In),
            FilterOperator.NotContains => !Positive(FilterOperator.Contains),
            FilterOperator.NotRegex => !Positive(FilterOperator.Regex),
            var op => Positive(op)
        };
    }

    /// <summary>What a column sorts by: an enum by its number, a status class by its status code.</summary>
    private static object? SortValue(string column, Dictionary<string, object?> row)
    {
        if (column == "statusClass")
        {
            return row["statusCode"];
        }

        var info = UrlGridReader.IssueRowColumns.Single(candidate => candidate.Id == column);
        return info.Type == UrlColumnType.Enum && row[column] is string name ? (long)info.Values.ToList().IndexOf(name) : row[column];
    }

    /// <summary>Unknown last, whichever the direction.</summary>
    private static int Compare(object? a, object? b, bool descending)
    {
        if (a is null || b is null)
        {
            return (a is null).CompareTo(b is null);
        }

        var compared = (a, b) switch
        {
            (string x, string y) => string.CompareOrdinal(x, y),
            (bool x, bool y) => x.CompareTo(y),
            _ => Convert.ToInt64(a, CultureInfo.InvariantCulture).CompareTo(Convert.ToInt64(b, CultureInfo.InvariantCulture))
        };
        return descending ? -compared : compared;
    }

    private static bool Equal(object value, FilterValue filter) => value switch
    {
        string text => text == filter.Text,
        bool flag => flag == filter.Boolean,
        _ => Number(value) == filter.Number
    };

    private static double Number(object value) => Convert.ToDouble(value, CultureInfo.InvariantCulture);

    /// <summary>As SQLite's LIKE: the case of ASCII letters ignored, and only theirs.</summary>
    private static bool Contains(string? value, string part) => value is not null && Lower(value).Contains(Lower(part), StringComparison.Ordinal);

    private static string Lower(string text) => string.Concat(text.Select(character => character is >= 'A' and <= 'Z' ? (char)(character + 32) : character));

    private static string Name<TEnum>(int value) where TEnum : struct, Enum => Name((TEnum)Enum.ToObject(typeof(TEnum), value));

    private static string Name<TEnum>(TEnum value) where TEnum : struct, Enum => char.ToLowerInvariant(value.ToString()[0]) + value.ToString()[1..];
}
