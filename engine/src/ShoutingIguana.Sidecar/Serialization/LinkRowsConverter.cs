using System.Text.Json;
using System.Text.Json.Serialization;
using ShoutingIguana.Sidecar.Api.Models;

namespace ShoutingIguana.Sidecar.Serialization;

/// <summary>
/// Writes a page of links as one object per link, keyed by column id (<c>{"id":8812,"to.url":"…","kind":"hyperlink"}</c>),
/// as <see cref="UrlRowsConverter"/> writes a page of URLs.
/// </summary>
internal sealed class LinkRowsConverter : JsonConverter<LinkRowsDto>
{
    public override LinkRowsDto Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("A page's rows are only ever written.");

    public override void Write(Utf8JsonWriter writer, LinkRowsDto value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var row in value.Rows)
        {
            UrlRowsConverter.WriteRow(writer, value.Columns, row.Values);
        }

        writer.WriteEndArray();
    }
}
