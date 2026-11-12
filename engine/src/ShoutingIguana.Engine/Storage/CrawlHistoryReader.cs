using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// A crawl's progress over time, counted back from its file. Each response keeps when it was fetched, so how many URLs
/// had been fetched by any moment can be told: pages, the site's own, errors and blocked ones too. When each URL was
/// found isn't kept, but the page that first linked it is, and a URL is found as that page is fetched, so the site's own
/// URLs found can be told too. The other counts of what was found (all of it, queued, not followed) can't be. When each
/// run began and ended, and when it was paused, comes with it where the file keeps that (<see cref="CrawlRunReader"/>):
/// a gap where nothing was fetched is then known for a pause or a stop, or for a site that wasn't answering.
/// Safe while the crawl runs.
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
        List<CrawlHistoryPoint> points = [new CrawlHistoryPoint(start, 0, 0, 0, 0, 0, 0)];

        // A second at a time, in time order: as many rows as seconds anything was fetched or found in, each added to the
        // totals. The site's own URLs (internal, not probes of its other versions) count as found when the page that first
        // linked them was fetched (or they were, if that was first: a page fetched again since keeps only its last time),
        // and the start URL and those only sitemaps list, which no page linked, from the crawl's start, as its sitemaps are
        // read then. One the crawl skipped (nofollow, a limit) is never counted: the live count leaves it out once it is.
        var siteUrl = $"u.is_internal = 1 AND u.source <> {(int)DiscoverySource.SiteVersion}";
        using var command = connection.CreateCommand();
        command.Transaction = snapshot;
        command.CommandText = $"""
            SELECT second, MAX(at), SUM(fetched), SUM(pages), SUM(site_fetched), SUM(errors), SUM(blocked), SUM(found)
            FROM (
                SELECT r.fetched_at / {BUCKET_MS} AS second, r.fetched_at AS at, 1 AS fetched,
                       CASE WHEN {CrawlSchema.PAGE} THEN 1 ELSE 0 END AS pages,
                       {siteUrl} AS site_fetched,
                       r.outcome IN ({(int)FetchOutcome.ClientError}, {(int)FetchOutcome.ServerError}, {(int)FetchOutcome.Timeout},
                                     {(int)FetchOutcome.NetworkError}, {(int)FetchOutcome.ProcessingError}) AS errors,
                       r.outcome = {(int)FetchOutcome.Blocked} AS blocked,
                       0 AS found
                FROM responses r JOIN urls u ON u.id = r.url_id
                WHERE r.fetched_at < $before
                UNION ALL
                SELECT found_at / {BUCKET_MS}, found_at, 0, 0, 0, 0, 0, 1
                FROM (
                    SELECT CASE WHEN u.found_on_id IS NULL THEN $startedAt
                                WHEN f.fetched_at IS NULL THEN COALESCE(o.fetched_at, $startedAt)
                                WHEN o.fetched_at IS NULL THEN f.fetched_at
                                ELSE MIN(f.fetched_at, o.fetched_at) END AS found_at
                    FROM urls u
                    LEFT JOIN responses f ON f.url_id = u.found_on_id
                    LEFT JOIN responses o ON o.url_id = u.id
                    WHERE {siteUrl} AND u.state <> {(int)UrlState.Skipped}
                )
                WHERE found_at < $before
            )
            GROUP BY second
            ORDER BY second
            """;
        command.Parameters.AddWithValue("$before", before?.ToUnixTimeMilliseconds() ?? long.MaxValue);
        command.Parameters.AddWithValue("$startedAt", startedAt);
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
                    total.Blocked + reader.GetInt64(6),
                    total.InternalFound + reader.GetInt64(7));
                series.Add(total);
            }
        }

        points.AddRange(series.Points());
        return new CrawlHistory(start, points) { Runs = CrawlRunReader.Read(connection, snapshot) };
    }
}
