using System.Globalization;
using System.Text.Json;
using ShoutingIguana.Engine.Analysis.Vocabulary;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// What structured-data-schema-errors and structured-data-missing-recommended find in one JSON-LD block or Microdata item
/// (batch 5 of docs/v2-new-checks.md): a context that isn't schema.org, a type or property schema.org doesn't have, a
/// date that isn't ISO 8601, a currency that isn't ISO 4217; and the properties Google recommends that an entity lacks.
/// Only what's written in schema.org's terms is judged: a type or property in another vocabulary (a prefix, a URL) isn't.
/// </summary>
internal static class SchemaChecks
{
    private static readonly SchemaVocabulary VOCABULARY = SchemaVocabulary.Instance;

    private static readonly string[] SCHEMA_ORG_PREFIXES = ["https://schema.org/", "http://schema.org/", "schema:"];

    /// <summary>What Google recommends of each type (its Article, Organization and product pages), and of a Product's Offer.</summary>
    private static readonly Dictionary<string, string[]> RECOMMENDED = new(StringComparer.Ordinal)
    {
        ["Article"] = ["headline", "image", "datePublished", "author"],
        ["NewsArticle"] = ["headline", "image", "datePublished", "author"],
        ["BlogPosting"] = ["headline", "image", "datePublished", "author"],
        ["Organization"] = ["url", "logo"],
        ["OnlineBusiness"] = ["url", "logo"],
        ["OnlineStore"] = ["url", "logo"],
        ["Product"] = ["image"]
    };

    private static readonly string[] OFFER_RECOMMENDED = ["priceCurrency", "availability"];

    /// <summary>ISO 8601 as structured data writes dates: a date, a reduced one, or a date and time (a space for the T, and no zone, read too).</summary>
    private static readonly string[] ISO_8601 =
    [
        "yyyy", "yyyy-MM", "yyyy-MM-dd", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF",
        "yyyy-MM-dd'T'HH:mmK", "yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"
    ];

    /// <summary>Adds the root's schema faults, in document order, to <paramref name="faults"/>.</summary>
    public static void Faults(JsonElement root, List<string> faults)
    {
        var tops = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).ToList() : [root];
        foreach (var top in tops.Where(top => top.ValueKind == JsonValueKind.Object))
        {
            // An empty block ({}, as Wix writes beside a page's real one) says nothing, so nothing in it is wrong.
            if (!top.EnumerateObject().Any())
            {
                continue;
            }

            if (!top.TryGetProperty("@context", out var context))
            {
                faults.Add("No @context");
                continue;
            }

            if (!IsSchemaOrg(context))
            {
                faults.Add($"@context {Quote(context.ValueKind == JsonValueKind.String ? context.GetString() ?? string.Empty : context.GetRawText())} isn't schema.org");
                continue;
            }

            Node(top, faults, 0);
        }
    }

    /// <summary>Adds what Google recommends that each top-level entity lacks ("Article: lacks image, datePublished").</summary>
    public static void Recommended(JsonElement root, List<string> lacking)
    {
        foreach (var entity in RichResultRequirements.TopLevelEntities(root))
        {
            foreach (var type in RichResultRequirements.Types(entity).Distinct(StringComparer.Ordinal))
            {
                if (!RECOMMENDED.TryGetValue(type, out var recommended))
                {
                    continue;
                }

                List<string> absent = [.. recommended.Where(property => !RichResultRequirements.HasValue(entity, property))];
                if (type == "Product" && entity.TryGetProperty("offers", out var offers))
                {
                    var each = offers.ValueKind == JsonValueKind.Array ? offers.EnumerateArray().ToList() : [offers];
                    absent.AddRange(each.Where(IsOffer).SelectMany(offer => OFFER_RECOMMENDED.Where(property => !OfferHas(offer, property)))
                        .Distinct(StringComparer.Ordinal).Select(property => $"offers.{property}"));
                }

                if (absent.Count > 0)
                {
                    lacking.Add($"{type}: lacks {string.Join(", ", absent)}");
                }
            }
        }
    }

    /// <summary>A type's local name in schema.org's terms; null for one in another vocabulary.</summary>
    internal static string? SchemaOrgName(string written)
    {
        foreach (var prefix in SCHEMA_ORG_PREFIXES)
        {
            if (written.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return written[prefix.Length..];
            }
        }

        return written.Contains(':', StringComparison.Ordinal) || written.Contains('/', StringComparison.Ordinal) ? null : written;
    }

    /// <summary>A date as ISO 8601 writes it.</summary>
    internal static bool IsIso8601(string value) =>
        DateTimeOffset.TryParseExact(value.Trim(), ISO_8601, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _);

    private static void Node(JsonElement node, List<string> faults, int depth)
    {
        if (depth > 64)
        {
            return;
        }

        var written = node.TryGetProperty("@type", out var type)
            ? (type.ValueKind == JsonValueKind.Array ? type.EnumerateArray() : (IEnumerable<JsonElement>)[type])
                .Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()!).ToList()
            : [];
        var names = written.Select(SchemaOrgName).ToList();

        // Typed in schema.org's terms only (no other vocabulary's type beside them): its types and properties can be judged.
        var judged = names.Count > 0 && names.All(name => name is not null);
        List<string> known = [];
        if (judged)
        {
            foreach (var name in names.OfType<string>())
            {
                if (VOCABULARY.IsType(name))
                {
                    known.Add(name);
                }
                else
                {
                    faults.Add($"{name} isn't a schema.org type");
                }
            }
        }

        var label = known.Count > 0 ? known[0] : null;
        foreach (var property in node.EnumerateObject())
        {
            // Keywords, another vocabulary's terms, and Actions' -input and -output annotations (query-input) aren't properties to judge.
            var name = property.Name.StartsWith('@') || property.Name.EndsWith("-input", StringComparison.Ordinal) || property.Name.EndsWith("-output", StringComparison.Ordinal)
                ? null
                : SchemaOrgName(property.Name);
            if (name is not null && label is not null && known.Count == names.Count)
            {
                if (!VOCABULARY.IsProperty(name))
                {
                    faults.Add($"{label}: {Quote(name)} isn't a schema.org property");
                }
                else if (!VOCABULARY.IsPropertyOf(known, name))
                {
                    faults.Add($"{label}: {Quote(name)} isn't a property of {label}");
                }
                else
                {
                    Values(label, name, property.Value, faults);
                }
            }

            foreach (var child in Children(property.Value))
            {
                Node(child, faults, depth + 1);
            }
        }
    }

    /// <summary>A date property's values must be ISO 8601 dates, and a priceCurrency's ISO 4217 codes.</summary>
    private static void Values(string label, string property, JsonElement value, List<string> faults)
    {
        var strings = (value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : (IEnumerable<JsonElement>)[value])
            .Select(item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("@value", out var literal) ? literal : item)
            .Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).Where(text => text.Trim().Length > 0);
        foreach (var text in strings)
        {
            if (VOCABULARY.TakesOnlyDates(property) && !IsIso8601(text))
            {
                faults.Add($"{label}: {property} {Quote(text)} isn't an ISO 8601 date");
            }
            else if (property == "priceCurrency" && !VOCABULARY.IsCurrency(text.Trim()))
            {
                faults.Add($"{label}: priceCurrency {Quote(text)} isn't ISO 4217");
            }
        }
    }

    private static IEnumerable<JsonElement> Children(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => [value],
        JsonValueKind.Array => value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object),
        _ => []
    };

    /// <summary>
    /// schema.org's context: its URL (either scheme, with or without the slash, and with www, which redirects there and
    /// validators read so), alone, in a list, or as an object's @vocab.
    /// </summary>
    private static bool IsSchemaOrg(JsonElement context) => context.ValueKind switch
    {
        JsonValueKind.String => IsSchemaOrgUrl(context.GetString()),
        JsonValueKind.Array => context.EnumerateArray().Any(IsSchemaOrg),
        JsonValueKind.Object => context.TryGetProperty("@vocab", out var vocabulary) && IsSchemaOrgUrl(vocabulary.GetString()),
        _ => false
    };

    private static bool IsSchemaOrgUrl(string? url) =>
        url?.Trim().TrimEnd('/').ToLowerInvariant() is "https://schema.org" or "http://schema.org" or "https://www.schema.org" or "http://www.schema.org";

    /// <summary>
    /// An offer's recommended property, as Google reads it: the currency may be the offer's own or its priceSpecification's
    /// (one, or the first of a list that has it), which is where shops that give a unit or sale price write it.
    /// </summary>
    private static bool OfferHas(JsonElement offer, string property) =>
        RichResultRequirements.HasValue(offer, property) ||
        (property == "priceCurrency" && offer.TryGetProperty("priceSpecification", out var specification) &&
         Children(specification).Any(each => RichResultRequirements.HasValue(each, property)));

    private static bool IsOffer(JsonElement offer) => offer.ValueKind == JsonValueKind.Object &&
                                                      (!offer.TryGetProperty("@type", out _) || RichResultRequirements.Types(offer).Contains("Offer"));

    private static string Quote(string text) => RuleText.Quote(text, 40);
}
