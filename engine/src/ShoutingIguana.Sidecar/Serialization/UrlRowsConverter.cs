using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Sidecar.Api.Models;

namespace ShoutingIguana.Sidecar.Serialization;

/// <summary>
/// Writes a page's rows as one object per row, keyed by column id (<c>{"id":12,"url":"…","statusCode":404}</c>): the
/// columns are the query's, so no DTO can name them. Values are numbers, text, booleans or null.
/// </summary>
internal sealed class UrlRowsConverter : JsonConverter<UrlRowsDto>
{
    public override UrlRowsDto Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("A page's rows are only ever written.");

    public override void Write(Utf8JsonWriter writer, UrlRowsDto value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        for (var index = 0; index < value.Rows.Count; index++)
        {
            WriteRow(writer, value.Columns, value.Rows[index].Values, value.Changes is { } changes ? (true, changes[index]) : null);
        }

        writer.WriteEndArray();
    }

    /// <summary>One row as an object keyed by column id, for any grid whose columns are its query's.</summary>
    /// <param name="change">The row's change since another crawl, written as its <c>change</c> when the rows were compared with one.</param>
    public static void WriteRow(Utf8JsonWriter writer, IReadOnlyList<string> columns, IReadOnlyList<object?> values, (bool Compared, RowChange? Change)? change = null)
    {
        writer.WriteStartObject();
        for (var index = 0; index < columns.Count; index++)
        {
            writer.WritePropertyName(columns[index]);
            switch (values[index])
            {
                case null:
                    writer.WriteNullValue();
                    break;
                case string text:
                    writer.WriteStringValue(text);
                    break;
                case bool flag:
                    writer.WriteBooleanValue(flag);
                    break;
                case double number:
                    writer.WriteNumberValue(number);
                    break;
                case RedirectChain chain:
                    JsonSerializer.Serialize(writer, chain, SidecarJsonContext.Default.RedirectChain);
                    break;
                case RobotsRefusal refusal:
                    JsonSerializer.Serialize(writer, refusal, SidecarJsonContext.Default.RobotsRefusal);
                    break;
                case var number:
                    writer.WriteNumberValue(Convert.ToInt64(number, CultureInfo.InvariantCulture));
                    break;
            }
        }

        if (change is { Change: var rowChange })
        {
            writer.WritePropertyName("change");
            if (rowChange is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                JsonSerializer.Serialize(writer, rowChange, SidecarJsonContext.Default.RowChange);
            }
        }

        writer.WriteEndObject();
    }
}
