using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Favicons;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// The site's icon, as a crawl kept it. Safe while the crawl runs: a crawl has none until it has fetched one, a few
/// requests after its start page. A crawl made before icons were kept has none, and so has one whose site has no icon
/// the engine keeps.
/// <para>
/// A crawl file can come from anywhere, so what it holds as an icon is trusted no more than what a site sent: the bytes
/// are read only if they're no more than an icon may be, and are told to be an image again here, by their own
/// signature. Whatever the file calls them, nothing but an image the engine keeps is ever given out.
/// </para>
/// </summary>
public static class FaviconReader
{
    /// <summary>The crawl's icon, or null when it has none.</summary>
    /// <exception cref="InvalidOperationException">The file isn't a crawl, or was written with another schema.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static CrawlFavicon? Read(string databasePath, CancellationToken cancellationToken = default) =>
        GridReadContext.Read(databasePath, cache: null, cancellationToken, context =>
        {
            if (!HasTable(context.Connection, context.Transaction))
            {
                return null;
            }

            using var command = context.Command($"SELECT url, fetched_at, body FROM favicon WHERE {KEPT}", []);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            var bytes = reader.GetFieldValue<byte[]>(2);
            return FaviconSniffer.Sniff(bytes) is { } image
                ? new CrawlFavicon(reader.GetValue(0) as string ?? string.Empty, image.ContentType, image.Width, image.Height, bytes, FetchedAt(reader.GetValue(1)))
                : null;
        });

    /// <summary>
    /// Whether the file holds an icon, without reading it: two lookups, for a list of crawls. What it holds is still told
    /// to be an image only as it's read, so a crafted file can say yes here and have none to give.
    /// </summary>
    internal static bool Has(SqliteConnection connection, SqliteTransaction? transaction)
    {
        if (!HasTable(connection, transaction))
        {
            return false;
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT 1 FROM favicon WHERE {KEPT}";
        return command.ExecuteScalar() is not null;
    }

    /// <summary>The one row, when it holds bytes, and no more of them than an icon may be: a larger blob is never read.</summary>
    private static readonly string KEPT = $"id = 1 AND typeof(body) = 'blob' AND length(body) BETWEEN 1 AND {FaviconSniffer.MAX_BYTES}";

    /// <summary>The table, with the columns this engine reads: a file from before icons were kept has none, and one another program made its own way is read as none.</summary>
    private static bool HasTable(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('favicon') WHERE name IN ('id', 'url', 'fetched_at', 'body')";
        return command.ExecuteScalar() is 4L;
    }

    /// <summary>When it was fetched, as the file has it; anything that isn't a time a date can hold reads as the start of 1970.</summary>
    private static DateTimeOffset FetchedAt(object value)
    {
        try
        {
            return value is long milliseconds ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds) : DateTimeOffset.UnixEpoch;
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTimeOffset.UnixEpoch;
        }
    }
}
