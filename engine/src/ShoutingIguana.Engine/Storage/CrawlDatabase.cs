using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Serialization;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// Opens crawl databases: one SQLite file per crawl. The writer uses WAL mode so readers (the UI, the
/// CLI) never block it. When a crawl closes, the WAL is folded back so the crawl is a single file again.
/// </summary>
public static class CrawlDatabase
{
    public static SqliteConnection OpenReader(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Crawl database not found.", path);
        }

        var connection = Connect(path, SqliteOpenMode.ReadWrite);
        Execute(connection, "PRAGMA busy_timeout = 5000; PRAGMA query_only = ON;");
        return connection;
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

    internal static SqliteConnection OpenWriter(string path)
    {
        var connection = Connect(path, SqliteOpenMode.ReadWriteCreate);
        Execute(connection, """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA temp_store = MEMORY;
            PRAGMA cache_size = -32768;
            PRAGMA busy_timeout = 5000;
            """);
        return connection;
    }

    /// <summary>
    /// Folds the write-ahead log back so the crawl is a single file again, then closes the connection. Folding
    /// needs every other connection closed; when a reader is still open the file simply stays in WAL mode.
    /// </summary>
    internal static void CloseWriter(SqliteConnection connection, ILogger logger)
    {
        try
        {
            Execute(connection, "PRAGMA wal_checkpoint(TRUNCATE); PRAGMA journal_mode = DELETE;");
        }
        catch (SqliteException exception)
        {
            logger.LogWarning(exception, "Could not fold the write-ahead log back into the crawl database");
        }

        connection.Dispose();
    }

    /// <summary>Creates the schema in an empty database. Returns true when the database was new.</summary>
    internal static bool EnsureSchema(SqliteConnection connection)
    {
        var version = Convert.ToInt32(Scalar(connection, "PRAGMA user_version;"), CultureInfo.InvariantCulture);
        if (version == CrawlSchema.VERSION)
        {
            return false;
        }

        if (version != 0)
        {
            throw new InvalidOperationException(
                $"This crawl uses database schema {version}; this engine reads schema {CrawlSchema.VERSION}.");
        }

        using var transaction = connection.BeginTransaction();
        Execute(connection, CrawlSchema.CREATE, transaction);
        Execute(connection, $"PRAGMA user_version = {CrawlSchema.VERSION};", transaction);
        transaction.Commit();
        return true;
    }

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
