using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// A crawl's progress over time, counted back from its file. Each response keeps when it was fetched, so how many URLs
/// had been fetched by any moment can be told: pages, the site's own, errors and blocked ones too. When each URL was
/// found isn't kept, so the found, queued and not followed counts can't be; nor can when each run began and ended, so a
/// gap where nothing was fetched can be a pause, a stop or a site that wasn't answering. Safe while the crawl runs.
/// </summary>
public static class CrawlHistoryReader
{
    public const int DEFAULT_MAX_POINTS = 600;

    private const int BUCKET_MS = 1_000;

    /// <param name="before">Only what was fetched before this: the start of a run a host is watching live, which it counts itself.</param>
    /// <param name="maxPoints">An even number: the points beyond the zero at the start are downsampled to at most this many, and the last.</param>
    public static CrawlHistory Read(string databasePath, DateTimeOffset? before = null, int maxPoints = DEFAULT_MAX_POINTS)
    {
        using var connection = CrawlDatabase.OpenReader(databasePath);
        using var snapshot = connection.BeginTransaction(deferred: true);
        long startedAt;
        using (var crawl = connection.CreateCommand())
        {
            crawl.Transaction = snapshot;
            crawl.CommandText = "SELECT started_at FROM crawl WHERE id = 1";
            startedAt = crawl.ExecuteScalar() is long value ? value : throw new InvalidOperationException($"'{databasePath}' is not a crawl database.");
        }

        var series = new DownsampledSeries<CrawlHistoryPoint>(maxPoints);
        var start = DateTimeOffset.FromUnixTimeMilliseconds(startedAt);
        List<CrawlHistoryPoint> points = [new CrawlHistoryPoint(start, 0, 0, 0, 0, 0)];

        // A second at a time, in time order: as many rows as seconds anything was fetched in, each added to the totals.
        using var command = connection.CreateCommand();
        command.Transaction = snapshot;
        command.CommandText = $"""
            SELECT r.fetched_at / {BUCKET_MS}, MAX(r.fetched_at), COUNT(*),
                   SUM(CASE WHEN {CrawlSchema.PAGE} THEN 1 ELSE 0 END),
                   SUM(u.is_internal = 1 AND u.source <> {(int)DiscoverySource.SiteVersion}),
                   SUM(r.outcome IN ({(int)FetchOutcome.ClientError}, {(int)FetchOutcome.ServerError}, {(int)FetchOutcome.Timeout},
                                     {(int)FetchOutcome.NetworkError}, {(int)FetchOutcome.ProcessingError})),
                   SUM(r.outcome = {(int)FetchOutcome.Blocked})
            FROM responses r JOIN urls u ON u.id = r.url_id
            WHERE r.fetched_at < $before
            GROUP BY 1
            ORDER BY 1
            """;
        command.Parameters.AddWithValue("$before", before?.ToUnixTimeMilliseconds() ?? long.MaxValue);
        using (var reader = command.ExecuteReader())
        {
            var total = points[0];
            while (reader.Read())
            {
                total = new CrawlHistoryPoint(
                    DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)),
                    total.Fetched + reader.GetInt64(2),
                    total.HtmlPages + reader.GetInt64(3),
                    total.InternalFetched + reader.GetInt64(4),
                    total.Errors + reader.GetInt64(5),
                    total.Blocked + reader.GetInt64(6));
                series.Add(total);
            }
        }

        points.AddRange(series.Points());
        return new CrawlHistory(start, points);
    }
}
