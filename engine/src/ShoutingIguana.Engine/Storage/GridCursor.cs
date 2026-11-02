using System.Buffers;
using System.Buffers.Text;
using System.Text.Json;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// The cursor a page gives for reading on after its last row: that row's sort key values and URL id, with the hash of the
/// query it belongs to, as compact JSON in base64url. Opaque to the UI, which only passes it back.
/// </summary>
internal static class GridCursor
{
    private const int MAX_LENGTH = 8_192;

    public static string Encode(string hash, IReadOnlyList<object?> values, long id)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("h", hash);
            json.WriteStartArray("v");
            foreach (var value in values)
            {
                switch (value)
                {
                    case null:
                        json.WriteNullValue();
                        break;
                    case string text:
                        json.WriteStringValue(text);
                        break;
                    default:
                        json.WriteNumberValue(Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture));
                        break;
                }
            }

            json.WriteEndArray();
            json.WriteNumber("i", id);
            json.WriteEndObject();
        }

        return Base64Url.EncodeToString(buffer.WrittenSpan);
    }

    /// <param name="textKeys">For each sort key, whether its values are text; numbers otherwise.</param>
    /// <exception cref="UrlQueryException">It isn't a cursor, or one from another query.</exception>
    public static GridCursorPosition Decode(string cursor, string hash, IReadOnlyList<bool> textKeys, string field = "after")
    {
        try
        {
            if (cursor.Length > MAX_LENGTH)
            {
                throw new FormatException("The cursor is too long.");
            }

            using var document = JsonDocument.Parse(Base64Url.DecodeFromChars(cursor));
            var root = document.RootElement;
            if (root.GetProperty("h").GetString() != hash)
            {
                throw UrlQueryException.For(field, "This cursor belongs to another query: start from an offset again.");
            }

            var stored = root.GetProperty("v");
            if (stored.GetArrayLength() != textKeys.Count)
            {
                throw new FormatException("The cursor has the wrong number of values.");
            }

            List<object?> values = [];
            var index = 0;
            foreach (var value in stored.EnumerateArray())
            {
                values.Add(value.ValueKind switch
                {
                    JsonValueKind.Null => null,
                    JsonValueKind.String when textKeys[index] => value.GetString(),
                    JsonValueKind.Number when !textKeys[index] => value.GetInt64(),
                    _ => throw new FormatException("A cursor value has the wrong type.")
                });
                index++;
            }

            return new GridCursorPosition(values, root.GetProperty("i").GetInt64());
        }
        catch (Exception exception) when (exception is FormatException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw UrlQueryException.For(field, "This isn't a cursor a page of this grid gave.");
        }
    }
}
