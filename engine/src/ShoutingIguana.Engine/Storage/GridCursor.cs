using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// The cursor a page gives for reading on after its last row: that row's URL id, a hash of its sort key values, and the
/// hash of the query it belongs to, as compact JSON in base64url, under 100 characters whatever the row holds (a URL of
/// any length, a long detail). Reading on reads the row's sort values again, in the new read's snapshot; a row that has
/// gone, or whose values have changed since, is refused rather than read on from a place that has moved. Opaque to the
/// UI, which only passes it back.
/// </summary>
internal static class GridCursor
{
    /// <summary>Longer than any cursor this gives.</summary>
    private const int MAX_LENGTH = 512;

    public static string Encode(string hash, IReadOnlyList<object?> values, long id)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("h", hash);
            json.WriteNumber("i", id);
            json.WriteString("k", KeysHash(values));
            json.WriteEndObject();
        }

        return Base64Url.EncodeToString(buffer.WrittenSpan);
    }

    /// <param name="keysOf">The sort key values of the row with an id, as the page read them; null when there's no such row.</param>
    /// <exception cref="UrlQueryException">It isn't a cursor, one from another query, or its row has gone or changed.</exception>
    public static GridCursorPosition Decode(string cursor, string hash, Func<long, IReadOnlyList<object?>?> keysOf, string field = "after")
    {
        string? storedHash;
        string? storedKeys;
        long id;
        try
        {
            if (cursor.Length > MAX_LENGTH)
            {
                throw new FormatException("The cursor is too long.");
            }

            using var document = JsonDocument.Parse(Base64Url.DecodeFromChars(cursor));
            var root = document.RootElement;
            storedHash = root.GetProperty("h").GetString();
            id = root.GetProperty("i").GetInt64();
            storedKeys = root.GetProperty("k").GetString();
        }
        catch (Exception exception) when (exception is FormatException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw UrlQueryException.For(field, "This isn't a cursor a page of this grid gave.");
        }

        if (storedHash != hash)
        {
            throw UrlQueryException.For(field, "This cursor belongs to another query: start from an offset again.");
        }

        var values = keysOf(id) ?? throw UrlQueryException.For(field, "The row this cursor stopped at isn't in the crawl any more: start from an offset again.");
        if (KeysHash(values) != storedKeys)
        {
            throw UrlQueryException.For(field, "The row this cursor stopped at has changed since: start from an offset again.");
        }

        return new GridCursorPosition(values, id);
    }

    /// <summary>The values, each with its type, hashed: two lists hash alike only when their values are the same.</summary>
    private static string KeysHash(IReadOnlyList<object?> values)
    {
        var text = new StringBuilder();
        foreach (var value in values)
        {
            text.Append(value switch
            {
                null => "n;",
                string words => $"s{words.Length.ToString(CultureInfo.InvariantCulture)}:{words};",
                _ => $"i{Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)};"
            });
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16];
    }
}
