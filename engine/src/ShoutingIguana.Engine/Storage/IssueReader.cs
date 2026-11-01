using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// Reads the results of the last analysis of a crawl. An analysis replaces the previous one in a single transaction, so
/// the snapshot readers (<see cref="ReadResults"/>, <see cref="ReadIssuePage"/>) never mix two; separate calls can, if an
/// analysis finishes between them.
/// </summary>
public static class IssueReader
{
    /// <summary>Tests only: called once a snapshot reader has read the analysis, before it reads the rest.</summary>
    internal static Action<string>? SnapshotTaken { get; set; }

    private const string SELECT_ROWS = """
        SELECT i.issue, u.url, i.detail, r.url
        FROM issues i
        JOIN urls u ON u.id = i.url_id
        LEFT JOIN urls r ON r.id = i.related_url_id
        """;

    /// <summary>The last analysis, or null when the crawl has never been analysed.</summary>
    public static AnalysisInfo? ReadAnalysis(string databasePath)
    {
        using var connection = CrawlDatabase.OpenReader(databasePath);
        return ReadAnalysis(connection, transaction: null);
    }

    /// <summary>Flagged URL counts per issue id. Checks that ran and found nothing are included with 0.</summary>
    public static IReadOnlyDictionary<string, long> ReadCounts(string databasePath)
    {
        using var connection = CrawlDatabase.OpenReader(databasePath);
        return ReadCounts(connection, transaction: null);
    }

    /// <summary>What the last analysis couldn't do and why, e.g. orphans skipped on a depth-limited crawl.</summary>
    public static IReadOnlyList<string> ReadNotes(string databasePath)
    {
        using var connection = CrawlDatabase.OpenReader(databasePath);
        return ReadNotes(connection, transaction: null);
    }

    /// <summary>
    /// The last analysis with its counts and notes, all from that one analysis even if another finishes meanwhile. Null
    /// when the crawl has never been analysed.
    /// </summary>
    public static AnalysisResults? ReadResults(string databasePath)
    {
        using var connection = CrawlDatabase.OpenReader(databasePath);
        using var snapshot = connection.BeginTransaction(deferred: true);
        if (ReadAnalysis(connection, snapshot) is not { } analysis)
        {
            return null;
        }

        SnapshotTaken?.Invoke(databasePath);
        return new AnalysisResults(analysis, ReadCounts(connection, snapshot), ReadNotes(connection, snapshot));
    }

    /// <summary>One page of the URLs flagged with an issue, in crawl order.</summary>
    public static IReadOnlyList<IssueRow> ReadIssues(string databasePath, string issueId, int limit, int offset = 0)
    {
        using var connection = CrawlDatabase.OpenReader(databasePath);
        return HasAnalysis(connection, transaction: null) ? ReadIssues(connection, transaction: null, issueId, limit, offset) : [];
    }

    /// <summary>
    /// One page of the URLs flagged with an issue, in crawl order, with the issue's total from the same analysis. Null
    /// when the crawl has never been analysed.
    /// </summary>
    public static IssuePage? ReadIssuePage(string databasePath, string issueId, int limit, int offset = 0)
    {
        using var connection = CrawlDatabase.OpenReader(databasePath);
        using var snapshot = connection.BeginTransaction(deferred: true);
        if (ReadAnalysis(connection, snapshot) is null)
        {
            return null;
        }

        SnapshotTaken?.Invoke(databasePath);
        using var command = Command(connection, snapshot, "SELECT url_count FROM issue_types WHERE id = $issue");
        command.Parameters.AddWithValue("$issue", issueId);
        return command.ExecuteScalar() is long total
            ? new IssuePage(total, ReadIssues(connection, snapshot, issueId, limit, offset))
            : new IssuePage(null, []);
    }

    /// <summary>Every flagged URL (or those for one issue), streamed in issue then crawl order, for exports.</summary>
    public static IEnumerable<IssueRow> ReadAll(string databasePath, string? issueId = null)
    {
        using var connection = CrawlDatabase.OpenReader(databasePath);
        if (!HasAnalysis(connection, transaction: null))
        {
            yield break;
        }

        using var command = connection.CreateCommand();
        command.CommandText = $"{SELECT_ROWS} ORDER BY i.issue, i.url_id";
        if (issueId is not null)
        {
            command.CommandText = $"{SELECT_ROWS} WHERE i.issue = $issue ORDER BY i.url_id";
            command.Parameters.AddWithValue("$issue", issueId);
        }
        foreach (var row in ReadRows(command))
        {
            yield return row;
        }
    }

    private static AnalysisInfo? ReadAnalysis(SqliteConnection connection, SqliteTransaction? transaction)
    {
        if (!HasAnalysis(connection, transaction))
        {
            return null;
        }

        using var command = Command(connection, transaction, "SELECT analyzed_at, engine_version, pages, duration_ms FROM analysis WHERE id = 1");
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new AnalysisInfo(
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
                reader.GetString(1),
                reader.GetInt64(2),
                TimeSpan.FromMilliseconds(reader.GetInt64(3)))
            : null;
    }

    private static Dictionary<string, long> ReadCounts(SqliteConnection connection, SqliteTransaction? transaction)
    {
        Dictionary<string, long> counts = new(StringComparer.Ordinal);
        if (!HasAnalysis(connection, transaction))
        {
            return counts;
        }

        using var command = Command(connection, transaction, "SELECT id, url_count FROM issue_types");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            counts[reader.GetString(0)] = reader.GetInt64(1);
        }

        return counts;
    }

    private static List<string> ReadNotes(SqliteConnection connection, SqliteTransaction? transaction)
    {
        List<string> notes = [];
        if (!HasTable(connection, transaction, "analysis_notes"))
        {
            return notes;
        }

        using var command = Command(connection, transaction, "SELECT note FROM analysis_notes ORDER BY id");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            notes.Add(reader.GetString(0));
        }

        return notes;
    }

    private static List<IssueRow> ReadIssues(SqliteConnection connection, SqliteTransaction? transaction, string issueId, int limit, int offset)
    {
        using var command = Command(connection, transaction, $"{SELECT_ROWS} WHERE i.issue = $issue ORDER BY i.url_id LIMIT $limit OFFSET $offset");
        command.Parameters.AddWithValue("$issue", issueId);
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);
        return [.. ReadRows(command)];
    }

    private static IEnumerable<IssueRow> ReadRows(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            yield return new IssueRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3));
        }
    }

    private static bool HasAnalysis(SqliteConnection connection, SqliteTransaction? transaction) => HasTable(connection, transaction, "analysis");

    /// <summary>Files analysed by an older engine may lack tables added since.</summary>
    private static bool HasTable(SqliteConnection connection, SqliteTransaction? transaction, string name)
    {
        using var command = Command(connection, transaction, "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name");
        command.Parameters.AddWithValue("$name", name);
        return command.ExecuteScalar() is not null;
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        return command;
    }
}
