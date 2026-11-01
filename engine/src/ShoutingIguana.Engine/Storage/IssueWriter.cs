using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// Writes analysis results. The CrawlWriter owns the file while a crawl runs; this writer takes the same file lock,
/// so it only opens the file when no crawl is writing, and no crawl can start until it closes. Everything goes into
/// one transaction that replaces the previous analysis, so readers see either the old issues or the new ones, never
/// a mix, and a failed analysis leaves the old results in place.
/// </summary>
internal sealed class IssueWriter : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly CrawlFileLock _fileLock;
    private readonly SqliteTransaction _transaction;
    private readonly SqliteCommand _insert;
    private readonly SqliteParameter _issue;
    private readonly SqliteParameter _urlId;
    private readonly SqliteParameter _detail;
    private readonly SqliteParameter _relatedUrlId;
    private readonly ILogger _logger;

    private IssueWriter(SqliteConnection connection, CrawlFileLock fileLock, ILogger logger)
    {
        _connection = connection;
        _fileLock = fileLock;
        _logger = logger;
        _transaction = connection.BeginTransaction();
        CrawlDatabase.Execute(connection, """
            DELETE FROM issues; DELETE FROM issue_types; DELETE FROM analysis; DELETE FROM analysis_notes; DELETE FROM effective_indexability;
            DELETE FROM skipped_checks;
            """, _transaction);

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

    /// <exception cref="InvalidOperationException">
    /// A crawl is writing to the file, or it was interrupted and should be resumed first, or it isn't a crawl this
    /// engine can read.
    /// </exception>
    public static IssueWriter Open(string databasePath, ILogger logger)
    {
        // Before the lock and the writer, which would create the file (and its lock file) in passing.
        if (!File.Exists(databasePath))
        {
            throw new FileNotFoundException("Crawl database not found.", databasePath);
        }

        var fileLock = CrawlFileLock.Acquire(databasePath);
        SqliteConnection? connection = null;
        try
        {
            connection = CrawlDatabase.OpenWriter(databasePath, allowNew: false);

            // Checked under the lock, before anything is written: no crawl can start now, so the answer holds.
            if (CrawlDatabase.ReadStatus(connection) is CrawlStatus.Running or CrawlStatus.Paused)
            {
                throw new InvalidOperationException(
                    "This crawl is still running, or was interrupted. Finish it with --resume (or stop it) before analysing.");
            }

            CrawlDatabase.Execute(connection, CrawlSchema.ANALYSIS);
            AddMissingColumns(connection);
            return new IssueWriter(connection, fileLock, logger);
        }
        catch
        {
            if (connection is not null)
            {
                CrawlDatabase.CloseWriter(connection, logger);
            }

            fileLock.Dispose();
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

    /// <summary>Stores indexability the analysis judged differently from the crawl, for the stats and the UI.</summary>
    public void AddIndexability(IReadOnlyList<(int UrlId, IndexabilityReason Reason)> changes)
    {
        using var insert = Command("INSERT OR REPLACE INTO effective_indexability (url_id, indexability_reason) VALUES ($urlId, $reason)");
        var urlId = insert.Parameters.Add("$urlId", SqliteType.Integer);
        var reason = insert.Parameters.Add("$reason", SqliteType.Integer);
        foreach (var change in changes)
        {
            urlId.Value = change.UrlId;
            reason.Value = (int)change.Reason;
            insert.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Records every check that ran with its URL count, the URL summary, and the analysis itself with its notes and the
    /// checks it skipped, then commits.
    /// </summary>
    /// <param name="checks">The checks that ran; ones the crawl couldn't support are left out.</param>
    /// <param name="info">The analysis, with what it was run against (its version, partial or not).</param>
    /// <param name="skipped">The checks left out, and why.</param>
    /// <param name="urls">What the analysis judged of every URL, for the summary.</param>
    /// <returns>Flagged URL counts per issue id.</returns>
    public Dictionary<string, long> Commit(IReadOnlyList<IssueDefinition> checks, AnalysisInfo info, string settingsJson, IReadOnlyList<string> notes,
        IReadOnlyList<SkippedCheck> skipped, UrlTable urls)
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
            foreach (var issue in checks)
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

        // After the issue types: the summary counts each URL's issues by severity. It's part of the analysis's time.
        var summaryTime = Stopwatch.StartNew();
        var summaryRows = UrlSummaryBuilder.Build(_connection, _transaction, urls);
        _logger.LogDebug("Stored the URL summary of {Rows} URLs in {Elapsed}", summaryRows, summaryTime.Elapsed);

        // The crawl row's updated_at as it stands: the lock keeps any crawl out until this commits, so it's the one analysed.
        using (var analysis = Command("""
                   INSERT INTO analysis (id, analyzed_at, engine_version, settings_json, pages, duration_ms, is_partial, crawl_updated_at,
                                         analysis_version, checks_fingerprint)
                   VALUES (1, $analyzedAt, $engineVersion, $settings, $pages, $durationMs, $isPartial, (SELECT updated_at FROM crawl WHERE id = 1),
                           $analysisVersion, $checksFingerprint)
                   """))
        {
            analysis.Parameters.AddWithValue("$analyzedAt", info.AnalyzedAt.ToUnixTimeMilliseconds());
            analysis.Parameters.AddWithValue("$engineVersion", info.EngineVersion);
            analysis.Parameters.AddWithValue("$settings", settingsJson);
            analysis.Parameters.AddWithValue("$pages", info.PagesAnalyzed);
            analysis.Parameters.AddWithValue("$durationMs", (long)(info.Duration + summaryTime.Elapsed).TotalMilliseconds);
            analysis.Parameters.AddWithValue("$isPartial", (object?)info.IsPartial ?? DBNull.Value);
            analysis.Parameters.AddWithValue("$analysisVersion", (object?)info.AnalysisVersion ?? DBNull.Value);
            analysis.Parameters.AddWithValue("$checksFingerprint", (object?)info.ChecksFingerprint ?? DBNull.Value);
            analysis.ExecuteNonQuery();
        }

        using (var note = Command("INSERT INTO analysis_notes (note) VALUES ($note)"))
        {
            var text = note.Parameters.Add("$note", SqliteType.Text);
            foreach (var value in notes)
            {
                text.Value = value;
                note.ExecuteNonQuery();
            }
        }

        using (var skip = Command("INSERT INTO skipped_checks (check_id, reason, text) VALUES ($checkId, $reason, $text)"))
        {
            var checkId = skip.Parameters.Add("$checkId", SqliteType.Text);
            var reason = skip.Parameters.Add("$reason", SqliteType.Text);
            var text = skip.Parameters.Add("$text", SqliteType.Text);
            foreach (var check in skipped)
            {
                checkId.Value = check.CheckId;
                reason.Value = check.Reason;
                text.Value = check.Text;
                skip.ExecuteNonQuery();
            }
        }

        _transaction.Commit();
        return counts;
    }

    /// <summary>Rolls back unless committed, then folds the write-ahead log and closes the file.</summary>
    public void Dispose()
    {
        try
        {
            _insert.Dispose();
            _transaction.Dispose();
        }
        catch (SqliteException exception)
        {
            // Rolling back an analysis that failed part-way; SQLite may already have rolled it back itself.
            _logger.LogDebug(exception, "Rolling back the analysis reported an error");
        }
        finally
        {
            CrawlDatabase.CloseWriter(_connection, _logger);
            _fileLock.Dispose();
        }
    }

    /// <summary>A file analysed by an earlier engine has the analysis table without the columns added since.</summary>
    private static void AddMissingColumns(SqliteConnection connection)
    {
        HashSet<string> existing = new(StringComparer.OrdinalIgnoreCase);
        using (var columns = connection.CreateCommand())
        {
            columns.CommandText = "SELECT name FROM pragma_table_info('analysis')";
            using var reader = columns.ExecuteReader();
            while (reader.Read())
            {
                existing.Add(reader.GetString(0));
            }
        }

        foreach (var (name, type) in CrawlSchema.ANALYSIS_COLUMNS.Where(column => !existing.Contains(column.Name)))
        {
            CrawlDatabase.Execute(connection, $"ALTER TABLE analysis ADD COLUMN {name} {type}");
        }
    }

    private SqliteCommand Command(string sql)
    {
        var command = _connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = sql;
        return command;
    }
}
