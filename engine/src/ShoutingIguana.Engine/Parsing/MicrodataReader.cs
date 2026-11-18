using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Parsing;

/// <summary>
/// A page's Microdata as the structured data checks read JSON-LD (change C5 of docs/v2-new-checks.md): each top-level item
/// (an <c>itemscope</c> that isn't some item's property) as a JSON-LD-shaped object, its schema.org types by their local
/// names and its properties' values as the HTML standard reads them, with a <c>content</c> attribute the value on any
/// element, as schema.org writes it (<see cref="ValueOf"/>). An item without an <c>itemtype</c> has no vocabulary
/// to judge it by, so only typed top-level items are kept.
/// </summary>
internal static class MicrodataReader
{
    private const int MAX_ITEMS = 50;
    private const int MAX_DEPTH = 32;
    private const string SCHEMA_ORG = "https://schema.org";

    public static IReadOnlyList<string> Read(IDocument document, Uri baseUri)
    {
        List<string> items = [];
        foreach (var item in document.QuerySelectorAll("[itemscope]").Where(element => !element.HasAttribute("itemprop")))
        {
            var types = Types(item);
            if (types.Count == 0)
            {
                continue;
            }

            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                var vocabulary = types.All(type => SchemaOrgLocal(type) is not null) ? SCHEMA_ORG : VocabularyOf(types[0]);
                WriteItem(writer, item, baseUri, vocabulary, 0);
            }

            var json = Encoding.UTF8.GetString(buffer.ToArray());
            if (json.Length < PageWalker.MAX_JSON_LD_CHARS)
            {
                items.Add(json);
            }

            if (items.Count == MAX_ITEMS)
            {
                break;
            }
        }

        return items;
    }

    /// <summary>An item: its context (top-level only), its type, then each property in document order.</summary>
    private static void WriteItem(Utf8JsonWriter writer, IElement item, Uri baseUri, string? context, int depth)
    {
        writer.WriteStartObject();
        if (context is not null)
        {
            writer.WriteString("@context", context);
        }

        var types = Types(item);
        if (types.Count == 1)
        {
            writer.WriteString("@type", SchemaOrgLocal(types[0]) ?? types[0]);
        }
        else if (types.Count > 1)
        {
            writer.WriteStartArray("@type");
            foreach (var type in types)
            {
                writer.WriteStringValue(SchemaOrgLocal(type) ?? type);
            }

            writer.WriteEndArray();
        }

        // Each name's values, in order: a name given twice is a list.
        var properties = new List<(string Name, List<IElement> Values)>();
        Collect(item, properties);
        foreach (var (name, values) in properties)
        {
            writer.WritePropertyName(name);
            if (values.Count > 1)
            {
                writer.WriteStartArray();
            }

            foreach (var value in values)
            {
                if (value.HasAttribute("itemscope") && depth < MAX_DEPTH)
                {
                    WriteItem(writer, value, baseUri, null, depth + 1);
                }
                else
                {
                    writer.WriteStringValue(ValueOf(value, baseUri));
                }
            }

            if (values.Count > 1)
            {
                writer.WriteEndArray();
            }
        }

        writer.WriteEndObject();
    }

    /// <summary>The item's properties: the elements under it with an itemprop, not those inside a nested item or another top-level one.</summary>
    private static void Collect(IElement scope, List<(string Name, List<IElement> Values)> properties)
    {
        foreach (var child in scope.Children)
        {
            if (child.GetAttribute("itemprop") is { } names)
            {
                foreach (var name in names.Split((char[])[' ', '\t', '\n', '\r', '\f'], StringSplitOptions.RemoveEmptyEntries).Select(PropertyName).OfType<string>())
                {
                    var index = properties.FindIndex(property => property.Name == name);
                    if (index < 0)
                    {
                        properties.Add((name, [child]));
                    }
                    else
                    {
                        properties[index].Values.Add(child);
                    }
                }
            }

            // A nested item's own properties are its own; a top-level item inside this one is another item.
            if (!child.HasAttribute("itemscope"))
            {
                Collect(child, properties);
            }
        }
    }

    /// <summary>
    /// A property's value (one that isn't an item): its <c>content</c> attribute on any element, else as the HTML standard
    /// reads it, an attribute for a media element, link, object, data, meter or time, else the text. The standard names
    /// <c>content</c> for meta alone, but schema.org's examples put it on a span ("<c>&lt;span itemprop="priceCurrency"
    /// content="USD"&gt;$&lt;/span&gt;</c>") and Google reads it there. It wins over the element's own attribute too: on any
    /// element but meta it means nothing but the value, while an href, src, datetime or value has a job of its own on the
    /// page, and RDFa, where the attribute comes from, puts <c>@content</c> before <c>@href</c>, <c>@src</c> and
    /// <c>@datetime</c> alike.
    /// </summary>
    private static string ValueOf(IElement element, Uri baseUri)
    {
        string? Url(string attribute) => element.GetAttribute(attribute) is { } value && UrlNormalizer.TryResolve(baseUri, value, out var resolved)
            ? resolved.AbsoluteUri
            : element.GetAttribute(attribute) ?? string.Empty;

        if (element.GetAttribute("content") is { } content)
        {
            return content;
        }

        return element.LocalName switch
        {
            "meta" => string.Empty,
            "audio" or "embed" or "iframe" or "img" or "source" or "track" or "video" => Url("src") ?? string.Empty,
            "a" or "area" or "link" => Url("href") ?? string.Empty,
            "object" => Url("data") ?? string.Empty,
            "data" or "meter" => element.GetAttribute("value") ?? string.Empty,
            "time" => element.GetAttribute("datetime") ?? Clean(element.TextContent),
            _ => Clean(element.TextContent)
        };
    }

    private static string Clean(string text) => string.Join(' ', text.Split((char[])[' ', '\t', '\n', '\r', '\f'], StringSplitOptions.RemoveEmptyEntries));

    private static List<string> Types(IElement item) =>
        item.GetAttribute("itemtype")?.Split((char[])[' ', '\t', '\n', '\r', '\f'], StringSplitOptions.RemoveEmptyEntries).ToList() ?? [];

    /// <summary>A schema.org type or property URL's local name ("https://schema.org/Product" is Product); null for another vocabulary's.</summary>
    private static string? SchemaOrgLocal(string url)
    {
        foreach (var prefix in (string[])["https://schema.org/", "http://schema.org/"])
        {
            if (url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && url.Length > prefix.Length)
            {
                return url[prefix.Length..];
            }
        }

        return null;
    }

    /// <summary>Another vocabulary's base, for the context: "http://data-vocabulary.org/Breadcrumb" is http://data-vocabulary.org.</summary>
    private static string VocabularyOf(string type) =>
        Uri.TryCreate(type, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Authority) : type;

    /// <summary>A property name: a plain one as written, a schema.org URL by its local name; another vocabulary's isn't read.</summary>
    private static string? PropertyName(string name) =>
        name.Contains(':', StringComparison.Ordinal) ? SchemaOrgLocal(name) : name;
}
