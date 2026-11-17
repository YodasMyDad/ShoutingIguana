using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Serialization;
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
    /// <summary>Threads SQLite may sort with while it builds the links index, which takes a third of the time of one.</summary>
    private const int SORT_THREADS = 4;

    /// <summary>
    /// The memory each of those sorts in before it spills to a temporary file: about 100 MB in all, however many links
    /// (12 s for a million-page crawl's 50 million on 32 cores; 8 MB takes twice that, and 32 MB adds 200 MB for 1 s).
    /// </summary>
    private const int SORT_CACHE_KIB = 16_384;

    private readonly SqliteConnection _connection;
    private readonly CrawlFileLock _fileLock;
    private readonly SqliteTransaction _transaction;
    private readonly SqliteCommand _insert;
    private readonly SqliteParameter _issue;
    private readonly SqliteParameter _urlId;
    private readonly SqliteParameter _detail;
    private readonly SqliteParameter _relatedUrlId;
    private readonly SqliteCommand _insertTarget;
    private readonly SqliteParameter _targetIssue;
    private readonly SqliteParameter _targetId;
    private readonly SqliteParameter _targetUrlId;
    private readonly SqliteCommand _insertFault;
    private readonly SqliteParameter _faultIssue;
    private readonly SqliteParameter _fault;
    private readonly SqliteParameter _faultUrlId;
    private readonly ILogger _logger;

    private IssueWriter(SqliteConnection connection, CrawlFileLock fileLock, ILogger logger)
    {
        _connection = connection;
        _fileLock = fileLock;
        _logger = logger;
        _transaction = connection.BeginTransaction();
        CrawlDatabase.Execute(connection, """
            DELETE FROM issues; DELETE FROM issue_types; DELETE FROM analysis; DELETE FROM analysis_notes; DELETE FROM effective_indexability;
            DELETE FROM skipped_checks; DELETE FROM issue_targets; DELETE FROM issue_faults;
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

        _insertTarget = Command("INSERT OR IGNORE INTO issue_targets (issue, related_url_id, url_id) VALUES ($issue, $relatedUrlId, $urlId)");
        _targetIssue = _insertTarget.Parameters.Add("$issue", SqliteType.Text);
        _targetId = _insertTarget.Parameters.Add("$relatedUrlId", SqliteType.Integer);
        _targetUrlId = _insertTarget.Parameters.Add("$urlId", SqliteType.Integer);
        _insertTarget.Prepare();

        _insertFault = Command("INSERT OR IGNORE INTO issue_faults (issue, fault, url_id) VALUES ($issue, $fault, $urlId)");
        _faultIssue = _insertFault.Parameters.Add("$issue", SqliteType.Text);
        _fault = _insertFault.Parameters.Add("$fault", SqliteType.Text);
        _faultUrlId = _insertFault.Parameters.Add("$urlId", SqliteType.Integer);
        _insertFault.Prepare();
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

    /// <summary>
    /// Stores one flagged URL, with every other URL the issue is about and the fault it groups by. An issue of a check grouped
    /// by the other URL that names none is stored as naming <see cref="IssueHit.NO_OTHER_URL"/>, so the check's groups are
    /// all in one table. A second hit for the same issue and URL is ignored, its other URLs with it.
    /// </summary>
    public void Add(IssueHit hit)
    {
        _issue.Value = hit.Issue.Id;
        _urlId.Value = hit.UrlId;
        _detail.Value = (object?)hit.Detail ?? DBNull.Value;
        _relatedUrlId.Value = hit.RelatedUrlId is { } related ? related : DBNull.Value;
        if (_insert.ExecuteNonQuery() == 0)
        {
            return;
        }

        var targets = hit.AllTargets;
        if (targets.Count == 0 && hit.Issue.Grouping == IssueGrouping.RelatedUrl)
        {
            targets = [IssueHit.NO_OTHER_URL];
        }

        _targetIssue.Value = hit.Issue.Id;
        _targetUrlId.Value = hit.UrlId;
        foreach (var target in targets)
        {
            _targetId.Value = target;
            _insertTarget.ExecuteNonQuery();
        }

        if (hit.Issue.FaultOf is { } faultOf && hit.Detail is { } detail)
        {
            _faultIssue.Value = hit.Issue.Id;
            _fault.Value = faultOf(detail);
            _faultUrlId.Value = hit.UrlId;
            _insertFault.ExecuteNonQuery();
        }
    }

    /// <summary>Stores indexability the analysis judged differently from the crawl, for the stats and the UI.</summary>
    /// <summary>
    /// Takes back every issue already stored for these checks, with their other URLs and faults: the analysis skipped them
    /// after the pages they were found on had been read, as when a page's facts turn out older than a check needs.
    /// </summary>
    public void Discard(IReadOnlyList<IssueDefinition> checks)
    {
        foreach (var table in new[] { "issues", "issue_targets", "issue_faults" })
        {
            using var delete = Command($"DELETE FROM {table} WHERE issue = $issue");
            var issue = delete.Parameters.Add("$issue", SqliteType.Text);
            foreach (var check in checks)
            {
                issue.Value = check.Id;
                delete.ExecuteNonQuery();
            }
        }
    }

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
    /// Records every check that ran with its URL count, the URL summary, and the analysis itself with its notes, the
    /// checks it skipped and its score, then commits. The score is worked out here, from the counts this analysis stored,
    /// the pages it read and the URLs crawled as it read them: the file's lock keeps any crawl out until the commit, so
    /// they are all of one moment, and nothing else ever writes a score.
    /// </summary>
    /// <param name="checks">The checks that ran; ones the crawl couldn't support are left out.</param>
    /// <param name="info">The analysis, with what it was run against (its version, partial or not).</param>
    /// <param name="skipped">The checks left out, and why.</param>
    /// <param name="urls">What the analysis judged of every URL, for the summary.</param>
    /// <param name="sitemapsCovered">Every sitemap was read, so the summary can say a URL isn't in one.</param>
    /// <param name="incomplete">Why the crawl didn't read all of the site, or null when it did: the score is then partial.</param>
    /// <param name="progress">Told of the URL summary as it is built, then of the writing.</param>
    /// <returns>Flagged URL counts per issue id, the URLs crawled, and the score.</returns>
    public CommittedAnalysis Commit(IReadOnlyList<IssueDefinition> checks, AnalysisInfo info, string settingsJson, IReadOnlyList<string> notes,
        IReadOnlyList<SkippedCheck> skipped, UrlTable urls, bool sitemapsCovered, ScoreIncompleteReason? incomplete, AnalysisProgressReporter progress)
    {
        progress.Start(AnalysisPhase.UrlSummary, urls.Length - 1);
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
        var summaryRows = UrlSummaryBuilder.Build(_connection, _transaction, urls, sitemapsCovered, progress);
        _logger.LogDebug("Stored the URL summary of {Rows} URLs in {Elapsed}", summaryRows, summaryTime.Elapsed);
        progress.Finish(summaryRows);

        // The rest is quick, but for the commit, the links index on a first analysis, and the file folded back into one,
        // which the analyzer reports the end of.
        progress.Start(AnalysisPhase.Writing, 1);

        // The links grid's counts, unless the file has them for the crawl as it stands: one pass over the links, part of the
        // analysis's time (the summary's clock is still running).
        var linkTime = Stopwatch.StartNew();
        if (LinkSummaryBuilder.BuildIfChanged(_connection, _transaction) is { } linkRows)
        {
            _logger.LogDebug("Stored the link summary in {Rows} rows in {Elapsed}", linkRows, linkTime.Elapsed);
        }

        var crawledUrls = urls.CountCrawled();
        var score = ScoreCalculator.Calculate([.. checks.Select(issue => new IssueCount(issue, counts.GetValueOrDefault(issue.Id)))], skipped.Count,
            info.PagesAnalyzed, crawledUrls, incomplete, urls.CountBlockedByRobots());

        // The crawl row's updated_at as it stands: the lock keeps any crawl out until this commits, so it's the one analysed.
        using (var analysis = Command("""
                   INSERT INTO analysis (id, analyzed_at, engine_version, settings_json, pages, duration_ms, is_partial, crawl_updated_at,
                                         analysis_version, checks_fingerprint, crawled_urls, score_version, score, score_partial, score_json)
                   VALUES (1, $analyzedAt, $engineVersion, $settings, $pages, $durationMs, $isPartial, (SELECT updated_at FROM crawl WHERE id = 1),
                           $analysisVersion, $checksFingerprint, $crawledUrls, $scoreVersion, $score, $scorePartial, $scoreJson)
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
            analysis.Parameters.AddWithValue("$crawledUrls", crawledUrls);
            analysis.Parameters.AddWithValue("$scoreVersion", ScoreCalculator.VERSION);
            analysis.Parameters.AddWithValue("$score", (object?)score?.Value ?? DBNull.Value);
            analysis.Parameters.AddWithValue("$scorePartial", score is null ? DBNull.Value : score.Partial);
            analysis.Parameters.AddWithValue("$scoreJson", score is null ? DBNull.Value : JsonSerializer.Serialize(score, EngineJsonContext.Default.SiteScore));
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

        // The rechecks this analysis is the first to follow are settled from the issues it stores, in the same commit.
        RecheckSettlement.Settle(_connection, _transaction, skipped.Select(check => check.CheckId), score?.Value);

        _transaction.Commit();
        IndexLinkKinds();
        return new CommittedAnalysis(counts, crawledUrls, score);
    }

    /// <summary>Rolls back unless committed, then folds the write-ahead log and closes the file.</summary>
    public void Dispose()
    {
        try
        {
            _insert.Dispose();
            _insertTarget.Dispose();
            _insertFault.Dispose();
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

    /// <summary>
    /// Indexes the links to each URL by kind, which the inspector's links of some kinds read, once per crawl file. After the
    /// commit, so the results can be read meanwhile, and outside a transaction, so the sort can spill to temporary files
    /// rather than memory: a million-page crawl's 50 million links take 0.6 GB of index. Failing to build it (a full disk)
    /// leaves the analysis as committed, and the links are read without it.
    /// </summary>
    private void IndexLinkKinds()
    {
        var time = Stopwatch.StartNew();
        try
        {
            CrawlDatabase.Execute(_connection,
                $"PRAGMA temp_store = FILE; PRAGMA cache_size = -{SORT_CACHE_KIB}; PRAGMA threads = {Math.Min(SORT_THREADS, Environment.ProcessorCount)}; {CrawlSchema.LINK_KINDS_INDEX}");
            _logger.LogDebug("Indexed the links by kind in {Elapsed}", time.Elapsed);
        }
        catch (SqliteException exception)
        {
            _logger.LogWarning(exception, "Couldn't index the links by kind; the inspector reads links of some kinds without it");
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
