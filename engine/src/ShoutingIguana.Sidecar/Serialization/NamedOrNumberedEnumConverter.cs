using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Serialization;

/// <summary>
/// An enum of the crawl options as its camelCase name (<c>"automatic"</c>, <c>"xPath"</c>), like every other enum.
/// Until 2.0.3 the rendering mode and an extraction rule's selector type were written as their numbers, which a saved
/// <c>defaults.json</c> and an app's kept settings still hold, so a number the enum defines is read too. Anything
/// else is refused.
/// </summary>
internal sealed class NamedOrNumberedEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
        {
            foreach (var value in Enum.GetValues<TEnum>())
            {
                if (Convert.ToInt32(value) == number)
                {
                    return value;
                }
            }
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var name = reader.GetString();
            foreach (var value in Enum.GetValues<TEnum>())
            {
                if (string.Equals(NameOf(value), name, StringComparison.Ordinal))
                {
                    return value;
                }
            }
        }

        throw new JsonException($"Not a value of {typeof(TEnum).Name}.");
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
        writer.WriteStringValue(NameOf(value));

    private static string NameOf(TEnum value) => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
}
