namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// Crawl database layout. URLs are interned once in <c>urls</c>, and every other table refers to them by
/// integer id. That keeps the largest table, <c>links</c>, to a few small integers per row.
/// </summary>
internal static class CrawlSchema
{
    public const int VERSION = 2;

    /// <summary>
    /// A page, as every count of pages goes by (si stats, the library, a crawl's live progress): an internal URL that
    /// answered with HTML the crawler read and parsed. A URL that was only status-checked has no word count, so it isn't
    /// one. SQL over <c>responses r</c> joined to <c>urls u</c>; outcome 0 is a success.
    /// </summary>
    public const string PAGE = "u.is_internal = 1 AND r.is_html = 1 AND r.outcome = 0 AND r.word_count IS NOT NULL";

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
    /// What the crawl told the person running it (the site blocking it, robots.txt unreadable, the site down, the sitemaps
    /// read), every run's, in the order raised. A crawl-time table the writer creates whenever it opens the file, so a file
    /// from an earlier engine gets it when it is continued; readers find none in a file that never was. The level is text,
    /// so the file explains itself; data is JSON of the facts the notice is about (<c>CrawlNoticeData</c>), or null.
    /// </summary>
    public const string NOTICES = """
        CREATE TABLE IF NOT EXISTS notices (
            id INTEGER PRIMARY KEY,
            at INTEGER NOT NULL,
            level TEXT NOT NULL,
            code TEXT NOT NULL,
            message TEXT NOT NULL,
            data TEXT
        );
        """;

    /// <summary>
    /// The time the crawl spent crawling: a row for each stretch of it, from a run's start (or its resuming after a pause)
    /// to its next pause or its end, so the time paused and the time between a stop and a continue are in none. A crawl-time
    /// table, created whenever the writer opens a file, as <see cref="NOTICES"/> is.
    /// <list type="bullet">
    /// <item><c>run</c> numbers the crawl's runs from 1: a run's stretches share it, and a pause is the gap between two.</item>
    /// <item><c>crawled_ms</c> is how long the stretch went on, by a clock that only runs forward. The writer stores it
    /// every few seconds while the stretch goes on, so a run cut short (the process died) loses only seconds. Null in the
    /// one row that stands for runs nobody timed: every run before the table was there, or one by an engine that doesn't
    /// keep it. With such a row the crawl's whole time isn't known, and readers say so rather than give a part of it.</item>
    /// <item><c>ended</c> is 1 once the stretch has ended; 0 while it goes on, and until the crawl is continued where its
    /// process died.</item>
    /// <item><c>crawl_updated_at</c> is the crawl row's stamp as it stood when the row was stored, and the last row's is
    /// moved on with it whenever this engine stamps the crawl row: as a run opens the file and as it ends. Every run stamps
    /// the crawl row, whichever engine runs it, so when the last row's isn't the crawl row's own, a run since went
    /// untimed: readers then don't know the crawl's time, and the next run this engine makes says so with the row above.</item>
    /// </list>
    /// </summary>
    public const string RUN_TIME = """
        CREATE TABLE IF NOT EXISTS run_time (
            id INTEGER PRIMARY KEY,
            run INTEGER NOT NULL,
            started_at INTEGER NOT NULL,
            crawled_ms INTEGER,
            ended INTEGER NOT NULL,
            crawl_updated_at INTEGER NOT NULL
        );
        """;

    /// <summary>
    /// The columns of <see cref="RUN_TIME"/>, in order. A table of any other shape is another program's: the writer then
    /// keeps no time (a crawl matters more than how long it took), and readers don't know the crawl's.
    /// </summary>
    public const string RUN_TIME_COLUMNS = "id,run,started_at,crawled_ms,ended,crawl_updated_at";

    /// <summary>
    /// The columns of <see cref="NOTICES"/>, which the writer stores into and readers read by name. Another program can
    /// have made the table its own way: readers then find no notices, and the writer refuses to continue the crawl.
    /// </summary>
    public static readonly string[] NOTICE_COLUMNS = ["id", "at", "level", "code", "message", "data"];

    /// <summary>
    /// The <c>responses</c> columns added since the table was first created: lanes 1 to 3 of a page's 256-bit content
    /// fingerprint (<c>simhash</c> is lane 0), stored bit for bit as signed integers. The writer adds what's missing
    /// whenever it opens a file, so an earlier engine's crawl gets them when it is continued, and its pages so far keep
    /// null; so do the pages an earlier engine fetches when it continues a crawl this one started, its inserts naming only
    /// the columns it knows. The crawl's schema version doesn't change, so either engine can read and continue either file.
    /// </summary>
    public static readonly (string Name, string Type)[] RESPONSE_COLUMNS = [("simhash_1", "INTEGER"), ("simhash_2", "INTEGER"), ("simhash_3", "INTEGER")];

    /// <summary>
    /// The <c>sitemaps</c> columns added since the table was first created, which the writer adds whenever it opens a file,
    /// as it does <see cref="RESPONSE_COLUMNS"/>:
    /// <list type="bullet">
    /// <item><c>listed_sitemaps</c> is how many sitemaps the file lists (its <c>&lt;sitemap&gt;</c> entries, as read: repeats
    /// and entries that aren't URLs included), so an index says how many it names beside how many of them the crawl has;
    /// 0 for a sitemap that lists only URLs. Null when the file's body was never read (it failed first, or was left
    /// unread), and for every sitemap an engine from before the column read.</item>
    /// </list>
    /// </summary>
    public static readonly (string Name, string Type)[] SITEMAP_COLUMNS = [("listed_sitemaps", "INTEGER")];

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
            checks_fingerprint TEXT,
            -- The URLs crawled as they stood (responses, less the ones never asked): what a URL check's count is a share of.
            crawled_urls INTEGER,
            -- The score: the version of the formula, the number (null when no page was read), whether it is of a crawl
            -- that didn't read all of the site, and the whole of it with every line, as JSON.
            score_version INTEGER,
            score INTEGER,
            score_partial INTEGER,
            score_json TEXT
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

        -- One row per flagged URL and issue. related_url_id is the other URL involved, when there is one: the broken link
        -- target (the first, where a page has several: issue_targets has them all), the duplicate, the canonical, the
        -- redirect target.
        CREATE TABLE IF NOT EXISTS issues (
            issue TEXT NOT NULL,
            url_id INTEGER NOT NULL,
            detail TEXT,
            related_url_id INTEGER,
            PRIMARY KEY (issue, url_id)
        ) WITHOUT ROWID;

        CREATE INDEX IF NOT EXISTS ix_issues_url ON issues (url_id);

        -- A check's issues by detail and other URL, as the Issues screen groups them, each group's URLs in crawl order
        -- (an index on a table without rowid ends with the rest of its key: url_id).
        CREATE INDEX IF NOT EXISTS ix_issues_group ON issues (issue, detail, related_url_id);

        -- Every other URL each issue is about, where issues keeps only the first: each of a page's broken (redirecting,
        -- nofollow, large...) targets, and the one other URL of the rest (the canonical, the redirect's end). A duplicate
        -- names the first page of its cluster instead, which that page names too, so a cluster is one group. The Issues
        -- screen groups a check's issues by the other URL from here, a page in each of its targets' groups, and the
        -- inspector counts the issues naming a URL ("named by"), its own left out. An issue with no other URL has no row.
        CREATE TABLE IF NOT EXISTS issue_targets (
            issue TEXT NOT NULL,
            related_url_id INTEGER NOT NULL,
            url_id INTEGER NOT NULL,
            PRIMARY KEY (issue, related_url_id, url_id)
        ) WITHOUT ROWID;

        -- The issues naming a URL, by check, without reading the others.
        CREATE INDEX IF NOT EXISTS ix_issue_targets_named ON issue_targets (related_url_id, issue);

        -- The URLs one issue names, for the inspector to say which group it's in, without reading the check's other issues.
        -- An analysis from before this index is read through the key, a check at a time.
        CREATE INDEX IF NOT EXISTS ix_issue_targets_url ON issue_targets (url_id, issue);

        -- The fault a check's issues group by where each issue's detail is the page's own instance of it (a JSON-LD
        -- parser's message with where in the block it failed, a canonical's href), so a fault in the site's template is one
        -- group. Only for the checks the catalog gives a fault (IssueDefinition.FaultOf).
        CREATE TABLE IF NOT EXISTS issue_faults (
            issue TEXT NOT NULL,
            fault TEXT NOT NULL,
            url_id INTEGER NOT NULL,
            PRIMARY KEY (issue, fault, url_id)
        ) WITHOUT ROWID;

        -- Indexability as the analysis judged it, only where that differs from responses.indexability_reason: an
        -- instant meta refresh is a redirect, and a canonical leading back to the page or to a broken URL is ignored.
        -- Read COALESCE(e.indexability_reason, r.indexability_reason) for what search engines will see.
        CREATE TABLE IF NOT EXISTS effective_indexability (
            url_id INTEGER PRIMARY KEY,
            indexability_reason INTEGER NOT NULL
        );
        """;

    /// <summary>
    /// The first <see cref="Analysis.AnalysisFreshness.VERSION"/> that stores <c>issue_targets</c> and <c>issue_faults</c>.
    /// An analysis by an earlier one has neither, or left a later one's behind, as it doesn't know to clear them: readers
    /// then group by the issues' own detail and first other URL, as that engine did.
    /// </summary>
    public const int ISSUE_TARGETS_SINCE = 4;

    /// <summary>
    /// The links to each URL by kind, which an analysis adds (a crawl doesn't need it, and a crawl continued since keeps it
    /// up to date). The inspector counts a URL's links of some kinds on it alone, and reaches a page of them at any offset by
    /// merging each kind's, already in crawl order: a nav target's million hyperlinks, a stylesheet's million links.
    /// </summary>
    public const string LINK_KINDS_INDEX = "CREATE INDEX IF NOT EXISTS ix_links_to_kind ON links (to_id, kind)";

    /// <summary>
    /// The links grid's table: the crawl's links counted by where they lead and what they are, built by the first analysis
    /// of the crawl as it stands, and again once the crawl has changed. A crawl of a million pages stores 50 million links, which no read can count in time;
    /// this holds about 2 rows a URL, in target order, and answers what the grid counts: the links matching any filter on
    /// these columns and on the target, the facets, and each target's matching links (for a sort by the target). The
    /// columns hold what <c>links</c> does, as it is.
    /// <c>link_blocks</c> counts the same links by where they are in crawl order instead of by target: by block of
    /// consecutive link ids (<see cref="LINK_BLOCK_SHIFT"/>), a few rows a block. A page anywhere in crawl order, under a filter on
    /// what a link is, is found by adding the blocks' counts up to it and reading the links of the block it starts in.
    /// <c>link_summary_state</c> says which state of the crawl they count: the crawl row's stamp (<c>updated_at</c>) as it
    /// stood, which every run of the crawl moves on. They count nothing but the links and which pages say nofollow, which
    /// only a crawl changes, so they stand until the stamp moves, whoever analyses the file meanwhile, and are passed over
    /// once it has. It also keeps the number of links and the highest link id, which the grid would otherwise count,
    /// the size of a block, which a read takes from the file, and the version of the counting (<see cref="LINK_SUMMARY_VERSION"/>).
    /// </summary>
    public const string LINK_SUMMARY = """
        DROP TABLE IF EXISTS link_summary;
        CREATE TABLE link_summary (
            to_id INTEGER NOT NULL,
            kind INTEGER NOT NULL,
            position INTEGER NOT NULL,
            flags INTEGER NOT NULL,
            -- 1 when the link leads to the page it's on.
            self INTEGER NOT NULL,
            -- 1 when it has text.
            has_text INTEGER NOT NULL,
            -- 1 when its page's robots directives say nofollow, so none of its hyperlinks are followed.
            source_nofollow INTEGER NOT NULL,
            links INTEGER NOT NULL,
            PRIMARY KEY (to_id, kind, position, flags, self, has_text, source_nofollow)
        ) WITHOUT ROWID;
        DROP TABLE IF EXISTS link_blocks;
        CREATE TABLE link_blocks (
            -- The link ids it counts, divided by the block size: links block * size to (block + 1) * size - 1.
            block INTEGER NOT NULL,
            kind INTEGER NOT NULL,
            position INTEGER NOT NULL,
            flags INTEGER NOT NULL,
            self INTEGER NOT NULL,
            has_text INTEGER NOT NULL,
            source_nofollow INTEGER NOT NULL,
            links INTEGER NOT NULL,
            PRIMARY KEY (block, kind, position, flags, self, has_text, source_nofollow)
        ) WITHOUT ROWID;
        DROP TABLE IF EXISTS link_summary_state;
        CREATE TABLE link_summary_state (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            crawl_updated_at INTEGER NOT NULL,
            links INTEGER NOT NULL,
            max_link_id INTEGER NOT NULL,
            -- A block of link_blocks counts 2 to this power consecutive link ids.
            block_shift INTEGER NOT NULL,
            -- How the links were counted: the engine's CrawlSchema.LINK_SUMMARY_VERSION when it built them.
            version INTEGER NOT NULL
        );
        """;

    /// <summary>
    /// The link summary's tables with their columns in order, as this engine builds and reads them: tables of another shape
    /// (a later engine's, another program's) are built again by an analysis, and passed over by a read.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> LINK_SUMMARY_TABLES = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["link_summary"] = "to_id,kind,position,flags,self,has_text,source_nofollow,links",
        ["link_blocks"] = "block,kind,position,flags,self,has_text,source_nofollow,links",
        ["link_summary_state"] = "id,crawl_updated_at,links,max_link_id,block_shift,version"
    };

    /// <summary>
    /// How this engine counts the links into the link summary. The tables' columns say what shape a summary has; this says
    /// what its numbers mean, so it moves on with any change to how a link is counted that keeps the columns as they are (a
    /// fix to which links count as followed, say). A summary of another version is passed over by a read and counted again
    /// by the next analysis, though the crawl hasn't changed, as one of another shape is. It isn't the analysis's version
    /// (<see cref="Analysis.AnalysisFreshness.VERSION"/>): no check's result is out of date for it.
    /// </summary>
    public const int LINK_SUMMARY_VERSION = 1;

    /// <summary>
    /// How many consecutive link ids a row of <c>link_blocks</c> counts, as a shift: 16,384. A page of 100 links under a
    /// filter is read from the blocks that hold them, so at most 100 blocks' links are passed over to fill it (1.6 million
    /// rows), however large the crawl and however few links match; the table holds 300 blocks for the 100k benchmark's 5
    /// million links, some 15 rows each. A build stores the size it counted by (<c>link_summary_state.block_shift</c>), and
    /// a read goes by the file's.
    /// </summary>
    public const int LINK_BLOCK_SHIFT = 14;

    /// <summary>The <c>analysis</c> columns added since the table was first created, which files analysed earlier lack.</summary>
    public static readonly (string Name, string Type)[] ANALYSIS_COLUMNS =
    [
        ("is_partial", "INTEGER"), ("crawl_updated_at", "INTEGER"), ("analysis_version", "INTEGER"), ("checks_fingerprint", "TEXT"),
        ("crawled_urls", "INTEGER"), ("score_version", "INTEGER"), ("score", "INTEGER"), ("score_partial", "INTEGER"), ("score_json", "TEXT")
    ];

    /// <summary>
    /// The URL grids' table: one row per URL, rebuilt by every analysis (dropped and created again, so a later engine
    /// can change its columns freely). It holds what the analysis judged next to copies of the crawl's numbers the grids
    /// sort and filter on, so a sort or filter reads this one narrow table and never joins; text (the URL, title,
    /// description) stays in urls and responses. Null means unknown: the crawl never fetched the URL (or wasn't able to
    /// ask, because its host had stopped answering), never read what decides it (a page's HTML, which a URL that was
    /// only status-checked doesn't have), or couldn't tell (a sitemap left unread, a URL no link leads to).
    /// </summary>
    public const string URL_SUMMARY = """
        DROP TABLE IF EXISTS url_summary;
        CREATE TABLE url_summary (
            url_id INTEGER PRIMARY KEY,
            is_internal INTEGER NOT NULL,
            -- DiscoverySource: how the crawl found the URL. 3 is the crawler's probe of the site's other versions, which
            -- nothing on the site leads to: the grids hide or label those.
            source INTEGER NOT NULL,
            -- Clicks from the start URL; null for a URL only a sitemap lists.
            depth INTEGER,
            -- ResourceType: page, image, script, stylesheet, font, media, PDF or other.
            resource_type INTEGER NOT NULL,
            status_code INTEGER,
            -- Indexability as the analysis judged it (see effective_indexability): 1 or 0, with the IndexabilityReason.
            indexable INTEGER,
            indexability_reason INTEGER,
            -- Where the URL's redirects (meta refreshes included) end, and the status there.
            final_url_id INTEGER NOT NULL,
            final_status_code INTEGER,
            -- 1 when a sitemap lists it; 0 only when every sitemap was read (the not-in-sitemap check ran), else null.
            in_sitemap INTEGER,
            -- Hyperlinks to the URL from the pages audited, not counting a page's links to itself: every one, those
            -- followed and those not, and the pages they're on.
            inlinks INTEGER NOT NULL,
            followed_inlinks INTEGER NOT NULL,
            nofollow_inlinks INTEGER NOT NULL,
            linking_pages INTEGER NOT NULL,
            -- Every hyperlink on the page, as stored in responses.outlink_count; null when its HTML wasn't read.
            outlinks INTEGER,
            -- Checks that flagged the URL, in all and by severity.
            issues INTEGER NOT NULL,
            errors INTEGER NOT NULL,
            warnings INTEGER NOT NULL,
            notices INTEGER NOT NULL,
            size_bytes INTEGER,
            response_ms INTEGER,
            word_count INTEGER,
            -- The room the first title and meta description take in search results, as the checks measure it (full-width
            -- characters and emoji count two); null without one.
            title_length INTEGER,
            description_length INTEGER
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
