using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Serialization;
using SQLitePCL;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// Opens crawl databases: one SQLite file per crawl. The writer uses WAL mode so readers (the UI, the
/// CLI) never block it. When a crawl closes, the WAL is folded back so the crawl is a single file again.
/// </summary>
public static class CrawlDatabase
{
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
            Execute(connection, "PRAGMA busy_timeout = 5000; PRAGMA query_only = ON;");
            CheckSchema(connection, path, allowNew: false);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>Reads the options a crawl was started with, so it can be resumed exactly.</summary>
    public static CrawlOptions ReadOptions(string path)
    {
        using var connection = OpenReader(path);
        var json = Scalar(connection, "SELECT options_json FROM crawl WHERE id = 1") as string
                   ?? throw new InvalidOperationException($"'{path}' is not a crawl database.");
        return JsonSerializer.Deserialize(json, EngineJsonContext.Default.CrawlOptions)
               ?? throw new InvalidOperationException($"'{path}' has unreadable crawl options.");
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
            Execute(connection, """
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;
                PRAGMA temp_store = MEMORY;
                PRAGMA cache_size = -32768;
                PRAGMA busy_timeout = 5000;
                """);
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
            Pooling = false
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }
}
