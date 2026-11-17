using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Serialization;
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
        SELECT i.issue, u.url, i.detail, r.url, i.url_id
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

    /// <summary>
    /// The checks the last analysis skipped, each with why, in catalog order. Empty before the first analysis, and for
    /// an analysis by an engine that didn't store reasons (its notes say why instead). A check the analysis didn't know,
    /// being newer than the engine that ran it, is listed as <see cref="SkippedCheckReasons.NEWER_THAN_ANALYSIS"/>; one
    /// an analysis by a newer engine than this didn't run isn't listed, as that engine dropped it and nothing is known.
    /// </summary>
    public static IReadOnlyList<SkippedCheck> ReadSkippedChecks(string databasePath)
    {
        using var connection = CrawlDatabase.OpenReader(databasePath);
        return ReadAnalysis(connection, transaction: null) is { } analysis ? ReadSkippedChecks(connection, transaction: null, analysis) : [];
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
    /// The last analysis with its counts, notes and skip reasons, and the crawl's stamp that says whether it still stands,
    /// all from one snapshot even if another analysis finishes, or the crawl is resumed, meanwhile. Null when the crawl
    /// has never been analysed.
    /// </summary>
    public static AnalysisResults? ReadResults(string databasePath)
    {
        using var connection = CrawlDatabase.OpenReader(databasePath);
        using var snapshot = connection.BeginTransaction(deferred: true);
        return ReadResults(connection, snapshot, () => SnapshotTaken?.Invoke(databasePath));
    }

    /// <summary>What <see cref="ReadResults(string)"/> returns, read through a snapshot another reader holds open.</summary>
    internal static AnalysisResults? ReadResults(SqliteConnection connection, SqliteTransaction snapshot, Action? analysisRead = null)
    {
        if (ReadAnalysis(connection, snapshot) is not { } analysis)
        {
            return null;
        }

        analysisRead?.Invoke();
        return new AnalysisResults(
            analysis,
            ReadCrawlUpdatedAt(connection, snapshot),
            ReadCounts(connection, snapshot),
            ReadNotes(connection, snapshot),
            ReadSkippedChecks(connection, snapshot, analysis))
        {
            Score = ReadScore(connection, snapshot, analysis)
        };
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

    internal static Dictionary<string, long> ReadCounts(SqliteConnection connection, SqliteTransaction? transaction)
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
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetInt32(4));
        }
    }

    /// <summary>
    /// The analysis row, with what it was run against where the engine that ran it stored that. A first analysis that was
    /// cancelled or failed leaves its tables behind without a row, which is no analysis.
    /// </summary>
    internal static AnalysisInfo? ReadAnalysis(SqliteConnection connection, SqliteTransaction? transaction)
    {
        if (!HasAnalysis(connection, transaction))
        {
            return null;
        }

        // By name: a file analysed by an earlier engine lacks the columns added since, which then read as null.
        using var command = Command(connection, transaction, "SELECT * FROM analysis WHERE id = 1");
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
            ChecksFingerprint = Text("checks_fingerprint"),
            CrawledUrlCount = Number("crawled_urls"),
            ScoreVersion = Number("score_version") is { } scoreVersion ? (int)scoreVersion : null,

            // A number no engine stores (a crafted file's) is no score.
            Score = Number("score") is >= 0 and <= 100 and var score
                ? new SiteScoreSummary((int)score, ScoreCalculator.BandOf((int)score), Number("score_partial") is not (null or 0))
                : null
        };
    }

    /// <summary>
    /// The whole score the analysis stored, or null when it stored none (it read no page, or an engine from before the
    /// score ran it) or stored one this engine can't read (a later engine's, with values it doesn't know). It is the
    /// headline's own: one that says another number, which no analysis stores, is no score either, so a list of crawls and
    /// the analysis never give two.
    /// </summary>
    private static SiteScore? ReadScore(SqliteConnection connection, SqliteTransaction? transaction, AnalysisInfo analysis)
    {
        // The headline is read from the same row: without one there is no score, whatever else the row holds.
        if (analysis.Score is not { } headline)
        {
            return null;
        }

        // By name, as the analysis itself is read: a file may lack the column.
        string? json = null;
        using (var command = Command(connection, transaction, "SELECT * FROM analysis WHERE id = 1"))
        using (var reader = command.ExecuteReader())
        {
            if (reader.Read())
            {
                var column = Enumerable.Range(0, reader.FieldCount).FirstOrDefault(ordinal => reader.GetName(ordinal).Equals("score_json", StringComparison.OrdinalIgnoreCase), -1);
                json = column >= 0 && !reader.IsDBNull(column) ? reader.GetValue(column) as string : null;
            }
        }

        if (json is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(json, EngineJsonContext.Default.SiteScore) is { Covers: not null, Lines: not null, IfFixed.CheckIds: not null } score &&
                   score.Lines.All(line => line?.CheckId is not null) && score.IfFixed.CheckIds.All(id => id is not null) &&
                   (score.Value, score.Partial) == (headline.Value, headline.Partial)
                ? score
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Files analysed by an older engine may lack tables added since.</summary>
    internal static bool HasTable(SqliteConnection connection, SqliteTransaction? transaction, string name)
    {
        using var command = Command(connection, transaction, "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name");
        command.Parameters.AddWithValue("$name", name);
        return command.ExecuteScalar() is not null;
    }

    private static bool HasAnalysis(SqliteConnection connection, SqliteTransaction? transaction) => HasTable(connection, transaction, "analysis");

    /// <summary>The crawl's stamp: when its status last changed (<see cref="CrawlFileSummary.UpdatedAt"/>).</summary>
    internal static DateTimeOffset ReadCrawlUpdatedAt(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = Command(connection, transaction, "SELECT updated_at FROM crawl WHERE id = 1");
        return command.ExecuteScalar() is long updatedAt
            ? DateTimeOffset.FromUnixTimeMilliseconds(updatedAt)
            : throw new InvalidOperationException("This file is not a crawl database.");
    }

    /// <summary>
    /// What <see cref="ReadSkippedChecks(string)"/> returns, for the analysis read through the same connection and
    /// transaction. An analysis by an engine from before the reasons stored none, and a skipped_checks table left by a
    /// later engine's earlier analysis isn't its own.
    /// </summary>
    private static List<SkippedCheck> ReadSkippedChecks(SqliteConnection connection, SqliteTransaction? transaction, AnalysisInfo analysis)
    {
        if (analysis.AnalysisVersion is null)
        {
            return [];
        }

        Dictionary<string, SkippedCheck> stored = new(StringComparer.Ordinal);
        HashSet<string> ran = new(StringComparer.Ordinal);
        using (var command = Command(connection, transaction, "SELECT check_id, reason, text FROM skipped_checks; SELECT id FROM issue_types;"))
        {
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

        // An analysis that knew a check either ran it or stored why not, so one it neither ran nor skipped it didn't know:
        // newer than its engine, unless that engine is newer than this one, when the check is one it no longer has. (A
        // check can be added without a new analysis version, so one of the same version may still be newer.) The newer
        // engine's own checks follow the catalog's.
        var droppedSince = analysis.AnalysisVersion > AnalysisFreshness.VERSION;
        List<SkippedCheck> skipped =
        [
            .. IssueCatalog.All.Where(check => !ran.Contains(check.Id))
                .Select(check => stored.GetValueOrDefault(check.Id) ?? (droppedSince ? null
                    : new SkippedCheck(check.Id, SkippedCheckReasons.NEWER_THAN_ANALYSIS, "This check is newer than the analysis. Analyse the crawl again to run it.")))
                .OfType<SkippedCheck>()
        ];
        skipped.AddRange(stored.Values.Where(check => IssueCatalog.Find(check.CheckId) is null).OrderBy(check => check.CheckId, StringComparer.Ordinal));
        return skipped;
    }

    /// <summary>
    /// The settings as stored, a threshold the analysing engine didn't have taking its default, and the title and
    /// description limits an older engine stored in characters converted to pixels (<see cref="LegacyAnalysisSettings"/>);
    /// settings that can't be read (edited by hand, say) are none, so they differ from any.
    /// </summary>
    private static AnalysisSettings? ReadSettings(string? json)
    {
        if (json is null)
        {
            return null;
        }

        try
        {
            var stored = JsonNode.Parse(json) is JsonObject settings ? LegacyAnalysisSettings.Upgrade(settings).ToJsonString() : json;
            return StoredJson.Deserialize(stored, EngineJsonContext.Default.AnalysisSettings, new AnalysisSettings());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        return command;
    }
}
