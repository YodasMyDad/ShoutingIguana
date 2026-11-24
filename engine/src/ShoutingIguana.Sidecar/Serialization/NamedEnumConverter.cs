using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Serialization;

/// <summary>
/// An enum written as its camelCase name (<c>"javaScript"</c>, <c>"xPath"</c>) and read from that name or from its
/// number. The app sends the name. A number is still read because these two options were numbers up to 2.0.3, and
/// defaults saved then hold one. A name or a number the enum doesn't define is refused.
/// </summary>
internal sealed class NamedEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    private static readonly Dictionary<string, TEnum> VALUES = Enum.GetValues<TEnum>()
        .ToDictionary(NameOf, value => value, StringComparer.Ordinal);

    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && reader.GetString() is { } name && VALUES.TryGetValue(name, out var named))
        {
            return named;
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number) &&
            VALUES.Values.Any(value => Convert.ToInt32(value) == number))
        {
            return VALUES.Values.First(value => Convert.ToInt32(value) == number);
        }

        throw new JsonException($"Not a value of {typeof(TEnum).Name}.");
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
        writer.WriteStringValue(NameOf(value));

    private static string NameOf(TEnum value) => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
}
