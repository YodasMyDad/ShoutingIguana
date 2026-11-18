using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// What the site runs on, as the crawl kept it (<see cref="CrawlSchema.SITE_PLATFORM"/>): null for a site no response
/// said anything of, for a crawl made before the fact was kept, and for a table of another program's.
/// </summary>
public static class SitePlatformReader
{
    /// <exception cref="InvalidOperationException">The file isn't a crawl, or was written with another schema.</exception>
    public static SitePlatform? Read(string databasePath, CancellationToken cancellationToken = default) =>
        GridReadContext.Read(databasePath, cache: null, cancellationToken, context => Read(context.Connection, context.Transaction));

    internal static SitePlatform? Read(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var columns = connection.CreateCommand();
        columns.Transaction = transaction;
        columns.CommandText = "SELECT group_concat(name, ',') FROM (SELECT name FROM pragma_table_info('site_platform') ORDER BY cid)";
        if (columns.ExecuteScalar() as string != CrawlSchema.SITE_PLATFORM_COLUMNS)
        {
            return null;
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT name, host, signal, url_id FROM site_platform WHERE id = 1";
        using var reader = command.ExecuteReader();
        return reader.Read() && reader.GetValue(0) is string name && reader.GetValue(1) is string host && reader.GetValue(2) is string signal
            ? new SitePlatform(name, host, signal, reader.IsDBNull(3) ? null : reader.GetInt32(3))
            : null;
    }
}
