using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace ShoutingIguana.Engine.Serialization;

/// <summary>
/// Reads JSON the engine stored earlier, which may lack properties the type has gained since: one the JSON doesn't have
/// takes the type's own default. The source-generated reader would set it to 0, false or null instead, so a new option
/// with any other default would change how a resumed crawl runs, or what an analysis checks. A required property that
/// is missing is still an error.
/// </summary>
internal static class StoredJson
{
    /// <param name="defaults">The type with every property at its default.</param>
    /// <exception cref="JsonException">The JSON isn't one of these.</exception>
    public static T? Deserialize<T>(string json, JsonTypeInfo<T> typeInfo, T defaults)
    {
        if (JsonNode.Parse(json) is not JsonObject stored)
        {
            return JsonSerializer.Deserialize(json, typeInfo);
        }

        // Names as this type's metadata matches them, so a property the JSON has is never given a second value.
        var comparer = typeInfo.Options.PropertyNameCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        HashSet<string> present = new(stored.Select(property => property.Key), comparer);
        HashSet<string> required = new(typeInfo.Properties.Where(property => property.IsRequired).Select(property => property.Name), comparer);
        foreach (var (name, value) in JsonSerializer.SerializeToNode(defaults, typeInfo)!.AsObject())
        {
            if (!present.Contains(name) && !required.Contains(name))
            {
                stored[name] = value?.DeepClone();
            }
        }

        return stored.Deserialize(typeInfo);
    }
}
