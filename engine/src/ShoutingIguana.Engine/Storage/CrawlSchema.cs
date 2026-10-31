namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// Crawl database layout. URLs are interned once in <c>urls</c>, and every other table refers to them by
/// integer id. That keeps the largest table, <c>links</c>, to a few small integers per row.
/// </summary>
internal static class CrawlSchema
{
    public const int VERSION = 2;

    public const string CREATE = """
        CREATE TABLE crawl (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            start_url TEXT NOT NULL,
            options_json TEXT NOT NULL,
            status INTEGER NOT NULL,
            started_at INTEGER NOT NULL,
            updated_at INTEGER NOT NULL,
            finished_at INTEGER,
            sitemaps_done INTEGER NOT NULL DEFAULT 0,
            engine_version TEXT NOT NULL
        );

        CREATE TABLE urls (
            id INTEGER PRIMARY KEY,
            url TEXT NOT NULL UNIQUE,
            is_internal INTEGER NOT NULL,
            fetch_mode INTEGER NOT NULL,
            depth INTEGER NOT NULL,
            state INTEGER NOT NULL,
            skip_reason INTEGER NOT NULL,
            source INTEGER NOT NULL,
            found_on_id INTEGER,
            found_via INTEGER
        );

        -- Only queued rows are indexed, so the frontier index shrinks as the crawl progresses.
        CREATE INDEX ix_urls_queue ON urls (is_internal, depth, id) WHERE state = 0;

        CREATE TABLE responses (
            url_id INTEGER PRIMARY KEY,
            fetched_at INTEGER NOT NULL,
            outcome INTEGER NOT NULL,
            status_code INTEGER,
            content_type TEXT,
            size_bytes INTEGER,
            body_bytes INTEGER,
            ttfb_ms INTEGER NOT NULL,
            response_ms INTEGER NOT NULL,
            http_version TEXT,
            redirect_url_id INTEGER,
            block_reason TEXT,
            error TEXT,
            is_html INTEGER NOT NULL,
            title TEXT,
            title_count INTEGER NOT NULL,
            meta_description TEXT,
            meta_description_count INTEGER NOT NULL,
            h1 TEXT,
            h1_count INTEGER NOT NULL,
            canonical_url_id INTEGER,
            is_noindex INTEGER NOT NULL,
            is_nofollow INTEGER NOT NULL,
            indexability INTEGER NOT NULL,
            indexability_reason INTEGER NOT NULL,
            word_count INTEGER,
            content_hash INTEGER,
            simhash INTEGER,
            language TEXT,
            outlink_count INTEGER NOT NULL
        );

        -- Brotli-compressed JSON (headers + page facts) and, when enabled, compressed HTML.
        CREATE TABLE response_details (url_id INTEGER PRIMARY KEY, data BLOB NOT NULL);
        CREATE TABLE response_html (url_id INTEGER PRIMARY KEY, html BLOB NOT NULL);

        -- text holds anchor text, image alt text, or the hreflang code, depending on kind.
        CREATE TABLE links (
            from_id INTEGER NOT NULL,
            to_id INTEGER NOT NULL,
            kind INTEGER NOT NULL,
            flags INTEGER NOT NULL,
            position INTEGER NOT NULL,
            text TEXT,
            fragment TEXT
        );

        CREATE INDEX ix_links_from ON links (from_id);
        CREATE INDEX ix_links_to ON links (to_id);

        CREATE TABLE robots (
            origin TEXT PRIMARY KEY,
            status_code INTEGER,
            content TEXT,
            error TEXT,
            fetched_at INTEGER NOT NULL
        );

        CREATE TABLE sitemaps (
            id INTEGER PRIMARY KEY,
            url TEXT NOT NULL UNIQUE,
            parent_id INTEGER,
            status_code INTEGER,
            is_index INTEGER NOT NULL DEFAULT 0,
            url_count INTEGER NOT NULL DEFAULT 0,
            error TEXT,
            -- 1 when nothing declared a sitemap and the crawler tried the conventional /sitemap.xml.
            is_probe INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE sitemap_urls (
            sitemap_id INTEGER NOT NULL,
            url_id INTEGER NOT NULL,
            PRIMARY KEY (sitemap_id, url_id)
        ) WITHOUT ROWID;
        """;

    /// <summary>
    /// Analysis results, created on first analysis and replaced as a whole by every later one. Severity and
    /// category are stored as text so the file explains itself in any SQLite browser.
    /// </summary>
    public const string ANALYSIS = """
        CREATE TABLE IF NOT EXISTS analysis (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            analyzed_at INTEGER NOT NULL,
            engine_version TEXT NOT NULL,
            settings_json TEXT NOT NULL,
            pages INTEGER NOT NULL,
            duration_ms INTEGER NOT NULL
        );

        -- What the last analysis couldn't do and why (checks this crawl can't support, rules that failed), in order.
        CREATE TABLE IF NOT EXISTS analysis_notes (id INTEGER PRIMARY KEY, note TEXT NOT NULL);

        -- Every check that ran, with how many URLs it flagged (0 = checked and clean). Checks the crawl can't
        -- support, such as orphans on a depth-limited crawl, are left out.
        CREATE TABLE IF NOT EXISTS issue_types (
            id TEXT PRIMARY KEY,
            category TEXT NOT NULL,
            severity TEXT NOT NULL,
            title TEXT NOT NULL,
            description TEXT NOT NULL,
            fix TEXT NOT NULL,
            url_count INTEGER NOT NULL
        ) WITHOUT ROWID;

        -- One row per flagged URL and issue. related_url_id is the other URL involved, when there is one:
        -- the broken link target, the duplicate, the canonical, the redirect target.
        CREATE TABLE IF NOT EXISTS issues (
            issue TEXT NOT NULL,
            url_id INTEGER NOT NULL,
            detail TEXT,
            related_url_id INTEGER,
            PRIMARY KEY (issue, url_id)
        ) WITHOUT ROWID;

        CREATE INDEX IF NOT EXISTS ix_issues_url ON issues (url_id);
        """;
}
