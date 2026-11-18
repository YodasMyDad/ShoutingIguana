using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Storage;

namespace ShoutingIguana.Engine.Tests.Support;

/// <summary>
/// Crawl files as earlier engines made them, from their real DDL, frozen here as it shipped: the first file of schema 2
/// (<see cref="SCHEMA_2"/>, unchanged in <c>CrawlSchema.CREATE</c> since), and the first rechecks' tables
/// (<see cref="FIRST_RECHECKS"/>). Every crawl-time table and column added since is what the engine's upgrade must add.
/// </summary>
internal static class OlderCrawlFiles
{
    /// <summary>The crawl file's tables as the first schema 2 engine created them (commit 90589c69).</summary>
    public const string SCHEMA_2 = """
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

        CREATE TABLE response_details (url_id INTEGER PRIMARY KEY, data BLOB NOT NULL);
        CREATE TABLE response_html (url_id INTEGER PRIMARY KEY, html BLOB NOT NULL);

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
            is_probe INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE sitemap_urls (
            sitemap_id INTEGER NOT NULL,
            url_id INTEGER NOT NULL,
            PRIMARY KEY (sitemap_id, url_id)
        ) WITHOUT ROWID;
        """;

    /// <summary>The rechecks' tables as the first engine to recheck created them (commit 19c4317b).</summary>
    public const string FIRST_RECHECKS = """
        CREATE TABLE rechecks (
            id INTEGER PRIMARY KEY,
            run INTEGER NOT NULL,
            started_at INTEGER NOT NULL,
            ended_at INTEGER,
            status INTEGER NOT NULL,
            check_id TEXT,
            group_detail TEXT,
            group_related_url_id INTEGER,
            url_list INTEGER NOT NULL DEFAULT 0,
            kinds TEXT NOT NULL,
            judged INTEGER NOT NULL,
            fetched INTEGER NOT NULL DEFAULT 0,
            count_before INTEGER,
            score_before INTEGER,
            score_after INTEGER,
            fixed INTEGER,
            still INTEGER,
            not_checked INTEGER,
            not_rechecked INTEGER,
            new INTEGER,
            settled_at INTEGER
        );

        CREATE TABLE recheck_urls (
            recheck_id INTEGER NOT NULL,
            url_id INTEGER NOT NULL,
            role INTEGER NOT NULL,
            flagged_before INTEGER NOT NULL,
            detail_before TEXT,
            related_before INTEGER,
            outcome INTEGER,
            PRIMARY KEY (recheck_id, url_id)
        ) WITHOUT ROWID;

        CREATE TABLE recheck_targets (
            recheck_id INTEGER NOT NULL,
            url_id INTEGER NOT NULL,
            related_url_id INTEGER NOT NULL,
            PRIMARY KEY (recheck_id, url_id, related_url_id)
        ) WITHOUT ROWID;
        """;

    /// <summary>Every table's columns, as (table, column, declared type), of a file or of an in-memory database.</summary>
    public static SortedSet<string> Columns(SqliteConnection connection)
    {
        SortedSet<string> columns = new(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.name, p.name, upper(p.type) FROM sqlite_master m, pragma_table_info(m.name) p
            WHERE m.type = 'table' AND m.name NOT LIKE 'sqlite_%'
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            columns.Add($"{reader.GetString(0)}.{reader.GetString(1)} {reader.GetString(2)}");
        }

        return columns;
    }

    public static SortedSet<string> Columns(string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        return Columns(connection);
    }

    /// <summary>The crawl-time tables and columns a new crawl's file has as its writer opens it, before any analysis.</summary>
    public static async Task<SortedSet<string>> NewCrawlColumnsAsync()
    {
        using var files = new CrawlFiles();
        var path = files.NewPath();
        await using (CrawlWriter.Open(path, new CrawlOptions { StartUrl = "https://example.com/" }, resume: false, new CrawlCounters(), NullLogger.Instance))
        {
        }

        return Columns(path);
    }

    /// <summary>A file of <paramref name="ddl"/> alone, at schema 2, as an earlier engine left it before it stored anything.</summary>
    public static void Create(string path, string ddl)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        Execute(connection, ddl + "\nPRAGMA user_version = 2;");
    }

    /// <summary>
    /// Takes a file this engine made back to <see cref="SCHEMA_2"/> as far as its crawl goes: every crawl-time table and column
    /// a new crawl's file has that the first schema 2 file didn't goes, with what it held. Its analysis stays as it is.
    /// </summary>
    public static async Task DowngradeAsync(string path)
    {
        var added = (await NewCrawlColumnsAsync()).Except(Schema2Columns()).ToList();
        var firstTables = Schema2Columns().Select(column => column.Split('.')[0]).ToHashSet(StringComparer.Ordinal);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        foreach (var table in added.Select(column => column.Split('.')[0]).Where(table => !firstTables.Contains(table)).Distinct())
        {
            Execute(connection, $"DROP TABLE IF EXISTS {table}");
        }

        foreach (var column in added.Where(column => firstTables.Contains(column.Split('.')[0])))
        {
            var (table, name) = (column.Split('.')[0], column.Split('.')[1].Split(' ')[0]);
            Execute(connection, $"ALTER TABLE {table} DROP COLUMN {name}");
        }
    }

    private static SortedSet<string> Schema2Columns()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        Execute(connection, SCHEMA_2);
        return Columns(connection);
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
