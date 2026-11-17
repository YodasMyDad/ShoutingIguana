using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Sitemaps;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// Aggregate queries over a stored crawl. Safe to run while the crawl is still writing. Once the crawl has been
/// analysed, indexability is the analysis's, which knows where canonicals and meta refreshes lead, and the indexability
/// breakdown counts what the analysis counts, as the URL summary does.
/// </summary>
public static class CrawlStatsReader
{
    /// <summary>The tallies the passes count into (<c>si_tally(slot, value)</c>).</summary>
    private const int OUTCOMES = 0, STATUS_CODES = 1, NON_INDEXABLE = 2, SKIP_REASONS = 3, PROBE_OUTCOMES = 4, PROBE_STATUS_CODES = 5;

    /// <summary>What the numbers are kept under.</summary>
    private const string KEPT = "stats";

    /// <summary>A URL a recheck let go (the fix loop): no longer found, so no part of the URLs found; its skip reason still counts it.</summary>
    private static readonly string LET_GO = $"(state = {(int)UrlState.Skipped} AND skip_reason = {(int)SkipReason.NoLongerLinked})";

    public static CrawlStats Read(string databasePath) => Read(databasePath, cache: null);

    /// <summary>
    /// The crawl's headline numbers, all of one snapshot of the file. The URLs are read once and the responses once,
    /// whatever is counted from them, and where the analysis's link summary counts the crawl as it stands no link is
    /// read: it gives the number of links, and which URLs another URL links to.
    /// </summary>
    /// <param name="cache">Where the numbers are kept while the crawl stands still (a running crawl's, briefly); none reads each time.</param>
    /// <param name="cancellationToken">Stops the read within milliseconds, the statement running included.</param>
    /// <exception cref="InvalidOperationException">The file isn't a crawl, or was written with another schema.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static CrawlStats Read(string databasePath, ReadCache? cache, CancellationToken cancellationToken = default) =>
        GridReadContext.Read(databasePath, cache, cancellationToken, context => context.Cached(KEPT, () => Read(context)));

    /// <summary>
    /// The crawl's headline numbers where they're kept for the crawl as it stands, and null where they'd have to be read:
    /// it opens the file and reads its stamp, a millisecond or two, and passes over nothing. For a host that queues its
    /// reads, which needn't queue for this.
    /// </summary>
    /// <exception cref="InvalidOperationException">The file isn't a crawl, or was written with another schema.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static CrawlStats? ReadKept(string databasePath, ReadCache cache, CancellationToken cancellationToken = default) =>
        GridReadContext.Read(databasePath, cache, cancellationToken, context => context.Keeps(KEPT) ? context.Cached(KEPT, () => Read(context)) : null);

    private static CrawlStats Read(GridReadContext context)
    {
        var join = context.HasEffectiveIndexability ? "LEFT JOIN effective_indexability e ON e.url_id = r.url_id" : string.Empty;

        string startUrl = string.Empty, engineVersion = string.Empty;
        var status = CrawlStatus.Running;
        DateTimeOffset startedAt = default;
        DateTimeOffset? finishedAt = null;
        Rows(context, "SELECT start_url, status, started_at, finished_at, engine_version FROM crawl WHERE id = 1", reader =>
        {
            startUrl = reader.GetString(0);
            status = (CrawlStatus)reader.GetInt32(1);
            startedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(2));
            finishedAt = reader.IsDBNull(3) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3));
            engineVersion = reader.GetString(4);
        });

        // One pass over the URLs.
        long urls = 0, internalUrls = 0, queued = 0, largestId = 0;
        Rows(context, $"""
            SELECT COUNT(*) - COALESCE(SUM({LET_GO}), 0), COALESCE(SUM(CASE WHEN {LET_GO} THEN 0 ELSE is_internal END), 0), COALESCE(SUM(state = 0), 0), COALESCE(MAX(id), 0),
                   COUNT(CASE WHEN state = 3 THEN {ReaderFunctions.TALLY}({SKIP_REASONS}, skip_reason) END)
            FROM urls
            """, reader =>
        {
            urls = reader.GetInt64(0);
            internalUrls = reader.GetInt64(1);
            queued = reader.GetInt64(2);
            largestId = reader.GetInt64(3);
        });

        // One pass over the responses, each with its URL: the pages, every response by outcome and by status (and the
        // probes of the site's other versions among them, apart), the site's URLs that can't be indexed by reason, and
        // the pages a sitemap led to, which may have no link to them. The limit
        // that limits nothing keeps SQLite from folding the inner query into the outer, which would work out what a row
        // is (a page, its reason, not checked) once for each count that asks, not once a row.
        UrlIdSet sitemapPages = new(largestId);
        var sitemapSlot = context.Use(sitemapPages);
        long htmlPages = 0, indexable = 0, totalBytes = 0, notChecked = 0;
        double averageMs = 0;
        Rows(context, $"""
            SELECT COALESCE(SUM(page), 0), COALESCE(SUM(page AND reason = 0), 0), COALESCE(AVG(CASE WHEN page THEN response_ms END), 0),
                   COALESCE(SUM(CASE WHEN page THEN size_bytes END), 0), COALESCE(SUM(not_checked), 0),
                   COUNT({ReaderFunctions.TALLY}({OUTCOMES}, outcome)),
                   COUNT({ReaderFunctions.TALLY}({STATUS_CODES}, status_code)),
                   COUNT(CASE WHEN is_internal = 1 AND reason <> 0 AND source <> {(int)DiscoverySource.SiteVersion} AND NOT not_checked
                              THEN {ReaderFunctions.TALLY}({NON_INDEXABLE}, reason) END),
                   COUNT(CASE WHEN page AND source = {(int)DiscoverySource.Sitemap} THEN {ReaderFunctions.ADD_TO_SET}({sitemapSlot}, url_id) END),
                   COUNT(CASE WHEN source = {(int)DiscoverySource.SiteVersion}
                              THEN {ReaderFunctions.TALLY}({PROBE_OUTCOMES}, outcome) + COALESCE({ReaderFunctions.TALLY}({PROBE_STATUS_CODES}, status_code), 0) END)
            FROM (SELECT r.url_id, r.outcome, r.status_code, r.response_ms, r.size_bytes, u.is_internal, u.source,
                         ({CrawlSchema.PAGE}) AS page, {context.Reason} AS reason, {AnalysisReader.NOT_CHECKED} AS not_checked
                  FROM responses r LEFT JOIN urls u ON u.id = r.url_id {join} LIMIT -1)
            """, reader =>
        {
            htmlPages = reader.GetInt64(0);
            indexable = reader.GetInt64(1);
            averageMs = reader.GetDouble(2);
            totalBytes = reader.GetInt64(3);
            notChecked = reader.GetInt64(4);
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
            LinkCount = context.LinkSummary?.Links ?? context.Scalar("SELECT COUNT(*) FROM links", []),
            SitemapCount = context.Scalar("SELECT COUNT(*) FROM sitemaps WHERE error IS NULL", []),
            SitemapErrorCount = context.Scalar($"""
                SELECT COUNT(*) FROM sitemaps WHERE error IS NOT NULL AND is_probe = 0
                AND substr(error, 1, {SitemapDiscoverer.NOT_READ_ERROR.Length}) <> '{SitemapDiscoverer.NOT_READ_ERROR}'
                """, []),
            SitemapUrlCount = context.Scalar("SELECT COUNT(DISTINCT url_id) FROM sitemap_urls", []),
            UnlinkedPageCount = Unlinked(context, sitemapPages.Seal(listed: 0), sitemapSlot, largestId),
            HtmlPageCount = htmlPages,
            IndexablePageCount = indexable,
            AverageResponseMs = averageMs,
            TotalBytes = totalBytes,
            Outcomes = Grouped(context, OUTCOMES, value => (FetchOutcome)value),
            StatusCodes = Grouped(context, STATUS_CODES, value => value),
            SkipReasons = Grouped(context, SKIP_REASONS, value => (SkipReason)value),
            SiteVersions = new SiteVersionStats(context.TalliesIn(PROBE_OUTCOMES).Values.Sum(), Grouped(context, PROBE_OUTCOMES, value => (FetchOutcome)value),
                Grouped(context, PROBE_STATUS_CODES, value => value)),
            NotCheckedCount = notChecked,

            // The site's own URLs, as the checks judge them: a probe of the site's other versions isn't one (that the http://
            // version doesn't answer is normal), and a URL never asked for, its host having stopped answering, has no answer.
            NonIndexableReasons = Grouped(context, NON_INDEXABLE, value => (IndexabilityReason)value)
        };
    }

    /// <summary>
    /// The pages a sitemap led to that no other URL links to: from the links themselves rather than depth. Where the link
    /// summary counts the crawl as it stands, from that: it counts each URL's links from other pages apart from a page's
    /// links to itself, so no link is read. Without it, each of those pages is looked up among the links.
    /// </summary>
    private static long Unlinked(GridReadContext context, UrlIdSet sitemapPages, int sitemapSlot, long largestId)
    {
        if (sitemapPages.Count == 0)
        {
            return 0;
        }

        if (context.LinkSummary is null)
        {
            return context.Scalar($"""
                SELECT COUNT(*) FROM urls u
                WHERE {ReaderFunctions.IN_SET}({sitemapSlot}, u.id) AND NOT EXISTS (SELECT 1 FROM links l WHERE l.to_id = u.id AND l.from_id <> u.id)
                """, []);
        }

        UrlIdSet linked = new(largestId);
        context.Scalar($"""
            SELECT COUNT({ReaderFunctions.ADD_TO_SET}({context.Use(linked)}, to_id)) FROM link_summary
            WHERE self = 0 AND links > 0 AND {ReaderFunctions.IN_SET}({sitemapSlot}, to_id)
            """, []);
        return sitemapPages.Count - linked.Seal(listed: 0).Count;
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

    /// <summary>A tally the passes counted, by value. A value past what an int holds, which only a crafted file has, is left out.</summary>
    private static Dictionary<TKey, long> Grouped<TKey>(GridReadContext context, int slot, Func<int, TKey> key)
        where TKey : notnull =>
        context.TalliesIn(slot).Where(group => group.Key is >= int.MinValue and <= int.MaxValue).ToDictionary(group => key((int)group.Key), group => group.Value);

    private static void Rows(GridReadContext context, string sql, Action<SqliteDataReader> read)
    {
        using var command = context.Command(sql, []);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            read(reader);
        }
    }
}
