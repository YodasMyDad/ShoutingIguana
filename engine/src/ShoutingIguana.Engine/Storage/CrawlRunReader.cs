using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// When a crawl was crawling, and for how long, as its file keeps it (<c>run_time</c>): each run with the stretches it
/// spent crawling, so the time between runs and the time paused can be told from the time spent. Safe while the crawl
/// runs, when the stretch going now is as the writer last stored it. It answers only when the whole of the crawl's time
/// is known: a file from before the time was kept, a run by an engine that didn't keep it (before, or since: the crawl's
/// stamp then isn't the one the last stretch was stored under), a table of another shape, or times that can't be true
/// all read as not known, never as a part of the time given for the whole.
/// </summary>
public static class CrawlRunReader
{
    /// <summary>The stretches listed at most: a crawl paused more often than this has its time, and no list.</summary>
    public const int MAX_STRETCHES = 1_000;

    /// <summary>The times <see cref="DateTimeOffset"/> can hold, in milliseconds since 1970: years 1 to 9999.</summary>
    private const long MIN_UNIX_MILLISECONDS = -62_135_596_800_000;
    private const long MAX_UNIX_MILLISECONDS = 253_402_300_799_999;

    /// <summary>No crawl spends longer than this crawling: ten years, in milliseconds.</summary>
    private const long MAX_CRAWLED_MS = 315_576_000_000;

    /// <summary>The crawl's runs and the time it spent crawling, or null when that isn't known.</summary>
    /// <exception cref="InvalidOperationException">The file isn't a crawl, or was written with another schema.</exception>
    public static CrawlRuns? Read(string databasePath)
    {
        using var connection = CrawlDatabase.OpenReader(databasePath);
        using var snapshot = connection.BeginTransaction(deferred: true);
        return Read(connection, snapshot);
    }

    internal static CrawlRuns? Read(SqliteConnection connection, SqliteTransaction? transaction)
    {
        if (!HasTable(connection, transaction))
        {
            return null;
        }

        // A recheck's run is listed with the crawl's, and its time isn't the crawl's.
        var rechecks = RecheckReader.HasTables(connection, transaction)
            ? "(SELECT run FROM rechecks WHERE typeof(run) = 'integer')"
            : "(SELECT NULL WHERE 0)";

        // What must hold of every stretch for the total to be the crawl's: each is timed, credibly, and the last was
        // stored under the crawl row's stamp as it stands, so no run since went untimed.
        long stretches;
        double totalMs;
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = $"""
                SELECT COUNT(*),
                       COALESCE(SUM(crawled_ms IS NULL OR typeof(crawled_ms) <> 'integer' OR crawled_ms < 0 OR crawled_ms > {MAX_CRAWLED_MS}
                                    OR typeof(started_at) <> 'integer' OR started_at < {MIN_UNIX_MILLISECONDS} OR started_at > {MAX_UNIX_MILLISECONDS - MAX_CRAWLED_MS}
                                    OR typeof(run) <> 'integer'), 0),
                       TOTAL(CASE WHEN run IN {rechecks} THEN 0 ELSE crawled_ms END),
                       (SELECT crawl_updated_at FROM run_time ORDER BY id DESC LIMIT 1) IS (SELECT updated_at FROM crawl WHERE id = 1)
                FROM run_time
                """;
            using var reader = command.ExecuteReader();
            if (!reader.Read() || reader.GetInt64(0) == 0 || reader.GetInt64(1) > 0 || !reader.GetBoolean(3))
            {
                return null;
            }

            (stretches, totalMs) = (reader.GetInt64(0), reader.GetDouble(2));
        }

        if (totalMs > MAX_CRAWLED_MS)
        {
            return null;
        }

        var time = TimeSpan.FromMilliseconds((long)totalMs);
        if (stretches > MAX_STRETCHES)
        {
            return new CrawlRuns(time, null);
        }

        // Each stretch begins by the wall clock and lasts by one that only runs forward, so a wall clock set back between
        // two (a time sync) would have the later begin before the earlier ended. A stretch begins no earlier than the one
        // before it ended, whichever run that was in: they're listed in order, none overlapping, and add up as stored.
        List<CrawlRun> runs = [];
        List<CrawlingStretch> crawling = [];
        long? run = null;
        long runMs = 0;
        var ended = false;
        var isRecheck = false;
        var earliest = DateTimeOffset.MinValue;
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = $"SELECT run, started_at, crawled_ms, ended IS NOT NULL AND ended <> 0, run IN {rechecks} FROM run_time ORDER BY id";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (run is not null && reader.GetInt64(0) != run)
                {
                    runs.Add(RunOf(crawling, runMs, ended) with { IsRecheck = isRecheck });
                    (crawling, runMs) = ([], 0);
                }

                var from = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1));
                from = from < earliest ? earliest : from;
                var crawledMs = reader.GetInt64(2);
                earliest = from.AddMilliseconds(crawledMs);
                crawling.Add(new CrawlingStretch(from, earliest));
                (run, runMs, ended, isRecheck) = (reader.GetInt64(0), runMs + crawledMs, reader.GetBoolean(3), reader.GetBoolean(4));
            }
        }

        runs.Add(RunOf(crawling, runMs, ended) with { IsRecheck = isRecheck });
        return new CrawlRuns(time, runs);
    }

    private static CrawlRun RunOf(List<CrawlingStretch> crawling, long crawledMs, bool lastEnded) =>
        new(crawling[0].From, lastEnded ? crawling[^1].To : null, TimeSpan.FromMilliseconds(crawledMs), crawling);

    /// <summary>The table, with the columns this engine writes, in order: one another program made its own way isn't read.</summary>
    private static bool HasTable(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT group_concat(name, ',') FROM (SELECT name FROM pragma_table_info('run_time') ORDER BY cid)";
        return command.ExecuteScalar() as string == CrawlSchema.RUN_TIME_COLUMNS;
    }
}
