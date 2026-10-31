using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Serialization;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>Read-only access to a stored crawl for the analysis pass.</summary>
internal sealed class AnalysisReader : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqliteCommand _links;
    private readonly SqliteParameter _linksFromId;
    private readonly SqliteCommand _details;
    private readonly SqliteParameter _detailsUrlId;

    private AnalysisReader(SqliteConnection connection)
    {
        _connection = connection;
        _links = connection.CreateCommand();
        _links.CommandText = "SELECT to_id, kind, flags, position, text, fragment FROM links WHERE from_id = $fromId";
        _linksFromId = _links.Parameters.Add("$fromId", SqliteType.Integer);
        _details = connection.CreateCommand();
        _details.CommandText = "SELECT data FROM response_details WHERE url_id = $urlId";
        _detailsUrlId = _details.Parameters.Add("$urlId", SqliteType.Integer);
    }

    public static AnalysisReader Open(string databasePath) => new(CrawlDatabase.OpenReader(databasePath));

    public CrawlStatus ReadStatus() =>
        CrawlDatabase.Scalar(_connection, "SELECT status FROM crawl WHERE id = 1") is long status
            ? (CrawlStatus)status
            : throw new InvalidOperationException("This file is not a crawl database.");

    public UrlTable ReadUrls()
    {
        var maxId = Convert.ToInt32(CrawlDatabase.Scalar(_connection, "SELECT COALESCE(MAX(id), 0) FROM urls"), CultureInfo.InvariantCulture);
        var inSitemap = new bool[maxId + 1];
        Rows("SELECT DISTINCT url_id FROM sitemap_urls", reader => inSitemap[reader.GetInt32(0)] = true);

        var statuses = new UrlStatus[maxId + 1];
        var startId = 0;
        Rows($"""
            SELECT u.id, u.is_internal, substr(u.url, 1, 6) = 'https:', u.depth, u.source,
                   r.url_id IS NOT NULL, r.outcome, r.status_code, r.is_html, r.indexability_reason,
                   r.redirect_url_id, r.size_bytes
            FROM urls u LEFT JOIN responses r ON r.url_id = u.id
            """, reader =>
        {
            var id = reader.GetInt32(0);
            var fetched = reader.GetBoolean(5);
            if (startId == 0 && reader.GetInt32(4) == (int)DiscoverySource.Seed)
            {
                startId = id;
            }

            statuses[id] = new UrlStatus(
                Known: true,
                IsInternal: reader.GetBoolean(1),
                IsHttps: reader.GetBoolean(2),
                Depth: reader.GetInt32(3),
                Fetched: fetched,
                Outcome: fetched ? (FetchOutcome)reader.GetInt32(6) : default,
                StatusCode: reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
                IsHtml: fetched && reader.GetBoolean(8),
                IndexabilityReason: fetched ? (IndexabilityReason)reader.GetInt32(9) : default,
                RedirectTargetId: reader.IsDBNull(10) ? 0 : reader.GetInt32(10),
                SizeBytes: reader.IsDBNull(11) ? null : reader.GetInt64(11),
                InSitemap: inSitemap[id]);
        });

        return new UrlTable(statuses, startId);
    }

    /// <summary>Every successful internal HTML page with its facts and outgoing links, in crawl order.</summary>
    public IEnumerable<AnalyzedPage> ReadPages()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"""
            SELECT u.id, u.url, u.depth, r.size_bytes, r.body_bytes, r.ttfb_ms, r.response_ms, r.indexability_reason, d.data
            FROM responses r
            JOIN urls u ON u.id = r.url_id
            JOIN response_details d ON d.url_id = r.url_id
            WHERE u.is_internal = 1 AND r.is_html = 1 AND r.outcome = {(int)FetchOutcome.Success}
            ORDER BY r.url_id
            """;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var details = BlobCodec.DecodeDetails((byte[])reader.GetValue(8));
            if (details.Page is null)
            {
                continue;
            }

            var id = reader.GetInt32(0);
            var url = reader.GetString(1);
            yield return new AnalyzedPage
            {
                UrlId = id,
                Url = url,
                Uri = new Uri(url),
                Depth = reader.GetInt32(2),
                SizeBytes = reader.IsDBNull(3) ? null : reader.GetInt64(3),
                BodyBytes = reader.IsDBNull(4) ? null : reader.GetInt64(4),
                TimeToFirstByteMs = reader.GetInt32(5),
                ResponseMs = reader.GetInt32(6),
                IndexabilityReason = (IndexabilityReason)reader.GetInt32(7),
                Headers = details.Headers,
                Facts = details.Page,
                Links = ReadLinks(id)
            };
        }
    }

    /// <summary>The stored facts of one URL, or null when it has none (not HTML, not internal, or not fetched).</summary>
    public PageFacts? ReadFacts(int urlId)
    {
        _detailsUrlId.Value = urlId;
        return _details.ExecuteScalar() is byte[] data ? BlobCodec.DecodeDetails(data).Page : null;
    }

    public CrawlOptions ReadOptions()
    {
        var json = CrawlDatabase.Scalar(_connection, "SELECT options_json FROM crawl WHERE id = 1") as string
                   ?? throw new InvalidOperationException("This file is not a crawl database.");
        return JsonSerializer.Deserialize(json, EngineJsonContext.Default.CrawlOptions)
               ?? throw new InvalidOperationException("The crawl options can't be read.");
    }

    /// <summary>Why the firewall stopped the crawler, per blocked URL.</summary>
    public Dictionary<int, string> ReadBlockReasons()
    {
        Dictionary<int, string> reasons = [];
        Rows($"SELECT url_id, block_reason FROM responses WHERE outcome = {(int)FetchOutcome.Blocked} AND block_reason IS NOT NULL",
            reader => reasons[reader.GetInt32(0)] = reader.GetString(1));
        return reasons;
    }

    public IReadOnlyList<RobotsRecord> ReadRobots()
    {
        List<RobotsRecord> robots = [];
        Rows("SELECT origin, status_code, content, error, fetched_at FROM robots", reader => robots.Add(new RobotsRecord(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetInt64(4))));
        return robots;
    }

    public IReadOnlyList<StoredSitemap> ReadSitemaps()
    {
        List<StoredSitemap> sitemaps = [];
        Rows("SELECT url, status_code, is_index, url_count, error FROM sitemaps ORDER BY id", reader => sitemaps.Add(new StoredSitemap(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetInt32(1),
            reader.GetBoolean(2),
            reader.GetInt32(3),
            reader.IsDBNull(4) ? null : reader.GetString(4))));
        return sitemaps;
    }

    public void Dispose()
    {
        _links.Dispose();
        _details.Dispose();
        _connection.Dispose();
    }

    private List<StoredLink> ReadLinks(int fromId)
    {
        _linksFromId.Value = fromId;
        List<StoredLink> links = [];
        using var reader = _links.ExecuteReader();
        while (reader.Read())
        {
            links.Add(new StoredLink(
                reader.GetInt32(0),
                (LinkKind)reader.GetInt32(1),
                (LinkFlags)reader.GetInt32(2),
                (LinkPosition)reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return links;
    }

    private void Rows(string sql, Action<SqliteDataReader> read)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            read(reader);
        }
    }
}
