using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Serialization;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>Read-only access to a stored crawl for the analysis pass.</summary>
internal sealed class AnalysisReader : IDisposable
{
    /// <summary>
    /// A page of the site to audit: its HTML was parsed (status-checked URLs have no word count), and it isn't one of
    /// Cloudflare's /cdn-cgi/ endpoints, such as the email-protection page every obfuscated address links to.
    /// </summary>
    internal const string SITE_PAGE = "r.word_count IS NOT NULL AND instr(u.url, '/cdn-cgi/') = 0";

    /// <summary>A response stored without a request, because the URL's host had stopped answering.</summary>
    private static readonly string NOT_CHECKED =
        $"COALESCE(substr(r.error, 1, {RetryingFetcher.NOT_CHECKED_ERROR.Length}) = '{RetryingFetcher.NOT_CHECKED_ERROR}', 0)";

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

    public CrawlStatus ReadStatus() => CrawlDatabase.ReadStatus(_connection);

    /// <summary>
    /// Sitemap entries on a host (and port) that none of the sitemaps listing them is on (sitemaps.org: search engines
    /// drop those), apart from entries on <paramref name="siteAuthorities"/>: the site's robots.txt declared the
    /// sitemaps, which the protocol accepts for that site's URLs wherever the sitemap lives.
    /// </summary>
    /// <param name="siteAuthorities">
    /// The start URL's host and port, and the home page's after redirects. A sitemap is stored under the URL it was
    /// found at, so one the bare domain's robots.txt leads to sits on the bare domain even when it is read from www.
    /// </param>
    public HashSet<int> ReadOffHostSitemapEntries(IReadOnlyList<string> siteAuthorities)
    {
        // Stored URLs are normalised, so the authority runs from "://" to the first "/" after it.
        static string Authority(string column) =>
            $"substr({column}, instr({column}, '://') + 3, instr(substr({column}, instr({column}, '://') + 3), '/') - 1)";

        using var command = _connection.CreateCommand();
        command.CommandText = $"""
            SELECT id FROM (
                SELECT su.url_id AS id, {Authority("u.url")} AS entry, {Authority("s.url")} AS sitemap
                FROM sitemap_urls su JOIN sitemaps s ON s.id = su.sitemap_id JOIN urls u ON u.id = su.url_id)
            WHERE entry NOT IN ({string.Join(", ", siteAuthorities.Select((_, index) => $"$site{index}"))})
            GROUP BY id HAVING MAX(entry = sitemap) = 0
            """;
        for (var index = 0; index < siteAuthorities.Count; index++)
        {
            command.Parameters.AddWithValue($"$site{index}", siteAuthorities[index]);
        }
        HashSet<int> ids = [];
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            ids.Add(reader.GetInt32(0));
        }

        return ids;
    }

    /// <summary>URLs that were never requested because their host had stopped answering.</summary>
    public int CountNotChecked() =>
        Convert.ToInt32(CrawlDatabase.Scalar(_connection, $"SELECT COUNT(*) FROM responses r WHERE {NOT_CHECKED}"), CultureInfo.InvariantCulture);

    public UrlTable ReadUrls()
    {
        var maxId = Convert.ToInt32(CrawlDatabase.Scalar(_connection, "SELECT COALESCE(MAX(id), 0) FROM urls"), CultureInfo.InvariantCulture);
        var inSitemap = new bool[maxId + 1];
        Rows("SELECT DISTINCT url_id FROM sitemap_urls", reader => inSitemap[reader.GetInt32(0)] = true);

        var statuses = new UrlStatus[maxId + 1];
        var startId = 0;
        // A URL failed without a request, because its host had stopped answering, was never checked: links to it can't
        // be judged, so it counts as unfetched.
        Rows($"""
            SELECT u.id, u.is_internal, substr(u.url, 1, 6) = 'https:', u.depth, u.source,
                   r.url_id IS NOT NULL AND NOT {NOT_CHECKED}, r.outcome, r.status_code, r.is_html, r.indexability_reason,
                   r.redirect_url_id, r.size_bytes, COALESCE({SITE_PAGE}, 0), u.url, r.content_type
            FROM urls u LEFT JOIN responses r ON r.url_id = u.id
            """, reader =>
        {
            var id = reader.GetInt32(0);
            var fetched = reader.GetBoolean(5);
            if (startId == 0 && reader.GetInt32(4) == (int)DiscoverySource.Seed)
            {
                startId = id;
            }

            var outcome = fetched ? (FetchOutcome)reader.GetInt32(6) : default;
            var contentType = outcome == FetchOutcome.Success && fetched && !reader.IsDBNull(14) ? reader.GetString(14) : null;
            statuses[id] = new UrlStatus(
                Known: true,
                IsInternal: reader.GetBoolean(1),
                IsHttps: reader.GetBoolean(2),
                Depth: reader.GetInt32(3),
                Fetched: fetched,
                Outcome: outcome,
                StatusCode: reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
                IsHtml: fetched && reader.GetBoolean(8),
                IsParsed: fetched && reader.GetBoolean(12),
                IndexabilityReason: fetched ? (IndexabilityReason)reader.GetInt32(9) : default,
                RedirectTargetId: reader.IsDBNull(10) ? 0 : reader.GetInt32(10),
                SizeBytes: reader.IsDBNull(11) ? null : reader.GetInt64(11),
                InSitemap: inSitemap[id],
                IsResource: ResourceTypes.IsResource(reader.GetString(13), contentType),
                Source: (DiscoverySource)reader.GetInt32(4));
        });

        return new UrlTable(statuses, startId, ReadInstantRefreshes(), ReadCanonicalTargets());
    }

    /// <summary>
    /// Every internal HTML page whose body was parsed, with its facts and outgoing links, in crawl order. URLs that were
    /// only status-checked (outside the start folder, iframes) have header facts alone and aren't pages to audit.
    /// Indexability comes from <paramref name="urls"/>, which knows where canonicals and meta refreshes lead.
    /// </summary>
    public IEnumerable<AnalyzedPage> ReadPages(UrlTable urls)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"""
            SELECT u.id, u.url, u.depth, r.size_bytes, r.body_bytes, r.ttfb_ms, r.response_ms, r.indexability_reason, d.data
            FROM responses r
            JOIN urls u ON u.id = r.url_id
            JOIN response_details d ON d.url_id = r.url_id
            WHERE u.is_internal = 1 AND r.is_html = 1 AND r.outcome = {(int)FetchOutcome.Success} AND {SITE_PAGE}
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
                IndexabilityReason = urls[id].IndexabilityReason,
                Headers = details.Headers,
                Facts = details.Page,
                Links = ReadLinks(id)
            };
        }
    }

    /// <summary>The stored URL with this id, or null when there's none (0 means no URL).</summary>
    public string? ReadUrl(int urlId) => CrawlDatabase.Scalar(_connection, $"SELECT url FROM urls WHERE id = {urlId}") as string;

    /// <summary>The id of a stored URL, given normalised, or 0 when the crawl never found it.</summary>
    public int ReadUrlId(string url)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT id FROM urls WHERE url = $url";
        command.Parameters.AddWithValue("$url", url);
        return command.ExecuteScalar() is long id ? (int)id : 0;
    }

    /// <summary>
    /// Origins (as robots.txt rows name them) whose every URL only the crawler's probe of the site's other versions found:
    /// nothing on the site links there, so their robots.txt is none of the site's business. A stored URL's origin is all
    /// that comes before the first "/" after "://", so its URLs sort between origin + "/" and origin + "0".
    /// </summary>
    public HashSet<string> ReadSiteVersionOnlyOrigins()
    {
        HashSet<string> origins = new(StringComparer.Ordinal);
        Rows($"""
            SELECT r.origin FROM robots r
            WHERE EXISTS (SELECT 1 FROM urls u WHERE u.url >= r.origin || '/' AND u.url < r.origin || '0' AND u.source = {(int)DiscoverySource.SiteVersion})
              AND NOT EXISTS (SELECT 1 FROM urls u WHERE u.url >= r.origin || '/' AND u.url < r.origin || '0' AND u.source <> {(int)DiscoverySource.SiteVersion})
            """, reader => origins.Add(reader.GetString(0)));
        return origins;
    }

    /// <summary>The stored facts of one URL, or null when it has none (not HTML, not internal, or not fetched).</summary>
    public PageFacts? ReadFacts(int urlId)
    {
        _detailsUrlId.Value = urlId;
        return _details.ExecuteScalar() is byte[] data ? BlobCodec.DecodeDetails(data).Page : null;
    }

    /// <summary>Whether sitemap discovery finished, so coverage checks against the sitemaps are meaningful.</summary>
    public bool ReadSitemapsDone() => CrawlDatabase.Scalar(_connection, "SELECT sitemaps_done FROM crawl WHERE id = 1") is 1L;

    /// <summary>Internal URLs the depth or URL limit kept out of the crawl, per reason.</summary>
    public Dictionary<SkipReason, long> ReadLimitSkips()
    {
        Dictionary<SkipReason, long> skips = [];
        Rows($"""
            SELECT skip_reason, COUNT(*) FROM urls
            WHERE is_internal = 1 AND state = {(int)UrlState.Skipped} AND skip_reason IN ({(int)SkipReason.DepthLimit}, {(int)SkipReason.UrlLimit})
            GROUP BY skip_reason
            """, reader => skips[(SkipReason)reader.GetInt32(0)] = reader.GetInt64(1));
        return skips;
    }

    /// <summary>Links from one page to a #fragment on another, streamed for the fragment check.</summary>
    public IEnumerable<(int From, int To, string Fragment)> ReadFragmentLinks()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"SELECT from_id, to_id, fragment FROM links WHERE kind = {(int)LinkKind.Hyperlink} AND fragment IS NOT NULL AND from_id <> to_id";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            yield return (reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2));
        }
    }

    public CrawlOptions ReadOptions()
    {
        var json = CrawlDatabase.Scalar(_connection, "SELECT options_json FROM crawl WHERE id = 1") as string
                   ?? throw new InvalidOperationException("This file is not a crawl database.");
        return JsonSerializer.Deserialize(json, EngineJsonContext.Default.CrawlOptions)
               ?? throw new InvalidOperationException("The crawl options can't be read.");
    }

    /// <summary>
    /// Why the crawler was kept out, per URL: the firewall's reason for a blocked URL, and for a URL refused because
    /// robots.txt couldn't be read, that failure. A refusal by a robots.txt rule has none.
    /// </summary>
    public Dictionary<int, string> ReadBlockReasons()
    {
        Dictionary<int, string> reasons = [];
        Rows($"""
            SELECT url_id, COALESCE(block_reason, error) FROM responses
            WHERE (outcome = {(int)FetchOutcome.Blocked} AND block_reason IS NOT NULL)
               OR (outcome = {(int)FetchOutcome.RobotsDisallowed} AND error IS NOT NULL)
            """, reader => reasons[reader.GetInt32(0)] = reader.GetString(1));
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
        Rows("SELECT url, status_code, is_index, url_count, error, is_probe FROM sitemaps ORDER BY id", reader => sitemaps.Add(new StoredSitemap(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetInt32(1),
            reader.GetBoolean(2),
            reader.GetInt32(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetBoolean(5))));
        return sitemaps;
    }

    public void Dispose()
    {
        _links.Dispose();
        _details.Dispose();
        _connection.Dispose();
    }

    /// <summary>
    /// Pages whose meta refresh moves on at once (0 seconds), with where to. The refresh target is a link of its own;
    /// the delay is only in the page facts, which are read for those pages alone.
    /// </summary>
    private List<(int Page, int Target)> ReadInstantRefreshes()
    {
        List<(int Page, int Target)> candidates = [];
        Rows($"SELECT from_id, to_id FROM links WHERE kind = {(int)LinkKind.MetaRefresh} AND from_id <> to_id",
            reader => candidates.Add((reader.GetInt32(0), reader.GetInt32(1))));
        return candidates.Where(candidate => ReadFacts(candidate.Page)?.MetaRefresh is { DelaySeconds: 0 }).ToList();
    }

    /// <summary>Pages the crawl recorded as canonicalised, with the target of their first canonical in the head or a Link header.</summary>
    private List<(int Page, int Target)> ReadCanonicalTargets()
    {
        List<(int Page, int Target)> canonicals = [];
        Rows($"""
            SELECT r.url_id, (SELECT l.to_id FROM links l
                              WHERE l.from_id = r.url_id AND l.kind = {(int)LinkKind.Canonical} AND (l.flags & {(int)LinkFlags.OutsideHead}) = 0
                              ORDER BY l.rowid LIMIT 1)
            FROM responses r WHERE r.indexability_reason = {(int)IndexabilityReason.Canonicalised}
            """, reader =>
        {
            if (!reader.IsDBNull(1))
            {
                canonicals.Add((reader.GetInt32(0), reader.GetInt32(1)));
            }
        });
        return canonicals;
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
