using System.Text.Json;
using System.Text.Json.Serialization;
using ShoutingIguana.Engine.Configuration.Models;

namespace ShoutingIguana.Sidecar.Serialization;

/// <summary>
/// The rendering mode as its camelCase name (<c>"automatic"</c>, <c>"javaScript"</c>), like every other enum. Until
/// 2.0.3 it was written as its number, which a saved <c>defaults.json</c> and an app's kept settings still hold, so a
/// number the enum defines is read too. Anything else is refused.
/// </summary>
internal sealed class JavaScriptRenderingConverter : JsonConverter<JavaScriptRendering>
{
    public override JavaScriptRendering Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number
            && reader.TryGetInt32(out var number)
            && Enum.IsDefined((JavaScriptRendering)number))
        {
            return (JavaScriptRendering)number;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var name = reader.GetString();
            foreach (var value in Enum.GetValues<JavaScriptRendering>())
            {
                if (string.Equals(NameOf(value), name, StringComparison.Ordinal))
                {
                    return value;
                }
            }
        }

        throw new JsonException("Not a JavaScript rendering mode.");
    }

    public override void Write(Utf8JsonWriter writer, JavaScriptRendering value, JsonSerializerOptions options) =>
        writer.WriteStringValue(NameOf(value));

    private static string NameOf(JavaScriptRendering value) => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
}
