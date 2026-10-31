using System.Globalization;
using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>The writer's SQL, compiled once and reused for every row.</summary>
internal sealed class WriterStatements : IDisposable
{
    private readonly List<SqliteCommand> _commands = [];
    private readonly SqliteCommand _insertUrl;
    private readonly SqliteCommand _updateUrlState;
    private readonly SqliteCommand _updateUrlDepth;
    private readonly SqliteCommand _updateUrlMode;
    private readonly SqliteCommand _updateUrlSkip;
    private readonly SqliteCommand _requeueUrl;
    private readonly SqliteCommand _insertLink;
    private readonly SqliteCommand _insertResponse;
    private readonly SqliteCommand _readOutcome;
    private readonly SqliteCommand _deleteResponse;
    private readonly SqliteCommand _upsertDetails;
    private readonly SqliteCommand _upsertHtml;
    private readonly SqliteCommand _claim;
    private readonly SqliteCommand _skipQueuedInternal;
    private readonly SqliteCommand _readLinkTargets;
    private readonly SqliteCommand _upsertRobots;
    private readonly SqliteCommand _registerSitemap;
    private readonly SqliteCommand _updateSitemap;
    private readonly SqliteCommand _insertSitemapUrl;
    private readonly SqliteCommand _updateCrawlStatus;
    private readonly SqliteCommand _markSitemapsDone;

    public WriterStatements(SqliteConnection connection)
    {
        _insertUrl = Prepare(connection, """
            INSERT INTO urls (id, url, is_internal, fetch_mode, depth, state, skip_reason, source, found_on_id, found_via)
            VALUES ($id, $url, $internal, $mode, $depth, $state, $skip, $source, $foundOn, $via)
            """, "$id", "$url", "$internal", "$mode", "$depth", "$state", "$skip", "$source", "$foundOn", "$via");
        _updateUrlState = Prepare(connection, "UPDATE urls SET state = $state WHERE id = $id", "$state", "$id");
        _updateUrlDepth = Prepare(connection, "UPDATE urls SET depth = $depth WHERE id = $id", "$depth", "$id");
        _updateUrlMode = Prepare(connection, "UPDATE urls SET fetch_mode = $mode WHERE id = $id", "$mode", "$id");
        _updateUrlSkip = Prepare(connection, "UPDATE urls SET skip_reason = $skip, fetch_mode = $mode WHERE id = $id", "$skip", "$mode", "$id");
        _requeueUrl = Prepare(connection,
            "UPDATE urls SET state = 0, skip_reason = 0, fetch_mode = $mode, depth = $depth WHERE id = $id",
            "$mode", "$depth", "$id");
        _insertLink = Prepare(connection, """
            INSERT INTO links (from_id, to_id, kind, flags, position, text, fragment)
            VALUES ($from, $to, $kind, $flags, $position, $text, $fragment)
            """, "$from", "$to", "$kind", "$flags", "$position", "$text", "$fragment");
        _insertResponse = Prepare(connection, """
            INSERT OR REPLACE INTO responses (
                url_id, fetched_at, outcome, status_code, content_type, size_bytes, body_bytes, ttfb_ms, response_ms,
                http_version, redirect_url_id, block_reason, error, is_html, title, title_count, meta_description,
                meta_description_count, h1, h1_count, canonical_url_id, is_noindex, is_nofollow, indexability,
                indexability_reason, word_count, content_hash, simhash, language, outlink_count)
            VALUES (
                $urlId, $fetchedAt, $outcome, $status, $contentType, $size, $body, $ttfb, $total,
                $httpVersion, $redirect, $block, $error, $isHtml, $title, $titleCount, $description,
                $descriptionCount, $h1, $h1Count, $canonical, $noindex, $nofollow, $indexability,
                $indexabilityReason, $words, $contentHash, $simhash, $language, $outlinks)
            """, "$urlId", "$fetchedAt", "$outcome", "$status", "$contentType", "$size", "$body", "$ttfb", "$total",
            "$httpVersion", "$redirect", "$block", "$error", "$isHtml", "$title", "$titleCount", "$description",
            "$descriptionCount", "$h1", "$h1Count", "$canonical", "$noindex", "$nofollow", "$indexability",
            "$indexabilityReason", "$words", "$contentHash", "$simhash", "$language", "$outlinks");
        _readOutcome = Prepare(connection,
            "SELECT outcome, is_html, CASE WHEN body_bytes IS NOT NULL THEN COALESCE(size_bytes, 0) ELSE 0 END FROM responses WHERE url_id = $id", "$id");
        _deleteResponse = Prepare(connection, """
            DELETE FROM responses WHERE url_id = $id;
            DELETE FROM response_details WHERE url_id = $id;
            DELETE FROM response_html WHERE url_id = $id;
            DELETE FROM links WHERE from_id = $id;
            """, "$id");
        _upsertDetails = Prepare(connection,
            "INSERT OR REPLACE INTO response_details (url_id, data) VALUES ($id, $data)", "$id", "$data");
        _upsertHtml = Prepare(connection,
            "INSERT OR REPLACE INTO response_html (url_id, html) VALUES ($id, $html)", "$id", "$html");
        _claim = Prepare(connection, """
            UPDATE urls SET state = 1
            WHERE id IN (
                SELECT id FROM urls
                WHERE state = 0 AND is_internal = $internal AND depth >= $fromDepth AND depth < $toDepth
                ORDER BY depth, id
                LIMIT $limit)
            RETURNING id, url, depth, fetch_mode
            """, "$internal", "$limit", "$fromDepth", "$toDepth");
        _skipQueuedInternal = Prepare(connection, $"""
            UPDATE urls SET state = 3, skip_reason = {(int)SkipReason.UrlLimit}
            WHERE state = 0 AND is_internal = 1
            RETURNING id, url
            """);
        _readLinkTargets = Prepare(connection, """
            SELECT l.kind, u.url, u.is_internal FROM links l JOIN urls u ON u.id = l.to_id WHERE l.from_id = $id
            """, "$id");
        _upsertRobots = Prepare(connection, """
            INSERT OR REPLACE INTO robots (origin, status_code, content, error, fetched_at)
            VALUES ($origin, $status, $content, $error, $fetchedAt)
            """, "$origin", "$status", "$content", "$error", "$fetchedAt");
        _registerSitemap = Prepare(connection, """
            INSERT INTO sitemaps (url, parent_id) VALUES ($url, $parent)
            ON CONFLICT (url) DO UPDATE SET parent_id = COALESCE(sitemaps.parent_id, excluded.parent_id)
            RETURNING id
            """, "$url", "$parent");
        _updateSitemap = Prepare(connection, """
            UPDATE sitemaps SET status_code = $status, is_index = $isIndex, url_count = $count, error = $error, is_probe = $isProbe
            WHERE id = $id
            """, "$status", "$isIndex", "$count", "$error", "$isProbe", "$id");
        _insertSitemapUrl = Prepare(connection,
            "INSERT OR IGNORE INTO sitemap_urls (sitemap_id, url_id) VALUES ($sitemap, $url)", "$sitemap", "$url");
        _updateCrawlStatus = Prepare(connection, """
            UPDATE crawl SET status = $status, updated_at = $now,
                finished_at = CASE WHEN $finished = 1 THEN $now ELSE NULL END
            WHERE id = 1
            """, "$status", "$now", "$finished");
        _markSitemapsDone = Prepare(connection, "UPDATE crawl SET sitemaps_done = 1 WHERE id = 1");
    }

    public void SetTransaction(SqliteTransaction? transaction)
    {
        foreach (var command in _commands)
        {
            command.Transaction = transaction;
        }
    }

    public void InsertUrl(int id, OutLink link, FetchMode mode, int depth, UrlState state, SkipReason skip,
        DiscoverySource source, int? foundOnId) =>
        Run(_insertUrl, id, link.Url, link.IsInternal, (int)mode, depth, (int)state, (int)skip, (int)source,
            foundOnId, source == DiscoverySource.Link ? (int)link.Kind : null);

    public void UpdateUrlState(int id, UrlState state) => Run(_updateUrlState, (int)state, id);

    public void UpdateUrlDepth(int id, int depth) => Run(_updateUrlDepth, depth, id);

    public void UpdateUrlMode(int id, FetchMode mode) => Run(_updateUrlMode, (int)mode, id);

    public void UpdateUrlSkip(int id, SkipReason skip, FetchMode mode) => Run(_updateUrlSkip, (int)skip, (int)mode, id);

    public void RequeueUrl(int id, FetchMode mode, int depth) => Run(_requeueUrl, (int)mode, depth, id);

    public void InsertLink(int fromId, int toId, OutLink link) =>
        Run(_insertLink, fromId, toId, (int)link.Kind, (int)link.Flags, (int)link.Position, link.Text, link.Fragment);

    public void InsertResponse(ResponseRow row, int? redirectId, int? canonicalId, int outlinkCount) =>
        Run(_insertResponse,
            row.UrlId, row.FetchedAt, (int)row.Outcome, row.StatusCode, row.ContentType, row.SizeBytes, row.BodyBytes,
            row.TimeToFirstByteMs, row.ResponseMs, row.HttpVersion, redirectId, row.BlockReason, row.Error, row.IsHtml,
            row.Title, row.TitleCount, row.MetaDescription, row.MetaDescriptionCount, row.H1, row.H1Count, canonicalId,
            row.IsNoindex, row.IsNofollow, (int)row.Indexability, (int)row.IndexabilityReason, row.WordCount,
            AsSigned(row.ContentHash), AsSigned(row.SimHash), row.Language, outlinkCount);

    /// <summary>What the stored response of a URL was, or null when it has none.</summary>
    public (FetchOutcome Outcome, bool IsHtml, long Bytes)? ReadOutcome(int urlId)
    {
        _readOutcome.Parameters[0].Value = urlId;
        using var reader = _readOutcome.ExecuteReader();
        return reader.Read() ? ((FetchOutcome)reader.GetInt32(0), reader.GetBoolean(1), reader.GetInt64(2)) : null;
    }

    /// <summary>Removes everything a fetch of the URL stored, before it is fetched again.</summary>
    public void DeleteResponse(int urlId) => Run(_deleteResponse, urlId);

    public void UpsertDetails(int urlId, byte[] data) => Run(_upsertDetails, urlId, data);

    public void UpsertHtml(int urlId, byte[] html) => Run(_upsertHtml, urlId, html);

    /// <summary>Claims up to <paramref name="limit"/> queued URLs with a depth in [fromDepth, toDepth), shallowest first.</summary>
    public List<FrontierItem> ClaimQueued(bool isInternal, int limit, int fromDepth, int toDepth)
    {
        _claim.Parameters[0].Value = isInternal;
        _claim.Parameters[1].Value = limit;
        _claim.Parameters[2].Value = fromDepth;
        _claim.Parameters[3].Value = toDepth;

        List<FrontierItem> items = [];
        using var reader = _claim.ExecuteReader();
        while (reader.Read())
        {
            items.Add(new FrontierItem(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2), (FetchMode)reader.GetInt32(3), isInternal));
        }

        return items;
    }

    /// <summary>Skips every internal URL still queued, for the URL limit, and returns them.</summary>
    public List<(int Id, string Url)> SkipQueuedInternal()
    {
        List<(int Id, string Url)> skipped = [];
        using var reader = _skipQueuedInternal.ExecuteReader();
        while (reader.Read())
        {
            skipped.Add((reader.GetInt32(0), reader.GetString(1)));
        }

        return skipped;
    }

    /// <summary>Where a recorded URL's links go, read in full before anything is updated.</summary>
    public List<(LinkKind Kind, string Url, bool IsInternal)> ReadLinkTargets(int fromId)
    {
        _readLinkTargets.Parameters[0].Value = fromId;
        List<(LinkKind Kind, string Url, bool IsInternal)> targets = [];
        using var reader = _readLinkTargets.ExecuteReader();
        while (reader.Read())
        {
            targets.Add(((LinkKind)reader.GetInt32(0), reader.GetString(1), reader.GetBoolean(2)));
        }

        return targets;
    }

    public void UpsertRobots(RobotsRecord record) =>
        Run(_upsertRobots, record.Origin, record.StatusCode, record.Content, record.Error, record.FetchedAt);

    public int RegisterSitemap(string url, int? parentId)
    {
        _registerSitemap.Parameters[0].Value = url;
        _registerSitemap.Parameters[1].Value = (object?)parentId ?? DBNull.Value;
        return Convert.ToInt32(_registerSitemap.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public void UpdateSitemap(SitemapRecord record) =>
        Run(_updateSitemap, record.StatusCode, record.IsIndex, record.UrlCount, record.Error, record.IsProbe, record.Id);

    public void InsertSitemapUrl(int sitemapId, int urlId) => Run(_insertSitemapUrl, sitemapId, urlId);

    public void UpdateCrawlStatus(CrawlStatus status, bool isFinished, long now) =>
        Run(_updateCrawlStatus, (int)status, now, isFinished);

    public void MarkSitemapsDone() => Run(_markSitemapsDone);

    public void Dispose()
    {
        foreach (var command in _commands)
        {
            command.Dispose();
        }
    }

    private SqliteCommand Prepare(SqliteConnection connection, string sql, params string[] parameterNames)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var name in parameterNames)
        {
            command.Parameters.Add(new SqliteParameter { ParameterName = name });
        }

        _commands.Add(command);
        return command;
    }

    private static void Run(SqliteCommand command, params object?[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            command.Parameters[i].Value = values[i] ?? DBNull.Value;
        }

        command.ExecuteNonQuery();
    }

    /// <summary>SQLite integers are signed 64-bit; hashes are stored bit-for-bit.</summary>
    private static long? AsSigned(ulong? value) => value is { } hash ? unchecked((long)hash) : null;
}
