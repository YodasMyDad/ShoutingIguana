using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>Aggregate queries over a stored crawl. Safe to run while the crawl is still writing.</summary>
public static class CrawlStatsReader
{
    public static CrawlStats Read(string databasePath)
    {
        using var connection = CrawlDatabase.OpenReader(databasePath);

        string startUrl = string.Empty, engineVersion = string.Empty;
        var status = CrawlStatus.Running;
        DateTimeOffset startedAt = default;
        DateTimeOffset? finishedAt = null;
        Rows(connection, "SELECT start_url, status, started_at, finished_at, engine_version FROM crawl WHERE id = 1", reader =>
        {
            startUrl = reader.GetString(0);
            status = (CrawlStatus)reader.GetInt32(1);
            startedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(2));
            finishedAt = reader.IsDBNull(3) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3));
            engineVersion = reader.GetString(4);
        });

        long urls = 0, internalUrls = 0, queued = 0;
        Rows(connection, "SELECT COUNT(*), COALESCE(SUM(is_internal), 0), COALESCE(SUM(state = 0), 0) FROM urls", reader =>
        {
            urls = reader.GetInt64(0);
            internalUrls = reader.GetInt64(1);
            queued = reader.GetInt64(2);
        });

        long htmlPages = 0, indexable = 0, totalBytes = 0;
        double averageMs = 0;
        Rows(connection, """
            SELECT COUNT(*), COALESCE(SUM(r.indexability = 0), 0), COALESCE(AVG(r.response_ms), 0), COALESCE(SUM(r.size_bytes), 0)
            FROM responses r JOIN urls u ON u.id = r.url_id
            WHERE u.is_internal = 1 AND r.is_html = 1 AND r.outcome = 0 AND r.word_count IS NOT NULL
            """, reader =>
        {
            htmlPages = reader.GetInt64(0);
            indexable = reader.GetInt64(1);
            averageMs = reader.GetDouble(2);
            totalBytes = reader.GetInt64(3);
        });

        return new CrawlStats
        {
            StartUrl = startUrl,
            Status = status,
            StartedAt = startedAt,
            FinishedAt = finishedAt,
            EngineVersion = engineVersion,
            UrlCount = urls,
            InternalUrlCount = internalUrls,
            QueuedCount = queued,
            LinkCount = Count(connection, "SELECT COUNT(*) FROM links"),
            SitemapCount = Count(connection, "SELECT COUNT(*) FROM sitemaps WHERE error IS NULL"),
            SitemapErrorCount = Count(connection, "SELECT COUNT(*) FROM sitemaps WHERE error IS NOT NULL AND is_probe = 0"),
            SitemapUrlCount = Count(connection, "SELECT COUNT(DISTINCT url_id) FROM sitemap_urls"),
            UnlinkedPageCount = Count(connection, $"""
                SELECT COUNT(*) FROM urls u JOIN responses r ON r.url_id = u.id
                WHERE u.is_internal = 1 AND u.depth >= {CrawlDepth.UNLINKED} AND r.is_html = 1 AND r.outcome = 0 AND r.word_count IS NOT NULL
                """),
            HtmlPageCount = htmlPages,
            IndexablePageCount = indexable,
            AverageResponseMs = averageMs,
            TotalBytes = totalBytes,
            Outcomes = Grouped(connection, "SELECT outcome, COUNT(*) FROM responses GROUP BY outcome", value => (FetchOutcome)value),
            StatusCodes = Grouped(connection, "SELECT status_code, COUNT(*) FROM responses WHERE status_code IS NOT NULL GROUP BY status_code", value => value),
            SkipReasons = Grouped(connection, "SELECT skip_reason, COUNT(*) FROM urls WHERE state = 3 GROUP BY skip_reason", value => (SkipReason)value),
            NonIndexableReasons = Grouped(connection, """
                SELECT r.indexability_reason, COUNT(*) FROM responses r JOIN urls u ON u.id = r.url_id
                WHERE u.is_internal = 1 AND r.indexability = 1 GROUP BY r.indexability_reason
                """, value => (IndexabilityReason)value)
        };
    }

    /// <summary>
    /// Pages the analysis audits whose response took longer than <paramref name="thresholdMs"/>, in crawl order. How
    /// slow a page is depends on the machine, so tests use this to check the slow-response issue exactly.
    /// </summary>
    public static IReadOnlyList<string> ReadSlowPages(string databasePath, int thresholdMs)
    {
        using var connection = CrawlDatabase.OpenReader(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT u.url FROM responses r JOIN urls u ON u.id = r.url_id
            WHERE u.is_internal = 1 AND r.is_html = 1 AND r.outcome = {(int)FetchOutcome.Success} AND {AnalysisReader.SITE_PAGE}
              AND r.response_ms > $threshold
            ORDER BY u.id
            """;
        command.Parameters.AddWithValue("$threshold", thresholdMs);
        List<string> urls = [];
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            urls.Add(reader.GetString(0));
        }

        return urls;
    }

    private static long Count(SqliteConnection connection, string sql) =>
        Convert.ToInt64(CrawlDatabase.Scalar(connection, sql) ?? 0L);

    private static Dictionary<TKey, long> Grouped<TKey>(SqliteConnection connection, string sql, Func<int, TKey> key)
        where TKey : notnull
    {
        Dictionary<TKey, long> groups = new();
        Rows(connection, sql, reader => groups[key(reader.GetInt32(0))] = reader.GetInt64(1));
        return groups;
    }

    private static void Rows(SqliteConnection connection, string sql, Action<SqliteDataReader> read)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            read(reader);
        }
    }
}
