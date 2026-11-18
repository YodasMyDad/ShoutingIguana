using System.Globalization;
using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Serialization;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// A Shopify store's crawl, as far as its crawler access goes: what the results say when Shopify refused pages, and which
/// pages "Recheck these pages" fetches again. The store is the one the crawl kept (<see cref="SitePlatformReader"/>), or,
/// for a crawl made before that was kept, the one the first page the crawl read says it is by its stored headers. The
/// site's URLs refused with 429 count as refused for want of a signature, unless their requests carried one
/// (<see cref="ShopifySignals.SIGNED_REFUSAL"/>). Null for a site that isn't a Shopify store, unless a signature it was
/// sent was refused.
/// </summary>
public static class ShopifyAccessReader
{
    /// <summary>The ids it gives back at most: as many as a recheck takes.</summary>
    public const int MAX_URL_IDS = RecheckPlanReader.LIMIT;

    private const string REFUSED = $"""
        FROM responses r JOIN urls u ON u.id = r.url_id
        WHERE u.is_internal = 1 AND substr(r.error, 1, $prefixLength) = $prefix
        """;

    private const string SIGNED = "substr(r.error, -$signedLength) = $signed";

    /// <exception cref="InvalidOperationException">The file isn't a crawl, or was written with another schema.</exception>
    public static ShopifyAccess? Read(string databasePath, CancellationToken cancellationToken = default) =>
        GridReadContext.Read(databasePath, cache: null, cancellationToken, context => Read(context.Connection, context.Transaction));

    internal static ShopifyAccess? Read(SqliteConnection connection, SqliteTransaction? transaction)
    {
        var platform = SitePlatformReader.Read(connection, transaction) ?? FromFirstPage(connection, transaction);
        var signed = Count(connection, transaction, $"SELECT COUNT(*) {REFUSED} AND {SIGNED}");
        if (platform is null && signed == 0)
        {
            return null;
        }

        var unsigned = platform is null ? 0 : Count(connection, transaction, $"SELECT COUNT(*) {REFUSED} AND NOT {SIGNED}");
        var refusedFilter = platform is null ? $"AND {SIGNED}" : string.Empty;
        using var ids = Command(connection, transaction, $"SELECT r.url_id {REFUSED} {refusedFilter} ORDER BY u.depth, u.id LIMIT {MAX_URL_IDS}");
        List<int> urlIds = [];
        using (var reader = ids.ExecuteReader())
        {
            while (reader.Read())
            {
                urlIds.Add(reader.GetInt32(0));
            }
        }

        return new ShopifyAccess(platform?.Host ?? StartHost(connection, transaction), platform?.Signal, unsigned, signed, urlIds);
    }

    /// <summary>For a crawl made before the fact was kept: the headers of the first of the site's pages it read.</summary>
    private static SitePlatform? FromFirstPage(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = Command(connection, transaction, """
            SELECT u.id, u.url, d.data FROM responses r JOIN urls u ON u.id = r.url_id JOIN response_details d ON d.url_id = r.url_id
            WHERE u.is_internal = 1 AND r.outcome = 0 AND r.is_html = 1 ORDER BY u.id LIMIT 1
            """);
        using var reader = command.ExecuteReader();
        if (!reader.Read() || reader.GetValue(2) is not byte[] data || !Uri.TryCreate(reader.GetString(1), UriKind.Absolute, out var url))
        {
            return null;
        }

        return ShopifySignals.FromHeaders(BlobCodec.DecodeDetails(data).Headers) is { } signal
            ? new SitePlatform(ShopifySignals.PLATFORM, WebBotSignature.AuthorityOf(url), signal, reader.GetInt32(0))
            : null;
    }

    private static string StartHost(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = Command(connection, transaction, "SELECT start_url FROM crawl WHERE id = 1");
        return command.ExecuteScalar() is string start && Uri.TryCreate(start, UriKind.Absolute, out var uri) ? WebBotSignature.AuthorityOf(uri) : string.Empty;
    }

    private static int Count(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using var command = Command(connection, transaction, sql);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$prefix", RetryingFetcher.RATE_LIMITED_ERROR);
        command.Parameters.AddWithValue("$prefixLength", RetryingFetcher.RATE_LIMITED_ERROR.Length);
        command.Parameters.AddWithValue("$signed", ShopifySignals.SIGNED_REFUSAL);
        command.Parameters.AddWithValue("$signedLength", ShopifySignals.SIGNED_REFUSAL.Length);
        return command;
    }
}
