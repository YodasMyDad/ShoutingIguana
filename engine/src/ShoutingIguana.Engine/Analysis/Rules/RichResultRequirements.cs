using System.Text.Json;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// The properties Google requires before a schema.org entity can show as a rich result. Recommended properties are
/// left out on purpose: missing them doesn't stop the rich result, so flagging them is noise.
/// </summary>
internal static class RichResultRequirements
{
    /// <summary>Required properties per type; "a|b" means at least one of them.</summary>
    private static readonly Dictionary<string, string[]> REQUIRED = new(StringComparer.Ordinal)
    {
        ["Product"] = ["name", "offers|review|aggregateRating"],
        ["Recipe"] = ["name", "image"],
        ["Event"] = ["name", "startDate", "location"],
        ["JobPosting"] = ["title", "description", "datePosted", "hiringOrganization", "jobLocation|jobLocationType"],
        ["VideoObject"] = ["name", "thumbnailUrl", "uploadDate"],
        ["Review"] = ["itemReviewed", "reviewRating", "author"],
        ["AggregateRating"] = ["itemReviewed", "ratingValue", "ratingCount|reviewCount"],
        ["BreadcrumbList"] = ["itemListElement"],
        ["LocalBusiness"] = ["name", "address"]
    };

    /// <summary>Adds "Type: needs a, b" for every top-level entity that lacks required properties.</summary>
    public static void Check(JsonElement root, List<string> problems)
    {
        HashSet<string> referenced = new(StringComparer.Ordinal);
        CollectReferences(root, referenced);
        foreach (var entity in TopLevelEntities(root))
        {
            // A node that another node points at by @id is nested in effect (Yoast and Rank Math build graphs this
            // way), so a rating attached to a product like that doesn't need its own itemReviewed.
            var isReferenced = entity.TryGetProperty("@id", out var id) && ReadString(id) is { } idText && referenced.Contains(idText);
            foreach (var type in Types(entity))
            {
                if (!REQUIRED.TryGetValue(type, out var required))
                {
                    continue;
                }

                var absent = required
                    .Where(options => !(isReferenced && options == "itemReviewed") && !options.Split('|').Any(property => HasValue(entity, property)))
                    .ToList();
                if (absent.Count > 0)
                {
                    problems.Add($"{type}: needs {string.Join(", ", absent.Select(options => options.Replace("|", " or ", StringComparison.Ordinal)))}");
                }
            }
        }
    }

    /// <summary>The root object or array items, plus anything in a @graph. Nested values (offers, reviews) aren't entities of their own.</summary>
    private static IEnumerable<JsonElement> TopLevelEntities(JsonElement root)
    {
        var candidates = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToList() : [root];
        foreach (var candidate in candidates.Where(candidate => candidate.ValueKind == JsonValueKind.Object))
        {
            yield return candidate;
            if (candidate.TryGetProperty("@graph", out var graph) && graph.ValueKind == JsonValueKind.Array)
            {
                foreach (var node in graph.EnumerateArray().Where(node => node.ValueKind == JsonValueKind.Object))
                {
                    yield return node;
                }
            }
        }
    }

    /// <summary>@id values used as references: objects whose only property is @id.</summary>
    private static void CollectReferences(JsonElement element, HashSet<string> referenced)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var properties = element.EnumerateObject().ToList();
                if (properties is [{ Name: "@id", Value.ValueKind: JsonValueKind.String } reference])
                {
                    if (ReadString(reference.Value) is { } target)
                    {
                        referenced.Add(target);
                    }

                    break;
                }

                foreach (var property in properties)
                {
                    CollectReferences(property.Value, referenced);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectReferences(item, referenced);
                }

                break;
        }
    }

    /// <summary>@type may be a string or an array, with or without a schema.org prefix.</summary>
    private static IEnumerable<string> Types(JsonElement entity)
    {
        if (!entity.TryGetProperty("@type", out var type))
        {
            return [];
        }

        var values = type.ValueKind == JsonValueKind.Array ? type.EnumerateArray().Select(ReadString) : [ReadString(type)];
        return values.OfType<string>().Select(value => value[(value.LastIndexOfAny(['/', ':']) + 1)..]);
    }

    /// <summary>A string that can't be read is there all the same, just invalid, so it counts as a value.</summary>
    private static bool HasValue(JsonElement entity, string property) =>
        entity.TryGetProperty(property, out var value) && value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => false,
            JsonValueKind.String => ReadString(value) is not { } text || !string.IsNullOrWhiteSpace(text),
            JsonValueKind.Array => value.GetArrayLength() > 0,
            JsonValueKind.Object => value.EnumerateObject().Any(),
            _ => true
        };

    /// <summary>
    /// A string value, or null for anything else and for strings that can't be read: JSON may escape half of a
    /// surrogate pair (<c>"\uDC00"</c>, from text cut mid-emoji), which System.Text.Json won't turn into a .NET string
    /// and offers no non-throwing way to detect. Page content is untrusted, so that's an expected outcome here.
    /// </summary>
    private static string? ReadString(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        try
        {
            return value.GetString();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
