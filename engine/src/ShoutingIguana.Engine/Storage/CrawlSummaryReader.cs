using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage.Models;
using SQLitePCL;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// The few numbers a list of crawls shows, cheap enough to read for every crawl in it: the crawl row, two counts and
/// the last analysis. It never touches the links, which <see cref="CrawlStatsReader.Read"/> counts. Safe to run while
/// the crawl is still writing. A read can be given a time limit through its cancellation token: crawl files are shared,
/// and one crafted to be slow to count must not hold up a list.
/// </summary>
public static class CrawlSummaryReader
{
    /// <exception cref="InvalidOperationException">The file isn't a crawl, or was written with another schema.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled before the read finished; the query stops at once.</exception>
    public static CrawlFileSummary Read(string databasePath, CancellationToken cancellationToken = default) =>
        Read(databasePath, withCounts: true, cancellationToken);

    /// <summary>
    /// The summary without its counts, which take a pass over every response: for a crawl that is still running, whose
    /// live progress has them, or when only its status and start URL are needed. Two lookups, however big the crawl.
    /// </summary>
    /// <inheritdoc cref="Read(string, CancellationToken)"/>
    public static CrawlFileSummary ReadWithoutCounts(string databasePath, CancellationToken cancellationToken = default) =>
        Read(databasePath, withCounts: false, cancellationToken);

    private static CrawlFileSummary Read(string databasePath, bool withCounts, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = CrawlDatabase.OpenReader(databasePath);

        // Declared after the connection, so it is disposed first: an interrupt never reaches a closed connection.
        using var interrupt = cancellationToken.Register(() => raw.sqlite3_interrupt(connection.Handle!));
        try
        {
            var summary = ReadCrawlRow(connection);
            cancellationToken.ThrowIfCancellationRequested();
            var analysis = IssueReader.ReadAnalysis(connection, transaction: null);
            cancellationToken.ThrowIfCancellationRequested();
            var flagged = analysis is null ? null : FlaggedChecks(connection);
            cancellationToken.ThrowIfCancellationRequested();
            var (fetched, crawled, pages) = withCounts ? Counts(connection) : (null, null, null);
            return summary with
            {
                HasFavicon = FaviconReader.Has(connection, transaction: null),
                TimeCrawling = CrawlRunReader.Read(connection, transaction: null)?.TimeCrawling,
                FetchedUrlCount = fetched,
                CrawledUrlCount = crawled,
                PageCount = pages,
                AnalyzedAt = analysis?.AnalyzedAt,
                Analysis = analysis,
                FlaggedChecks = flagged
            };
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == raw.SQLITE_INTERRUPT && cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("Reading the crawl's summary took too long.", exception, cancellationToken);
        }
    }

    private static CrawlFileSummary ReadCrawlRow(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT start_url, status, started_at, updated_at, finished_at, engine_version FROM crawl WHERE id = 1";
        using var crawl = command.ExecuteReader();
        return crawl.Read()
            ? new CrawlFileSummary
            {
                StartUrl = crawl.GetString(0),
                Status = (CrawlStatus)crawl.GetInt32(1),
                StartedAt = DateTimeOffset.FromUnixTimeMilliseconds(crawl.GetInt64(2)),
                UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(crawl.GetInt64(3)),
                FinishedAt = crawl.IsDBNull(4) ? null : DateTimeOffset.FromUnixTimeMilliseconds(crawl.GetInt64(4)),
                EngineVersion = crawl.GetString(5)
            }
            : throw new InvalidOperationException("This file is not a crawl database.");
    }

    /// <summary>The URLs with an outcome, those of them that were asked (not "not checked"), and the pages, in one pass over the responses.</summary>
    private static (long? Fetched, long? Crawled, long? Pages) Counts(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT COUNT(*), COALESCE(SUM(NOT {AnalysisReader.NOT_CHECKED}), 0), COALESCE(SUM({CrawlSchema.PAGE}), 0)
            FROM responses r LEFT JOIN urls u ON u.id = r.url_id
            """;
        using var reader = command.ExecuteReader();
        return reader.Read() ? (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2)) : (0, 0, 0);
    }

    private static Dictionary<IssueSeverity, long> FlaggedChecks(SqliteConnection connection)
    {
        Dictionary<IssueSeverity, long> checks = [];
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT severity, COUNT(*) FROM issue_types WHERE url_count > 0 GROUP BY severity";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            // Stored as the severity's name. One this engine doesn't know (a newer engine's) is left out.
            if (Enum.TryParse<IssueSeverity>(reader.GetString(0), ignoreCase: false, out var severity) && Enum.IsDefined(severity))
            {
                checks[severity] = reader.GetInt64(1);
            }
        }

        return checks;
    }
}
