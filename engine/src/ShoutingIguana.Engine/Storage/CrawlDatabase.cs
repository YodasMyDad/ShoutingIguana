using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Serialization;
using ShoutingIguana.Engine.Urls;
using SQLitePCL;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// Opens crawl databases: one SQLite file per crawl. The writer uses WAL mode so readers (the UI, the
/// CLI) never block it. When a crawl closes, the WAL is folded back so the crawl is a single file again.
/// </summary>
public static class CrawlDatabase
{
    /// <summary>
    /// How long opening a crawl file, and anything a reader does, waits for a lock another program holds (a SQLite
    /// browser writing, a backup) before giving up: a couple of seconds, not Microsoft.Data.Sqlite's 30 s of retries.
    /// </summary>
    private const int OPEN_TIMEOUT_SECONDS = 2;

    private const int OPEN_BUSY_TIMEOUT_MS = 1000;

    /// <summary>Once a writer has its file open, its writes wait for another program's lock as long as SQLite's defaults allow.</summary>
    private const int WRITE_BUSY_TIMEOUT_MS = 5000;

    private const int WRITE_TIMEOUT_SECONDS = 30;

    /// <summary>Why a writer couldn't open a file someone is reading: the reader's lock outlasted the open timeout.</summary>
    private const string BEING_READ =
        "This crawl is being read (by an export, or another program), so it can't be written to until that finishes. Try again in a moment.";

    /// <summary>What SQLite keeps beside a database: its write-ahead log, a rollback journal, and the log's shared memory.</summary>
    private static readonly string[] COMPANIONS = ["-wal", "-journal", "-shm"];

    /// <exception cref="InvalidOperationException">The file isn't a crawl, or was written with another schema.</exception>
    public static SqliteConnection OpenReader(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Crawl database not found.", path);
        }

        var connection = Connect(path, SqliteOpenMode.ReadWrite);
        try
        {
            Execute(connection, "PRAGMA query_only = ON;");
            CheckSchema(connection, path, allowNew: false);

            // Readers run over files from anywhere, people share them, and this is where a crafted one would do its harm:
            // a view or a trigger it hides (the engine creates neither), such as a view that never ends when counted. The
            // writer paths don't need this: a crafted file is read before it is ever written to, and refused here first.
            if (Convert.ToInt64(Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type IN ('view', 'trigger')"), CultureInfo.InvariantCulture) > 0)
            {
                throw new InvalidOperationException($"'{path}' is not a crawl database: it has views or triggers, which Shouting Iguana never creates.");
            }

            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Reads the options a crawl was started with, so it can be resumed exactly. Header values and proxy credentials
    /// aren't stored (see <see cref="CrawlOptions.WithoutSecrets"/>): supply them again to resume.
    /// </summary>
    public static CrawlOptions ReadOptions(string path)
    {
        using var connection = OpenReader(path);
        var json = Scalar(connection, "SELECT options_json FROM crawl WHERE id = 1") as string
                   ?? throw new InvalidOperationException($"'{path}' is not a crawl database.");
        return StoredJson.Deserialize(json, EngineJsonContext.Default.CrawlOptions, new CrawlOptions { StartUrl = string.Empty })
               ?? throw new InvalidOperationException($"'{path}' has unreadable crawl options.");
    }

    /// <summary>
    /// Whether a crawl or an analysis is writing to the file, in this process or another. A file that says it is running
    /// while nothing holds it was interrupted, and can be resumed. Only a snapshot: the answer can change at once. Asking
    /// never makes a writer in this process give up, and a writer elsewhere waits out the instant it takes.
    /// </summary>
    /// <exception cref="IOException">The path can't be used at all, e.g. it is too long.</exception>
    public static bool IsInUse(string path) => CrawlFileLock.IsHeld(path);

    /// <summary>
    /// Deletes a crawl file and what SQLite keeps beside it, holding the crawl's lock throughout, so no crawl or analysis
    /// can start on it meanwhile. The file is renamed first, which fails while another program has it open (on Windows):
    /// then nothing has been deleted. Its log goes before it, since a log left beside a new file of the same name would be
    /// read into that file. On Linux and macOS the lock file stays, as it does after every crawl: deleting it would let
    /// two writers lock two different files.
    /// </summary>
    /// <exception cref="CrawlFileInUseException">A crawl or an analysis is writing to the file.</exception>
    /// <exception cref="IOException">Another program has the file open, or it can't be deleted.</exception>
    /// <exception cref="UnauthorizedAccessException">This user may not delete it.</exception>
    public static void Delete(string path)
    {
        using var fileLock = CrawlFileLock.Acquire(path);
        string? renamed = null;
        if (File.Exists(path))
        {
            renamed = $"{path}.{Guid.NewGuid():N}.deleting";
            File.Move(path, renamed);
        }

        try
        {
            foreach (var companion in COMPANIONS)
            {
                File.Delete(path + companion);
            }
        }
        catch when (renamed is not null)
        {
            // Nothing is lost: the file goes back beside whatever of its companions is left.
            File.Move(renamed, path);
            throw;
        }

        if (renamed is not null)
        {
            File.Delete(renamed);
        }
    }

    /// <summary>The CLI's name for a new crawl's file, <c>host-yyyyMMdd-HHmmss.sicrawl</c>, stamped with the local time now.</summary>
    public static string DefaultDatabasePath(string startUrl) => DefaultDatabasePath(startUrl, DateTime.Now);

    /// <summary>
    /// <c>host-yyyyMMdd-HHmmss.sicrawl</c> for a crawl starting at <paramref name="timestamp"/>, with the characters no
    /// file system allows in a name (an IPv6 host's colons) replaced. A relative path: hosts put it in their own folder.
    /// </summary>
    public static string DefaultDatabasePath(string startUrl, DateTime timestamp)
    {
        var host = UrlNormalizer.TryNormalize(startUrl, out var normalized) ? new Uri(normalized).Host : "crawl";
        var name = string.Concat(host.Select(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' ? character : '-')).Trim('-');
        return $"{(name.Length == 0 ? "crawl" : name)}-{timestamp.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.sicrawl";
    }

    /// <param name="allowNew">Accept an empty file, which the caller turns into a crawl.</param>
    /// <exception cref="InvalidOperationException">The file isn't a crawl, or was written with another schema.</exception>
    internal static SqliteConnection OpenWriter(string path, bool allowNew = true)
    {
        var connection = Connect(path, SqliteOpenMode.ReadWriteCreate);
        try
        {
            // Before anything writes, even the switch to WAL: a file this engine can't read must stay as it is.
            CheckSchema(connection, path, allowNew);
            try
            {
                Execute(connection, """
                    PRAGMA journal_mode = WAL;
                    PRAGMA synchronous = NORMAL;
                    PRAGMA temp_store = MEMORY;
                    PRAGMA cache_size = -32768;
                    """);
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode is raw.SQLITE_BUSY or raw.SQLITE_LOCKED)
            {
                // A closed crawl is a single file in rollback-journal mode, kept that way so it can be copied and read from
                // anywhere. While someone is part-way through reading one, it can't switch to WAL: that is said once the
                // open timeout passes, a couple of seconds, as a busy file, rather than after Microsoft.Data.Sqlite's 30 s.
                throw new CrawlFileInUseException(BEING_READ, exception);
            }

            // Open: from here on, the writes wait for another program's lock as long as they always have.
            Execute(connection, $"PRAGMA busy_timeout = {WRITE_BUSY_TIMEOUT_MS};");
            connection.DefaultTimeout = WRITE_TIMEOUT_SECONDS;
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Folds the write-ahead log back so the crawl is a single file again, then closes the connection. Leaving WAL mode
    /// needs every other connection closed. When one is still open (si stats, a SQLite browser, an export, the UI) the
    /// file stays in WAL mode, and SQLite folds the log in when that connection closes. Nothing here waits for them.
    /// </summary>
    internal static void CloseWriter(SqliteConnection connection, ILogger logger)
    {
        try
        {
            // A reader part-way through a query holds back part of the log; checkpoint the rest rather than wait.
            Execute(connection, "PRAGMA busy_timeout = 0; PRAGMA wal_checkpoint(TRUNCATE);");

            // One attempt through the native API: Microsoft.Data.Sqlite retries a busy result for its whole command
            // timeout (30 s), and SQLite's busy timeout doesn't cover the lock this needs.
            var result = raw.sqlite3_exec(connection.Handle!, "PRAGMA journal_mode = DELETE;");
            if (result == raw.SQLITE_BUSY)
            {
                logger.LogDebug("Another connection has the crawl file open, so it stays in WAL mode until that closes");
            }
            else if (result != raw.SQLITE_OK)
            {
                logger.LogWarning("Could not leave WAL mode: {Error}", raw.sqlite3_errmsg(connection.Handle!).utf8_to_string());
            }
        }
        catch (Exception exception) when (exception is SqliteException or InvalidOperationException)
        {
            // The file is closed regardless; whatever went wrong before this already has its own error.
            logger.LogWarning(exception, "Could not fold the write-ahead log back into the crawl database");
        }
        finally
        {
            connection.Dispose();
        }
    }

    /// <summary>
    /// Creates the schema in an empty database (<see cref="OpenWriter"/> has refused anything else). Returns true when
    /// the database was new.
    /// </summary>
    internal static bool EnsureSchema(SqliteConnection connection)
    {
        if (SchemaVersion(connection) == CrawlSchema.VERSION)
        {
            return false;
        }

        using var transaction = connection.BeginTransaction();
        Execute(connection, CrawlSchema.CREATE, transaction);
        Execute(connection, $"PRAGMA user_version = {CrawlSchema.VERSION};", transaction);
        transaction.Commit();
        return true;
    }

    /// <summary>The crawl's status as last stored. A crash leaves Running behind, so Running can also mean interrupted.</summary>
    internal static CrawlStatus ReadStatus(SqliteConnection connection) =>
        Scalar(connection, "SELECT status FROM crawl WHERE id = 1") is long status
            ? (CrawlStatus)status
            : throw new InvalidOperationException("This file is not a crawl database.");

    internal static void Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        command.ExecuteNonQuery();
    }

    internal static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    /// <summary>
    /// Refuses a file written with another schema, or one that isn't a crawl at all, before anything reads or writes
    /// it. An empty file passes only when the caller is about to create a crawl in it.
    /// </summary>
    private static void CheckSchema(SqliteConnection connection, string path, bool allowNew)
    {
        var version = SchemaVersion(connection);
        if (version == CrawlSchema.VERSION)
        {
            return;
        }

        if (version != 0)
        {
            var advice = version < CrawlSchema.VERSION
                ? "Crawl the site again with this version."
                : "Open it with the newer version of Shouting Iguana that wrote it.";
            throw new InvalidOperationException(
                $"'{path}' uses crawl database schema {version}; this engine reads schema {CrawlSchema.VERSION}. {advice}");
        }

        var isEmpty = Convert.ToInt64(Scalar(connection, "SELECT COUNT(*) FROM sqlite_master"), CultureInfo.InvariantCulture) == 0;
        if (!allowNew || !isEmpty)
        {
            throw new InvalidOperationException($"'{path}' is not a crawl database.");
        }
    }

    private static int SchemaVersion(SqliteConnection connection) =>
        Convert.ToInt32(Scalar(connection, "PRAGMA user_version;"), CultureInfo.InvariantCulture);

    private static SqliteConnection Connect(string path, SqliteOpenMode mode)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Pooling = false,
            DefaultTimeout = OPEN_TIMEOUT_SECONDS
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        connection.Open();
        try
        {
            // Before anything reads the file. Crawl files are shared, so a crafted one may name functions or virtual tables
            // in its schema (a view, a default, a generated column): with the schema untrusted, those can't run. And a file
            // another program has locked is refused in seconds.
            Execute(connection, $"PRAGMA trusted_schema = OFF; PRAGMA busy_timeout = {OPEN_BUSY_TIMEOUT_MS};");
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }
}
