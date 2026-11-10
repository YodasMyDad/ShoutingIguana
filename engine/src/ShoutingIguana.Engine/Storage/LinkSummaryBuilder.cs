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
    /// Each link's place in the table: its target, kind, position and flags as stored, whether it leads to its own page,
    /// has text, and sits on a page whose robots directives say nofollow (as the inspector judges a link followed).
    /// </summary>
    private const string COUNT_LINKS = $"""
        SELECT COUNT({COUNT}(l.rowid, l.to_id, l.kind, l.position, l.flags,
                   ((l.from_id = l.to_id) << 2) | ((l.text IS NOT NULL AND l.text <> '') << 1) |
                   (l.from_id IN (SELECT url_id FROM responses WHERE is_nofollow <> 0)))),
               COALESCE(MAX(l.rowid), 0)
        FROM links l
        """;

    /// <summary>Bits of a packed key: the three yes-or-no columns, the flags, the position, the kind, then the target.</summary>
    private const int FLAG_BITS = 16;
    private const int SMALL_BITS = 8;
    private const int TARGET_SHIFT = 3 + FLAG_BITS + SMALL_BITS + SMALL_BITS;

    /// <param name="analyzedAt">The analysis's time, as its row stores it: what says the tables are that analysis's.</param>
    /// <returns>The rows written.</returns>
    public static long Build(SqliteConnection connection, SqliteTransaction transaction, long analyzedAt)
    {
        CrawlDatabase.Execute(connection, CrawlSchema.LINK_SUMMARY, transaction);
        var counts = (ByTarget: new Counts(), ByBlock: new Counts());

        // Through SQLite's own interface, as the readers' per-row functions are: a third of the wrapper's cost a call.
        // Nothing may throw back into SQLite: a failure is the statement's error.
        raw.sqlite3_create_function(connection.Handle!, COUNT, 6, raw.SQLITE_UTF8, counts, static (call, state, arguments) =>
        {
            try
            {
                var (byTarget, byBlock) = ((Counts, Counts))state;
                var (kind, position, flags, bits) = (raw.sqlite3_value_int64(arguments[2]), raw.sqlite3_value_int64(arguments[3]),
                    raw.sqlite3_value_int64(arguments[4]), raw.sqlite3_value_int64(arguments[5]));
                byTarget.Add(raw.sqlite3_value_int64(arguments[1]), kind, position, flags, bits);
                byBlock.Add(raw.sqlite3_value_int64(arguments[0]) >> CrawlSchema.LINK_BLOCK_SHIFT, kind, position, flags, bits);
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

        using var state = Command(connection, transaction, "INSERT INTO link_summary_state (id, analyzed_at, links, max_link_id) VALUES (1, $at, $links, $max)");
        state.Parameters.AddWithValue("$at", analyzedAt);
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
