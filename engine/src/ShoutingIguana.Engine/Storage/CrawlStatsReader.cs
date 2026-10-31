using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// Aggregate queries over a stored crawl. Safe to run while the crawl is still writing. Once the crawl has been
/// analysed, indexability is the analysis's, which knows where canonicals and meta refreshes lead.
/// </summary>
public static class CrawlStatsReader
{
    public static CrawlStats Read(string databasePath)
    {
        using var connection = CrawlDatabase.OpenReader(databasePath);
        var (join, reason) = Indexability(connection);

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
        Rows(connection, $"""
            SELECT COUNT(*), COALESCE(SUM({reason} = 0), 0), COALESCE(AVG(r.response_ms), 0), COALESCE(SUM(r.size_bytes), 0)
            FROM responses r JOIN urls u ON u.id = r.url_id {join}
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
            // From the links themselves rather than depth: a sitemap page that no other URL links to.
            UnlinkedPageCount = Count(connection, $"""
                SELECT COUNT(*) FROM urls u JOIN responses r ON r.url_id = u.id
                WHERE u.is_internal = 1 AND u.source = {(int)DiscoverySource.Sitemap}
                  AND r.is_html = 1 AND r.outcome = 0 AND r.word_count IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM links l WHERE l.to_id = u.id AND l.from_id <> u.id)
                """),
            HtmlPageCount = htmlPages,
            IndexablePageCount = indexable,
            AverageResponseMs = averageMs,
            TotalBytes = totalBytes,
            Outcomes = Grouped(connection, "SELECT outcome, COUNT(*) FROM responses GROUP BY outcome", value => (FetchOutcome)value),
            StatusCodes = Grouped(connection, "SELECT status_code, COUNT(*) FROM responses WHERE status_code IS NOT NULL GROUP BY status_code", value => value),
            SkipReasons = Grouped(connection, "SELECT skip_reason, COUNT(*) FROM urls WHERE state = 3 GROUP BY skip_reason", value => (SkipReason)value),
            NonIndexableReasons = Grouped(connection, $"""
                SELECT {reason}, COUNT(*) FROM responses r JOIN urls u ON u.id = r.url_id {join}
                WHERE u.is_internal = 1 AND {reason} <> 0 GROUP BY 1
                """, value => (IndexabilityReason)value)
        };
    }

    /// <summary>
    /// Pages the analysis audits whose response took longer than <paramref name="thresholdMs"/>, in crawl order. How
    /// slow a page is depends on the machine, so tests use this to check the slow-response issue exactly. Read it after
    /// the analysis: a page that moves on with an instant meta refresh is a redirect, not an audited page.
    /// </summary>
    public static IReadOnlyList<string> ReadSlowPages(string databasePath, int thresholdMs)
    {
        using var connection = CrawlDatabase.OpenReader(databasePath);
        var (join, reason) = Indexability(connection);
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT u.url FROM responses r JOIN urls u ON u.id = r.url_id {join}
            WHERE u.is_internal = 1 AND r.is_html = 1 AND r.outcome = {(int)FetchOutcome.Success} AND {AnalysisReader.SITE_PAGE}
              AND {reason} <> {(int)IndexabilityReason.Redirected} AND r.response_ms > $threshold
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

    /// <summary>Indexability as analysed once the crawl has been, which knows where canonicals and meta refreshes lead.</summary>
    private static (string Join, string Reason) Indexability(SqliteConnection connection) =>
        CrawlDatabase.Scalar(connection, "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'effective_indexability'") is not null
            ? ("LEFT JOIN effective_indexability e ON e.url_id = r.url_id", "COALESCE(e.indexability_reason, r.indexability_reason)")
            : (string.Empty, "r.indexability_reason");

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
