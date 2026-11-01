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
    /// category are stored as text so the file explains itself in any SQLite browser. A file analysed by an earlier
    /// engine gets what it lacks on its next analysis: new tables here, new columns from <see cref="ANALYSIS_COLUMNS"/>.
    /// </summary>
    public const string ANALYSIS = """
        -- Which engine ran the last analysis, and against what: the crawl's updated_at as it stood, the engine's analysis
        -- version and checks fingerprint, the settings. That tells whether it is out of date. Columns after duration_ms are
        -- null when an earlier engine analysed the crawl (and when an earlier engine analyses it again), and so they
        -- also say whether url_summary and skipped_checks belong to this analysis.
        CREATE TABLE IF NOT EXISTS analysis (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            analyzed_at INTEGER NOT NULL,
            engine_version TEXT NOT NULL,
            settings_json TEXT NOT NULL,
            pages INTEGER NOT NULL,
            duration_ms INTEGER NOT NULL,
            is_partial INTEGER,
            crawl_updated_at INTEGER,
            analysis_version INTEGER,
            checks_fingerprint TEXT
        );

        -- What the last analysis couldn't do and why (checks this crawl can't support, rules that failed), in order.
        CREATE TABLE IF NOT EXISTS analysis_notes (id INTEGER PRIMARY KEY, note TEXT NOT NULL);

        -- The checks the last analysis skipped, each with a stable reason code and a sentence saying why. The notes say it
        -- for the analysis as a whole; this is per check, for the UI.
        CREATE TABLE IF NOT EXISTS skipped_checks (
            check_id TEXT PRIMARY KEY,
            reason TEXT NOT NULL,
            text TEXT NOT NULL
        ) WITHOUT ROWID;

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

        -- Indexability as the analysis judged it, only where that differs from responses.indexability_reason: an
        -- instant meta refresh is a redirect, and a canonical leading back to the page or to a broken URL is ignored.
        -- Read COALESCE(e.indexability_reason, r.indexability_reason) for what search engines will see.
        CREATE TABLE IF NOT EXISTS effective_indexability (
            url_id INTEGER PRIMARY KEY,
            indexability_reason INTEGER NOT NULL
        );
        """;

    /// <summary>The <c>analysis</c> columns added since the table was first created, which files analysed earlier lack.</summary>
    public static readonly (string Name, string Type)[] ANALYSIS_COLUMNS =
        [("is_partial", "INTEGER"), ("crawl_updated_at", "INTEGER"), ("analysis_version", "INTEGER"), ("checks_fingerprint", "TEXT")];

    /// <summary>
    /// The URL grids' table: one row per URL, rebuilt by every analysis (dropped and created again, so a later engine
    /// can change its columns freely). It holds what the analysis judged next to copies of the crawl's numbers the grids
    /// sort and filter on, so a sort or filter reads this one narrow table and never joins; text (the URL, title,
    /// description) stays in urls and responses. Null means unknown: the crawl never fetched the URL (or wasn't able to
    /// ask, because its host had stopped answering).
    /// </summary>
    public const string URL_SUMMARY = """
        DROP TABLE IF EXISTS url_summary;
        CREATE TABLE url_summary (
            url_id INTEGER PRIMARY KEY,
            is_internal INTEGER NOT NULL,
            depth INTEGER NOT NULL,
            -- ResourceType: page, image, script, stylesheet, font, media, PDF or other.
            resource_type INTEGER NOT NULL,
            status_code INTEGER,
            -- Indexability as the analysis judged it (see effective_indexability): 1 or 0, with the IndexabilityReason.
            indexable INTEGER,
            indexability_reason INTEGER,
            -- Where the URL's redirects (meta refreshes included) end, and the status there.
            final_url_id INTEGER NOT NULL,
            final_status_code INTEGER,
            in_sitemap INTEGER NOT NULL,
            -- Hyperlinks to the URL from the pages audited, not counting a page's links to itself: every one, those
            -- followed and those not, and the pages they're on.
            inlinks INTEGER NOT NULL,
            followed_inlinks INTEGER NOT NULL,
            nofollow_inlinks INTEGER NOT NULL,
            linking_pages INTEGER NOT NULL,
            -- Every hyperlink on the page, as stored in responses.outlink_count.
            outlinks INTEGER,
            -- Checks that flagged the URL, in all and by severity.
            issues INTEGER NOT NULL,
            errors INTEGER NOT NULL,
            warnings INTEGER NOT NULL,
            notices INTEGER NOT NULL,
            size_bytes INTEGER,
            response_ms INTEGER,
            word_count INTEGER
        );
        """;

    /// <summary>
    /// Built once the rows are in, which is far quicker than keeping them up to date row by row. One per column the grids
    /// sort by most or filter on selectively: with LIMIT, a sort walks its index and stops after a page, and a selective
    /// filter (a status, a non-indexable reason, a depth, a rare type) reads only its rows. Every index ends with the row
    /// id, which keyset paging and ties need. The other columns (is_internal, in_sitemap, the other inlink counts, the
    /// severities) are cheap to scan in a table this narrow, about 100 ms per million URLs. This SQLite has no STAT4
    /// histograms, so its planner can't tell a common value from a rare one; the grids' readers count the matching rows
    /// first and choose between the filter's index and the sort's (engine/README.md).
    /// </summary>
    public const string URL_SUMMARY_INDEXES = """
        CREATE INDEX ix_url_summary_status ON url_summary (status_code);
        CREATE INDEX ix_url_summary_indexability ON url_summary (indexability_reason);
        CREATE INDEX ix_url_summary_type ON url_summary (resource_type);
        CREATE INDEX ix_url_summary_depth ON url_summary (depth);
        CREATE INDEX ix_url_summary_linking_pages ON url_summary (linking_pages);
        CREATE INDEX ix_url_summary_issues ON url_summary (issues);
        CREATE INDEX ix_url_summary_response_ms ON url_summary (response_ms);
        CREATE INDEX ix_url_summary_size ON url_summary (size_bytes);
        CREATE INDEX ix_url_summary_words ON url_summary (word_count);
        """;
}
