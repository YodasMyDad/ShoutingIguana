using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// Builds <c>link_summary</c> and <c>link_blocks</c> inside the analysis's transaction: one pass over the links, each
/// counted under its target and under its block of ids, by what it is, in a function SQLite calls per row, then the counts
/// stored in key order. Grouping them in SQL sorts every link (18 s for the 100k benchmark's 5 million); counted in hash
/// tables, they take a pass (about 1 s), and the tables are about 2 entries a URL, as the analysis's own are a few dozen
/// bytes a URL.
/// </summary>
internal static class LinkSummaryBuilder
{
    private const string COUNT = "si_count_link";

    /// <summary>
    /// Each link as it's counted: its id, both ends, its kind, position and flags as stored, and whether it has text. Whether
    /// it leads to its own page, and sits on a page whose robots directives say nofollow (as the inspector judges a link
    /// followed), the count works out from its ends.
    /// </summary>
    private const string COUNT_LINKS = $"""
        SELECT COUNT({COUNT}(l.rowid, l.to_id, l.from_id, l.kind, l.position, l.flags, l.text IS NOT NULL AND l.text <> '')), COALESCE(MAX(l.rowid), 0)
        FROM links l
        """;

    /// <summary>Bits of a packed key: the three yes-or-no columns, the flags, the position, the kind, then the target.</summary>
    private const int FLAG_BITS = 16;
    private const int SMALL_BITS = 8;
    private const int TARGET_SHIFT = 3 + FLAG_BITS + SMALL_BITS + SMALL_BITS;

    /// <summary>
    /// Builds the link summary unless the file has one of the crawl as it stands: the links don't change between a crawl's
    /// runs, so analysing a crawl again (new thresholds, a newer engine's checks) leaves its link summary as it is.
    /// </summary>
    /// <returns>The rows written, or null when the summary stood.</returns>
    public static long? BuildIfChanged(SqliteConnection connection, SqliteTransaction transaction) =>
        IsCurrent(connection, transaction) ? null : Build(connection, transaction);

    /// <summary>Whether the file's link summary is this engine's, and counts the crawl as it stands.</summary>
    internal static bool IsCurrent(SqliteConnection connection, SqliteTransaction? transaction) => ReadCurrent(connection, transaction) is not null;

    /// <summary>
    /// The link summary's own counts (the links, and their highest id) when it counts the crawl as it stands: its tables
    /// are this engine's, column for column, and carry the crawl row's stamp. Null otherwise: no summary, another shape's
    /// (a later engine's, another program's), or one of the crawl before its last run.
    /// </summary>
    internal static (long Links, long MaxLinkId)? ReadCurrent(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        foreach (var (table, columns) in CrawlSchema.LINK_SUMMARY_TABLES)
        {
            // Asked of each table before any is read: a statement naming a column a table lacks wouldn't compile.
            command.CommandText = "SELECT group_concat(name, ',') FROM (SELECT name FROM pragma_table_info($table) ORDER BY cid)";
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$table", table);
            if (command.ExecuteScalar() as string != columns)
            {
                return null;
            }
        }

        command.CommandText = """
            SELECT s.links, s.max_link_id FROM link_summary_state s JOIN crawl c ON c.id = 1 AND c.updated_at = s.crawl_updated_at WHERE s.id = 1
            """;
        command.Parameters.Clear();
        using var reader = command.ExecuteReader();
        return reader.Read() && !reader.IsDBNull(0) && !reader.IsDBNull(1) ? (reader.GetInt64(0), reader.GetInt64(1)) : null;
    }

    /// <returns>The rows written.</returns>
    public static long Build(SqliteConnection connection, SqliteTransaction transaction)
    {
        CrawlDatabase.Execute(connection, CrawlSchema.LINK_SUMMARY, transaction);
        var counts = new Counting(NofollowPages(connection, transaction));

        // Through SQLite's own interface, as the readers' per-row functions are: a third of the wrapper's cost a call.
        // Nothing may throw back into SQLite: a failure is the statement's error.
        raw.sqlite3_create_function(connection.Handle!, COUNT, 7, raw.SQLITE_UTF8, counts, static (call, state, arguments) =>
        {
            try
            {
                ((Counting)state).Add(raw.sqlite3_value_int64(arguments[0]), raw.sqlite3_value_int64(arguments[1]), raw.sqlite3_value_int64(arguments[2]),
                    raw.sqlite3_value_int64(arguments[3]), raw.sqlite3_value_int64(arguments[4]), raw.sqlite3_value_int64(arguments[5]),
                    raw.sqlite3_value_int64(arguments[6]) != 0);
                raw.sqlite3_result_int(call, 1);
            }
            catch (Exception exception)
            {
                raw.sqlite3_result_error(call, exception.Message);
            }
        });

        long links, maxLinkId;
        using (var count = Command(connection, transaction, COUNT_LINKS))
        using (var reader = count.ExecuteReader())
        {
            reader.Read();
            (links, maxLinkId) = (reader.GetInt64(0), reader.GetInt64(1));
        }

        var written = Store(connection, transaction, "link_summary", "to_id", counts.ByTarget) + Store(connection, transaction, "link_blocks", "block", counts.ByBlock);

        // The crawl row's stamp as it stands: the analysis holds the crawl's lock, so no run can move it before this commits.
        using var state = Command(connection, transaction,
            "INSERT INTO link_summary_state (id, crawl_updated_at, links, max_link_id) VALUES (1, (SELECT updated_at FROM crawl WHERE id = 1), $links, $max)");
        state.Parameters.AddWithValue("$links", links);
        state.Parameters.AddWithValue("$max", maxLinkId);
        state.ExecuteNonQuery();
        return written;
    }

    /// <summary>The counts, stored in the table's own order: by target, or by block.</summary>
    private static long Store(SqliteConnection connection, SqliteTransaction transaction, string table, string first, Counts counts)
    {
        using var insert = Command(connection, transaction, $"""
            INSERT INTO {table} ({first}, kind, position, flags, self, has_text, source_nofollow, links)
            VALUES ($first, $kind, $position, $flags, $self, $hasText, $sourceNofollow, $links)
            """);
        var parameters = new[] { "$first", "$kind", "$position", "$flags", "$self", "$hasText", "$sourceNofollow", "$links" }
            .Select(name => insert.Parameters.Add(name, SqliteType.Integer)).ToArray();
        insert.Prepare();
        long written = 0;
        foreach (var (key, count) in counts.InKeyOrder())
        {
            long[] values = [key.First, key.Kind, key.Position, key.Flags, (key.Bits >> 2) & 1, (key.Bits >> 1) & 1, key.Bits & 1, count];
            for (var index = 0; index < values.Length; index++)
            {
                parameters[index].Value = values[index];
            }

            insert.ExecuteNonQuery();
            written++;
        }

        return written;
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }

    /// <summary>The pages whose robots directives say nofollow: few, on most sites none.</summary>
    private static HashSet<long> NofollowPages(SqliteConnection connection, SqliteTransaction transaction)
    {
        HashSet<long> pages = [];
        using var command = Command(connection, transaction, "SELECT url_id FROM responses WHERE is_nofollow <> 0");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            pages.Add(reader.GetInt64(0));
        }

        return pages;
    }

    /// <summary>
    /// The pass's counts, by target and by block. A page's links are stored together, so whether the page says nofollow is
    /// looked up once a page, not once a link.
    /// </summary>
    private sealed class Counting(HashSet<long> nofollowPages)
    {
        private long _page = long.MinValue;
        private bool _pageNofollow;

        public Counts ByTarget { get; } = new();

        public Counts ByBlock { get; } = new();

        public void Add(long id, long to, long from, long kind, long position, long flags, bool hasText)
        {
            if (from != _page)
            {
                (_page, _pageNofollow) = (from, nofollowPages.Count > 0 && nofollowPages.Contains(from));
            }

            var bits = (from == to ? 4L : 0) | (hasText ? 2L : 0) | (_pageNofollow ? 1L : 0);
            ByTarget.Add(to, kind, position, flags, bits);
            ByBlock.Add(id >> CrawlSchema.LINK_BLOCK_SHIFT, kind, position, flags, bits);
        }
    }

    /// <param name="First">The target's URL id, or the block of link ids.</param>
    private readonly record struct Key(long First, long Kind, long Position, long Flags, long Bits);

    /// <summary>
    /// The links counted by key. A key the crawler writes packs into one number, in the table's own order; one it never
    /// would (a crafted file's kind of a million) is kept whole, apart, so no file can make the packing lie.
    /// </summary>
    private sealed class Counts
    {
        private readonly Dictionary<long, long> _packed = [];
        private readonly Dictionary<Key, long> _whole = [];

        public void Add(long first, long kind, long position, long flags, long bits)
        {
            if ((ulong)first < 1UL << (63 - TARGET_SHIFT) && (ulong)kind < 1UL << SMALL_BITS && (ulong)position < 1UL << SMALL_BITS && (ulong)flags < 1UL << FLAG_BITS)
            {
                var packed = (first << TARGET_SHIFT) | (kind << (3 + FLAG_BITS + SMALL_BITS)) | (position << (3 + FLAG_BITS)) | (flags << 3) | (bits & 7);
                CollectionsMarshal.GetValueRefOrAddDefault(_packed, packed, out _)++;
            }
            else
            {
                CollectionsMarshal.GetValueRefOrAddDefault(_whole, new Key(first, kind, position, flags, bits & 7), out _)++;
            }
        }

        public IEnumerable<(Key Key, long Count)> InKeyOrder()
        {
            var packed = _packed.Keys.ToArray();
            Array.Sort(packed);
            foreach (var key in packed)
            {
                yield return (new Key(key >> TARGET_SHIFT, (key >> (3 + FLAG_BITS + SMALL_BITS)) & ((1 << SMALL_BITS) - 1), (key >> (3 + FLAG_BITS)) & ((1 << SMALL_BITS) - 1),
                    (key >> 3) & ((1 << FLAG_BITS) - 1), key & 7), _packed[key]);
            }

            foreach (var (key, count) in _whole)
            {
                yield return (key, count);
            }
        }
    }
}
