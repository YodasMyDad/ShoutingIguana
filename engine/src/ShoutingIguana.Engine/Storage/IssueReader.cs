using System.Text.Json;
using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Serialization;
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
        return ReadAnalysis(connection);
    }

    /// <summary>
    /// The checks the last analysis skipped, each with why, in catalog order. Empty before the first analysis, and for
    /// an analysis by an engine that didn't store reasons (its notes say why instead). A check the analysis didn't know,
    /// being newer than the engine that ran it, is listed as <see cref="SkippedCheckReasons.NEWER_THAN_ANALYSIS"/>.
    /// </summary>
    public static IReadOnlyList<SkippedCheck> ReadSkippedChecks(string databasePath)
    {
        using var connection = CrawlDatabase.OpenReader(databasePath);
        if (ReadAnalysis(connection) is not { AnalysisVersion: not null })
        {
            return [];
        }

        Dictionary<string, SkippedCheck> stored = new(StringComparer.Ordinal);
        HashSet<string> ran = new(StringComparer.Ordinal);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT check_id, reason, text FROM skipped_checks; SELECT id FROM issue_types;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                stored[reader.GetString(0)] = new SkippedCheck(reader.GetString(0), reader.GetString(1), reader.GetString(2));
            }

            reader.NextResult();
            while (reader.Read())
            {
                ran.Add(reader.GetString(0));
            }
        }

        // An analysis that knew a check either ran it or stored why not; a newer engine's checks follow the catalog's.
        List<SkippedCheck> skipped =
        [
            .. IssueCatalog.All.Where(check => !ran.Contains(check.Id)).Select(check => stored.GetValueOrDefault(check.Id) ??
                new SkippedCheck(check.Id, SkippedCheckReasons.NEWER_THAN_ANALYSIS, "This check is newer than the analysis. Analyse the crawl again to run it."))
        ];
        skipped.AddRange(stored.Values.Where(check => IssueCatalog.Find(check.CheckId) is null).OrderBy(check => check.CheckId, StringComparer.Ordinal));
        return skipped;
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

    /// <summary>
    /// The analysis row, with what it was run against where the engine that ran it stored that. A first analysis that was
    /// cancelled or failed leaves its tables behind without a row, which is no analysis.
    /// </summary>
    internal static AnalysisInfo? ReadAnalysis(SqliteConnection connection)
    {
        if (!HasAnalysis(connection))
        {
            return null;
        }

        // By name: a file analysed by an earlier engine lacks the columns added since, which then read as null.
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM analysis WHERE id = 1";
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var columns = Enumerable.Range(0, reader.FieldCount).ToDictionary(reader.GetName, StringComparer.OrdinalIgnoreCase);
        long? Number(string name) => columns.TryGetValue(name, out var ordinal) && !reader.IsDBNull(ordinal) ? reader.GetInt64(ordinal) : null;
        string? Text(string name) => columns.TryGetValue(name, out var ordinal) && !reader.IsDBNull(ordinal) ? reader.GetString(ordinal) : null;

        return new AnalysisInfo(
            DateTimeOffset.FromUnixTimeMilliseconds(Number("analyzed_at")!.Value),
            Text("engine_version")!,
            Number("pages")!.Value,
            TimeSpan.FromMilliseconds(Number("duration_ms")!.Value))
        {
            IsPartial = Number("is_partial") is { } partial ? partial != 0 : null,
            Settings = ReadSettings(Text("settings_json")),
            CrawlUpdatedAt = Number("crawl_updated_at") is { } updatedAt ? DateTimeOffset.FromUnixTimeMilliseconds(updatedAt) : null,
            AnalysisVersion = Number("analysis_version") is { } version ? (int)version : null,
            ChecksFingerprint = Text("checks_fingerprint")
        };
    }

    /// <summary>Files analysed by an older engine may lack tables added since.</summary>
    internal static bool HasTable(SqliteConnection connection, string name) =>
        CrawlDatabase.Scalar(connection, $"SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = '{name}'") is not null;

    private static bool HasAnalysis(SqliteConnection connection) => HasTable(connection, "analysis");

    /// <summary>The settings as stored; settings that can't be read (edited by hand, say) are none, so they differ from any.</summary>
    private static AnalysisSettings? ReadSettings(string? json)
    {
        if (json is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(json, EngineJsonContext.Default.AnalysisSettings);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
