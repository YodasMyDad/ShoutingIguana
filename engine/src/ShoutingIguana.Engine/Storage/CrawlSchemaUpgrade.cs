using Microsoft.Data.Sqlite;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// Brings a crawl file up to this engine's crawl-time schema, whichever engine made it: every table and column added
/// since the first file of <see cref="CrawlSchema.VERSION"/>, each where it's missing. Whatever opens a file to write
/// crawl-time data (a new crawl, a continue, a recheck) calls it first, so no write ever meets a file without the
/// columns it names. The tables are created if not there; the columns, each nullable, are added with the rows a file has
/// keeping none; nothing else changes, and the schema version stays as it is, so either engine can read and continue
/// either file. An analysis writes only its own tables, which it brings up to date itself
/// (<see cref="CrawlSchema.ANALYSIS"/>, <see cref="CrawlSchema.ANALYSIS_COLUMNS"/>), and never these: readers tell a
/// file's facts from an earlier engine by the columns it lacks.
/// </summary>
/// <remarks>
/// A table or a column added to the crawl's schema goes here too: <c>CrawlSchemaUpgradeTests</c> fails until it does.
/// </remarks>
internal static class CrawlSchemaUpgrade
{
    /// <summary>The crawl-time tables made since the first file of the schema, in the order they came.</summary>
    public static readonly string[] TABLES = [CrawlSchema.NOTICES, CrawlSchema.RUN_TIME, CrawlSchema.FAVICON, CrawlSchema.RECHECKS, CrawlSchema.UNCOLLECTED, CrawlSchema.EXTRACTIONS];

    /// <summary>
    /// The columns whose null means something of its own, by <c>table.column</c>, with what a file already has that lacks
    /// the fact when the column is added (<see cref="CrawlSchema.UNCOLLECTED"/>): for a sitemap entry's lastmod, every
    /// sitemap read so far.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> UNCOLLECTED = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [CrawlSchema.LASTMOD_FACT] = "SELECT id FROM sitemaps"
    };

    /// <summary>The columns added since each table was first made, by table.</summary>
    public static readonly (string Table, (string Name, string Type)[] Columns)[] COLUMNS =
    [
        ("responses", CrawlSchema.RESPONSE_COLUMNS),
        ("sitemaps", CrawlSchema.SITEMAP_COLUMNS),
        ("sitemap_urls", CrawlSchema.SITEMAP_URL_COLUMNS),
        ("recheck_urls", CrawlSchema.RECHECK_URL_COLUMNS)
    ];

    /// <summary>Adds what the file lacks, each table and its columns in one transaction.</summary>
    /// <exception cref="InvalidOperationException">Another program made the notices table its own way, so the crawl's alerts couldn't be stored.</exception>
    public static void Apply(SqliteConnection connection, string databasePath)
    {
        using (var transaction = connection.BeginTransaction())
        {
            foreach (var table in TABLES)
            {
                CrawlDatabase.Execute(connection, table, transaction);
            }

            CheckNoticesTable(connection, transaction, databasePath);
            foreach (var (table, columns) in COLUMNS)
            {
                AddMissingColumns(connection, transaction, table, columns);
            }

            transaction.Commit();
        }
    }

    /// <summary>
    /// The notices table may be another program's: CREATE TABLE IF NOT EXISTS keeps whatever is there. Every notice is stored
    /// into the columns this engine made, so a table missing one, keyed by something other than the id, or with a column
    /// of its own that must have a value, would fail the crawl at its first notice. Refused before anything is written,
    /// in words that say why, as a file with views or triggers is: the upgrade it is part of is undone.
    /// </summary>
    /// <exception cref="InvalidOperationException">The table isn't one this engine can store notices in.</exception>
    private static void CheckNoticesTable(SqliteConnection connection, SqliteTransaction transaction, string databasePath)
    {
        Dictionary<string, (string Type, bool NotNull, bool HasDefault, bool IsKey)> columns = new(StringComparer.OrdinalIgnoreCase);
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT name, type, \"notnull\", dflt_value IS NOT NULL, pk FROM pragma_table_info('notices')";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                columns[reader.GetString(0)] = (reader.GetString(1), reader.GetBoolean(2), reader.GetBoolean(3), reader.GetInt32(4) > 0);
            }
        }

        var hasEveryColumn = CrawlSchema.NOTICE_COLUMNS.All(columns.ContainsKey);
        var keyedById = hasEveryColumn && columns["id"].IsKey && columns["id"].Type.Equals("INTEGER", StringComparison.OrdinalIgnoreCase) &&
                        columns.Values.Count(column => column.IsKey) == 1;
        var dataMayBeLeftOut = hasEveryColumn && !columns["data"].NotNull;
        var othersNeedNothing = columns
            .Where(column => !CrawlSchema.NOTICE_COLUMNS.Contains(column.Key, StringComparer.OrdinalIgnoreCase))
            .All(column => !column.Value.NotNull || column.Value.HasDefault);
        if (!(keyedById && dataMayBeLeftOut && othersNeedNothing))
        {
            throw new InvalidOperationException(
                $"'{databasePath}' can't be continued: another program changed the table it keeps its alerts in (notices), so new ones couldn't be stored there.");
        }
    }

    /// <summary>Adds the columns of a table that a file made by an earlier engine lacks: each may be null, so the rows the file has keep none.</summary>
    private static void AddMissingColumns(SqliteConnection connection, SqliteTransaction transaction, string table, (string Name, string Type)[] columns)
    {
        HashSet<string> existing = new(StringComparer.OrdinalIgnoreCase);
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = $"SELECT name FROM pragma_table_info('{table}')";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                existing.Add(reader.GetString(0));
            }
        }

        foreach (var (name, type) in columns.Where(column => !existing.Contains(column.Name)))
        {
            CrawlDatabase.Execute(connection, $"ALTER TABLE {table} ADD COLUMN {name} {type}", transaction);

            // What the file has so far didn't collect it: the analysis must not read its nulls as "none".
            if (UNCOLLECTED.TryGetValue($"{table}.{name}", out var items))
            {
                CrawlDatabase.Execute(connection, $"INSERT OR IGNORE INTO uncollected_facts (fact, item_id) SELECT '{table}.{name}', id FROM ({items})", transaction);
            }
        }
    }
}
