using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// Writes analysis results. The CrawlWriter owns the file while a crawl runs; this writer only opens it when no
/// crawl is writing. Everything goes into one transaction that replaces the previous analysis, so readers see
/// either the old issues or the new ones, never a mix, and a failed analysis leaves the old results in place.
/// </summary>
internal sealed class IssueWriter : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqliteTransaction _transaction;
    private readonly SqliteCommand _insert;
    private readonly SqliteParameter _issue;
    private readonly SqliteParameter _urlId;
    private readonly SqliteParameter _detail;
    private readonly SqliteParameter _relatedUrlId;
    private readonly ILogger _logger;

    private IssueWriter(SqliteConnection connection, ILogger logger)
    {
        _connection = connection;
        _logger = logger;
        _transaction = connection.BeginTransaction();
        CrawlDatabase.Execute(connection, "DELETE FROM issues; DELETE FROM issue_types; DELETE FROM analysis;", _transaction);

        _insert = connection.CreateCommand();
        _insert.Transaction = _transaction;
        _insert.CommandText = """
            INSERT OR IGNORE INTO issues (issue, url_id, detail, related_url_id)
            VALUES ($issue, $urlId, $detail, $relatedUrlId)
            """;
        _issue = _insert.Parameters.Add("$issue", SqliteType.Text);
        _urlId = _insert.Parameters.Add("$urlId", SqliteType.Integer);
        _detail = _insert.Parameters.Add("$detail", SqliteType.Text);
        _relatedUrlId = _insert.Parameters.Add("$relatedUrlId", SqliteType.Integer);
        _insert.Prepare();
    }

    public static IssueWriter Open(string databasePath, ILogger logger)
    {
        var connection = CrawlDatabase.OpenWriter(databasePath, allowNew: false);
        try
        {
            CrawlDatabase.Execute(connection, CrawlSchema.ANALYSIS);
            return new IssueWriter(connection, logger);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>Stores one flagged URL. A second hit for the same issue and URL is ignored.</summary>
    public void Add(IssueHit hit)
    {
        _issue.Value = hit.Issue.Id;
        _urlId.Value = hit.UrlId;
        _detail.Value = (object?)hit.Detail ?? DBNull.Value;
        _relatedUrlId.Value = hit.RelatedUrlId is { } related ? related : DBNull.Value;
        _insert.ExecuteNonQuery();
    }

    /// <summary>Records every check that ran with its URL count, plus the analysis itself, then commits.</summary>
    /// <returns>Flagged URL counts per issue id.</returns>
    public Dictionary<string, long> Commit(IReadOnlyList<IssueDefinition> catalog, AnalysisInfo info, string settingsJson)
    {
        Dictionary<string, long> counts = new(StringComparer.Ordinal);
        using (var count = Command("SELECT issue, COUNT(*) FROM issues GROUP BY issue"))
        using (var reader = count.ExecuteReader())
        {
            while (reader.Read())
            {
                counts[reader.GetString(0)] = reader.GetInt64(1);
            }
        }

        using (var type = Command("""
                   INSERT INTO issue_types (id, category, severity, title, description, fix, url_count)
                   VALUES ($id, $category, $severity, $title, $description, $fix, $count)
                   """))
        {
            foreach (var issue in catalog)
            {
                type.Parameters.Clear();
                type.Parameters.AddWithValue("$id", issue.Id);
                type.Parameters.AddWithValue("$category", issue.Category.ToString());
                type.Parameters.AddWithValue("$severity", issue.Severity.ToString());
                type.Parameters.AddWithValue("$title", issue.Title);
                type.Parameters.AddWithValue("$description", issue.Description);
                type.Parameters.AddWithValue("$fix", issue.Fix);
                type.Parameters.AddWithValue("$count", counts.GetValueOrDefault(issue.Id));
                type.ExecuteNonQuery();
            }
        }

        using (var analysis = Command("""
                   INSERT INTO analysis (id, analyzed_at, engine_version, settings_json, pages, duration_ms)
                   VALUES (1, $analyzedAt, $engineVersion, $settings, $pages, $durationMs)
                   """))
        {
            analysis.Parameters.AddWithValue("$analyzedAt", info.AnalyzedAt.ToUnixTimeMilliseconds());
            analysis.Parameters.AddWithValue("$engineVersion", info.EngineVersion);
            analysis.Parameters.AddWithValue("$settings", settingsJson);
            analysis.Parameters.AddWithValue("$pages", info.PagesAnalyzed);
            analysis.Parameters.AddWithValue("$durationMs", (long)info.Duration.TotalMilliseconds);
            analysis.ExecuteNonQuery();
        }

        _transaction.Commit();
        return counts;
    }

    /// <summary>Rolls back unless committed, then folds the write-ahead log and closes the file.</summary>
    public void Dispose()
    {
        _insert.Dispose();
        _transaction.Dispose();
        CrawlDatabase.CloseWriter(_connection, _logger);
    }

    private SqliteCommand Command(string sql)
    {
        var command = _connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = sql;
        return command;
    }
}
