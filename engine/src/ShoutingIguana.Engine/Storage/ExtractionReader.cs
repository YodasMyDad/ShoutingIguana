using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// What a crawl's custom extraction rules found, as its file stores it. Safe while the crawl runs, when the last
/// second's pages may not be stored yet. A file from before the table, or a crawl with no rules, has none.
/// </summary>
public static class ExtractionReader
{
    public const int DEFAULT_LIMIT = 100;
    public const int MAX_LIMIT = 500;

    public static ExtractionPage Read(string databasePath, ExtractionQuery query)
    {
        var options = CrawlDatabase.ReadOptions(databasePath);
        var rules = options.ExtractionRules ?? [];
        using var connection = CrawlDatabase.OpenReader(databasePath);
        using var snapshot = connection.BeginTransaction(deferred: true);
        if (!IssueReader.HasTable(connection, snapshot, "extractions"))
        {
            return new ExtractionPage(Counts(rules, []), 0, []);
        }

        var rule = string.IsNullOrWhiteSpace(query.Rule) ? null : query.Rule.Trim();
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : Like(query.Search.Trim());
        var offset = Math.Max(0, query.Offset);
        var limit = Math.Clamp(query.Limit <= 0 ? DEFAULT_LIMIT : query.Limit, 1, MAX_LIMIT);

        Dictionary<string, (long Matches, long Errors)> found = new(StringComparer.Ordinal);
        using (var counts = connection.CreateCommand())
        {
            counts.Transaction = snapshot;
            counts.CommandText = """
                SELECT name,
                       SUM(CASE WHEN error IS NULL THEN 1 ELSE 0 END),
                       SUM(CASE WHEN error IS NULL THEN 0 ELSE 1 END)
                FROM extractions GROUP BY name
                """;
            using var reader = counts.ExecuteReader();
            while (reader.Read())
            {
                found[reader.GetString(0)] = (reader.GetInt64(1), reader.GetInt64(2));
            }
        }

        var where = "WHERE ($rule IS NULL OR e.name = $rule) AND ($search IS NULL OR u.url LIKE $search ESCAPE '\\' OR IFNULL(e.value, '') LIKE $search ESCAPE '\\')";
        long total;
        using (var count = connection.CreateCommand())
        {
            count.Transaction = snapshot;
            count.CommandText = $"SELECT COUNT(*) FROM extractions e JOIN urls u ON u.id = e.url_id {where}";
            Bind(count, rule, search);
            total = (long)(count.ExecuteScalar() ?? 0L);
        }

        List<ExtractionRow> rows = [];
        using (var command = connection.CreateCommand())
        {
            command.Transaction = snapshot;
            command.CommandText = $"""
                SELECT u.url, e.name, e.selector_type, e.selector, e.value, e.count, e.error
                FROM extractions e JOIN urls u ON u.id = e.url_id
                {where}
                ORDER BY u.url, e.name
                LIMIT $limit OFFSET $offset
                """;
            Bind(command, rule, search);
            command.Parameters.AddWithValue("$limit", limit);
            command.Parameters.AddWithValue("$offset", offset);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var type = reader.GetInt32(2);
                if (!Enum.IsDefined((ExtractionSelector)type))
                {
                    continue;
                }

                rows.Add(new ExtractionRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    (ExtractionSelector)type,
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetInt32(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6)));
            }
        }

        return new ExtractionPage(Counts(rules, found), total, rows);
    }

    private static void Bind(SqliteCommand command, string? rule, string? search)
    {
        command.Parameters.AddWithValue("$rule", (object?)rule ?? DBNull.Value);
        command.Parameters.AddWithValue("$search", (object?)search ?? DBNull.Value);
    }

    private static List<ExtractionRuleCount> Counts(IReadOnlyList<ExtractionRule> rules, Dictionary<string, (long Matches, long Errors)> found)
    {
        List<ExtractionRuleCount> counts = [];
        foreach (var rule in rules)
        {
            if (rule is null)
            {
                continue;
            }

            var trimmed = rule.Trimmed();
            found.TryGetValue(trimmed.Name, out var count);
            counts.Add(new ExtractionRuleCount(trimmed.Name, rule.SelectorType, trimmed.Selector, rule.Enabled, count.Matches, count.Errors));
        }

        return counts;
    }

    /// <summary>A LIKE pattern that matches the text, with its own <c>%</c> and <c>_</c> taken literally.</summary>
    private static string Like(string text)
    {
        var escaped = text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
        return $"%{escaped}%";
    }
}
