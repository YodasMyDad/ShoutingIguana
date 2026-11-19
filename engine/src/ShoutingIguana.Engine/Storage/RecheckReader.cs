using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// A crawl's rechecks (the fix loop), as its file keeps them: each with its "before" and, once the analysis after it settled
/// it, its result; and the URLs of one. Read only, in one snapshot. A file never rechecked (or from an engine before
/// rechecks) has none, which isn't an error.
/// </summary>
public static class RecheckReader
{
    /// <summary>The most URLs a page of a recheck's holds.</summary>
    public const int MAX_LIMIT = 1_000;

    // A recheck recorded as failed (by an earlier engine: one that fails now is rolled back, and leaves no row) judged
    // nothing it can stand by. The score the analysis after it stored isn't its doing, so it has none.
    private static readonly string COLUMNS = $"""
        k.id, k.status, k.started_at, k.ended_at, k.settled_at,
        (SELECT SUM(t.crawled_ms) FROM run_time t WHERE t.run = k.run), k.check_id, k.group_detail, k.group_related_url_id, k.url_list, k.kinds,
        k.judged, k.fetched, k.count_before, k.fixed, k.still, k.not_checked, k.not_rechecked, k.new, k.score_before,
        CASE WHEN k.status = {(int)RecheckStatus.Failed} THEN NULL ELSE k.score_after END
        """;

    /// <summary>Every recheck of the crawl, newest first.</summary>
    /// <exception cref="InvalidOperationException">The file isn't a crawl this engine can read.</exception>
    public static IReadOnlyList<RecheckRecord> ReadAll(string databasePath)
    {
        using var connection = CrawlDatabase.OpenReader(databasePath);
        using var snapshot = connection.BeginTransaction(deferred: true);
        return HasTables(connection, snapshot) ? Records(connection, snapshot, $"SELECT {COLUMNS} FROM rechecks k ORDER BY k.id DESC") : [];
    }

    /// <summary>
    /// How many rechecks a crawl has, and the latest, in one snapshot on a connection a reader already has open (a list of
    /// crawls reads it with the crawl's summary): none for a file never rechecked.
    /// </summary>
    internal static (int Count, RecheckRecord? Latest) ReadLatest(SqliteConnection connection)
    {
        using var snapshot = connection.BeginTransaction(deferred: true);
        if (!HasTables(connection, snapshot))
        {
            return (0, null);
        }

        var count = Convert.ToInt32(Scalar(connection, snapshot, "SELECT COUNT(*) FROM rechecks"), System.Globalization.CultureInfo.InvariantCulture);
        return (count, count == 0 ? null : Records(connection, snapshot, $"SELECT {COLUMNS} FROM rechecks k ORDER BY k.id DESC LIMIT 1").Single());
    }

    /// <summary>One recheck, or null when the file has none by that number.</summary>
    public static RecheckRecord? Read(string databasePath, int id)
    {
        using var connection = CrawlDatabase.OpenReader(databasePath);
        using var snapshot = connection.BeginTransaction(deferred: true);
        return HasTables(connection, snapshot) ? Records(connection, snapshot, $"SELECT {COLUMNS} FROM rechecks k WHERE k.id = $id", id).SingleOrDefault() : null;
    }

    /// <summary>
    /// A page of one recheck's URLs, judged first and then in crawl order, narrowed to the outcomes and roles asked for (all
    /// when null), with what the check says of each now. Null when the file has no recheck by that number.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The offset is negative, or the limit isn't 1 to <see cref="MAX_LIMIT"/>.</exception>
    public static RecheckRowPage? ReadRows(string databasePath, int id, IReadOnlyCollection<RecheckOutcome>? outcomes, IReadOnlyCollection<RecheckRole>? roles,
        int offset, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MAX_LIMIT);
        using var connection = CrawlDatabase.OpenReader(databasePath);
        using var snapshot = connection.BeginTransaction(deferred: true);
        if (!HasTables(connection, snapshot) || Scalar(connection, snapshot, "SELECT check_id FROM rechecks WHERE id = $id", id) is not { } checkValue)
        {
            return null;
        }

        var checkId = checkValue is string { Length: > 0 } named ? named : null;
        var where = "ru.recheck_id = $id" +
                    (outcomes is null ? string.Empty : $" AND ru.outcome IN ({string.Join(", ", outcomes.Select(outcome => (int)outcome).DefaultIfEmpty(-1))})") +
                    (roles is null ? string.Empty : $" AND ru.role IN ({string.Join(", ", roles.Select(role => (int)role).DefaultIfEmpty(-1))})");
        var total = Convert.ToInt32(Scalar(connection, snapshot, $"SELECT COUNT(*) FROM recheck_urls ru WHERE {where}", id), System.Globalization.CultureInfo.InvariantCulture);

        List<RecheckRow> rows = [];
        using (var command = Command(connection, snapshot, $"""
                   SELECT ru.url_id, u.url, ru.role, ru.outcome, ru.flagged_before, ru.detail_before,
                          (SELECT i.detail FROM issues i WHERE i.issue = $check AND i.url_id = ru.url_id),
                          EXISTS (SELECT 1 FROM issues i WHERE i.issue = $check AND i.url_id = ru.url_id),
                          r.status_code
                   FROM recheck_urls ru JOIN urls u ON u.id = ru.url_id LEFT JOIN responses r ON r.url_id = ru.url_id
                   WHERE {where}
                   ORDER BY ru.role = {(int)RecheckRole.Judged} DESC, ru.url_id
                   LIMIT $limit OFFSET $offset
                   """, id))
        {
            command.Parameters.AddWithValue("$check", (object?)checkId ?? DBNull.Value);
            command.Parameters.AddWithValue("$limit", limit);
            command.Parameters.AddWithValue("$offset", offset);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new RecheckRow(reader.GetInt32(0), reader.GetString(1), (RecheckRole)reader.GetInt32(2),
                    reader.IsDBNull(3) ? null : (RecheckOutcome)reader.GetInt32(3), reader.GetBoolean(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetBoolean(7) ? reader.IsDBNull(6) ? string.Empty : reader.GetString(6) : null, reader.IsDBNull(8) ? null : reader.GetInt32(8)));
            }
        }

        return new RecheckRowPage(total, offset, rows);
    }

    /// <summary>Whether the file has rechecks at all: the tables are made the first time one runs.</summary>
    internal static bool HasTables(SqliteConnection connection, SqliteTransaction? transaction) =>
        Scalar(connection, transaction, "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'rechecks'") is not null;

    private static List<RecheckRecord> Records(SqliteConnection connection, SqliteTransaction snapshot, string sql, int? id = null)
    {
        var answers = Answers(connection, snapshot, id);
        List<RecheckRecord> records = [];
        using var command = Command(connection, snapshot, sql, id);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            static DateTimeOffset? Time(SqliteDataReader reader, int column) => reader.IsDBNull(column) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(column));
            static int? Int(SqliteDataReader reader, int column) => reader.IsDBNull(column) ? null : reader.GetInt32(column);

            var settledAt = Time(reader, 4);
            IssueGroupKey? group = reader.IsDBNull(7) && reader.IsDBNull(8) ? null : new IssueGroupKey(reader.IsDBNull(7) ? null : reader.GetString(7), Int(reader, 8));
            records.Add(new RecheckRecord(
                reader.GetInt32(0),
                (RecheckStatus)reader.GetInt32(1),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(2)),
                Time(reader, 3),
                settledAt,
                reader.IsDBNull(5) ? null : reader.GetInt64(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                group,
                reader.GetBoolean(9),
                [.. reader.GetString(10).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Enum.Parse<RecheckKind>)],
                reader.GetInt32(11),
                reader.GetInt32(12),
                reader.IsDBNull(13) ? null : reader.GetInt64(13),
                settledAt is null ? null
                    : new RecheckResult(Int(reader, 14) ?? 0, Int(reader, 15) ?? 0, Int(reader, 16) ?? 0, Int(reader, 17) ?? 0, Int(reader, 18) ?? 0,
                        Int(reader, 19), Int(reader, 20)))
            {
                Answers = answers.TryGetValue(reader.GetInt32(0), out var counts)
                    ? new RecheckAnswerCounts(counts.Read, counts.RateLimited, counts.Blocked, counts.RobotsTxt, counts.NoAnswer,
                        counts.NotAsked + Math.Max(0, reader.GetInt32(11) - counts.Rows))
                    : null
            });
        }

        return records;
    }

    /// <summary>
    /// How the site answered each recheck (or the one asked for) for the URLs it judges, counted: the judged URLs past its
    /// cap have no row, and count as never asked. None for a recheck whose rows have no answer (an earlier engine's), nor
    /// in a file whose table has no column for it.
    /// </summary>
    private static Dictionary<int, (int Read, int RateLimited, int Blocked, int RobotsTxt, int NoAnswer, int NotAsked, int Rows)> Answers(
        SqliteConnection connection, SqliteTransaction snapshot, int? id)
    {
        Dictionary<int, (int Read, int RateLimited, int Blocked, int RobotsTxt, int NoAnswer, int NotAsked, int Rows)> answers = [];
        if (Scalar(connection, snapshot, "SELECT COUNT(*) FROM pragma_table_info('recheck_urls') WHERE name = 'answer'") is not > 0L)
        {
            return answers;
        }

        using var command = Command(connection, snapshot, $"""
            SELECT recheck_id, answer, COUNT(*) FROM recheck_urls
            WHERE role = {(int)RecheckRole.Judged} AND answer IS NOT NULL{(id is null ? string.Empty : " AND recheck_id = $id")}
            GROUP BY recheck_id, answer
            """, id);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var recheck = reader.GetInt32(0);
            var count = reader.GetInt32(2);
            var (read, rateLimited, blocked, robotsTxt, noAnswer, notAsked, rows) = answers.GetValueOrDefault(recheck);
            answers[recheck] = (RecheckAnswer)reader.GetInt32(1) switch
            {
                RecheckAnswer.Read => (read + count, rateLimited, blocked, robotsTxt, noAnswer, notAsked, rows + count),
                RecheckAnswer.RateLimited => (read, rateLimited + count, blocked, robotsTxt, noAnswer, notAsked, rows + count),
                RecheckAnswer.Blocked => (read, rateLimited, blocked + count, robotsTxt, noAnswer, notAsked, rows + count),
                RecheckAnswer.RobotsTxt => (read, rateLimited, blocked, robotsTxt + count, noAnswer, notAsked, rows + count),
                RecheckAnswer.NoAnswer => (read, rateLimited, blocked, robotsTxt, noAnswer + count, notAsked, rows + count),
                _ => (read, rateLimited, blocked, robotsTxt, noAnswer, notAsked + count, rows + count)
            };
        }

        return answers;
    }

    private static object? Scalar(SqliteConnection connection, SqliteTransaction? transaction, string sql, int? id = null)
    {
        using var command = Command(connection, transaction, sql, id);
        var value = command.ExecuteScalar();
        return value is DBNull ? string.Empty : value;
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql, int? id = null)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        if (id is { } value)
        {
            command.Parameters.AddWithValue("$id", value);
        }

        return command;
    }
}
