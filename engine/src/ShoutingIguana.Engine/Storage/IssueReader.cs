using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>Reads the results of the last analysis of a crawl.</summary>
public static class IssueReader
{
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
        if (!HasAnalysis(connection))
        {
            return null;
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT analyzed_at, engine_version, pages, duration_ms FROM analysis WHERE id = 1";
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new AnalysisInfo(
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
                reader.GetString(1),
                reader.GetInt64(2),
                TimeSpan.FromMilliseconds(reader.GetInt64(3)))
            : null;
    }

    /// <summary>Flagged URL counts per issue id. Checks that ran and found nothing are included with 0.</summary>
    public static IReadOnlyDictionary<string, long> ReadCounts(string databasePath)
    {
        using var connection = CrawlDatabase.OpenReader(databasePath);
        Dictionary<string, long> counts = new(StringComparer.Ordinal);
        if (!HasAnalysis(connection))
        {
            return counts;
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, url_count FROM issue_types";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            counts[reader.GetString(0)] = reader.GetInt64(1);
        }

        return counts;
    }

    /// <summary>What the last analysis couldn't do and why, e.g. orphans skipped on a depth-limited crawl.</summary>
    public static IReadOnlyList<string> ReadNotes(string databasePath)
    {
        using var connection = CrawlDatabase.OpenReader(databasePath);
        List<string> notes = [];
        if (!HasTable(connection, "analysis_notes"))
        {
            return notes;
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT note FROM analysis_notes ORDER BY id";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            notes.Add(reader.GetString(0));
        }

        return notes;
    }

    /// <summary>One page of the URLs flagged with an issue, in crawl order.</summary>
    public static IReadOnlyList<IssueRow> ReadIssues(string databasePath, string issueId, int limit, int offset = 0)
    {
        using var connection = CrawlDatabase.OpenReader(databasePath);
        if (!HasAnalysis(connection))
        {
            return [];
        }

        using var command = connection.CreateCommand();
        command.CommandText = $"{SELECT_ROWS} WHERE i.issue = $issue ORDER BY i.url_id LIMIT $limit OFFSET $offset";
        command.Parameters.AddWithValue("$issue", issueId);
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);
        return ReadRows(command).ToList();
    }

    /// <summary>Every flagged URL (or those for one issue), streamed in issue then crawl order, for exports.</summary>
    public static IEnumerable<IssueRow> ReadAll(string databasePath, string? issueId = null)
    {
        using var connection = CrawlDatabase.OpenReader(databasePath);
        if (!HasAnalysis(connection))
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

    private static bool HasAnalysis(SqliteConnection connection) => HasTable(connection, "analysis");

    /// <summary>Files analysed by an older engine may lack tables added since.</summary>
    private static bool HasTable(SqliteConnection connection, string name) =>
        CrawlDatabase.Scalar(connection, $"SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = '{name}'") is not null;
}
