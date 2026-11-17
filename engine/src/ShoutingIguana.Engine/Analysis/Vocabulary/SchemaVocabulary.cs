using System.Collections.Concurrent;
using System.Text.Json;

namespace ShoutingIguana.Engine.Analysis.Vocabulary;

/// <summary>
/// The schema.org vocabulary and the ISO 4217 currency codes, as bundled (schema-org.json, written by generate.mjs beside
/// it): each type's parents, each property's types and whether it takes only dates. Names are schema.org's local names
/// (Product, priceCurrency).
/// </summary>
internal sealed class SchemaVocabulary
{
    private readonly Dictionary<string, string[]> _parents;
    private readonly Dictionary<string, (string[] Domains, bool Dates)> _properties;
    private readonly HashSet<string> _currencies;
    private readonly ConcurrentDictionary<string, HashSet<string>> _ancestors = new(StringComparer.Ordinal);

    private SchemaVocabulary(Dictionary<string, string[]> parents, Dictionary<string, (string[] Domains, bool Dates)> properties, HashSet<string> currencies)
    {
        _parents = parents;
        _properties = properties;
        _currencies = currencies;
    }

    public static SchemaVocabulary Instance { get; } = Load();

    public bool IsType(string name) => _parents.ContainsKey(name);

    public bool IsProperty(string name) => _properties.ContainsKey(name);

    /// <summary>The property takes only dates (Date or DateTime), so a value must be one, in ISO 8601.</summary>
    public bool TakesOnlyDates(string property) => _properties.TryGetValue(property, out var known) && known.Dates;

    public bool IsCurrency(string code) => _currencies.Contains(code);

    /// <summary>The property is schema.org's for one of the types or a type they descend from (a property of Thing is everyone's).</summary>
    public bool IsPropertyOf(IEnumerable<string> types, string property) =>
        _properties.TryGetValue(property, out var known) && types.Any(type => known.Domains.Any(Ancestors(type).Contains));

    /// <summary>The type and every type it descends from.</summary>
    private HashSet<string> Ancestors(string type) => _ancestors.GetOrAdd(type, start =>
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        Stack<string> pending = new([start]);
        while (pending.TryPop(out var next))
        {
            if (seen.Add(next) && _parents.TryGetValue(next, out var parents))
            {
                foreach (var parent in parents)
                {
                    pending.Push(parent);
                }
            }
        }

        return seen;
    });

    private static SchemaVocabulary Load()
    {
        using var stream = typeof(SchemaVocabulary).Assembly.GetManifestResourceStream("schema-org.json")
                           ?? throw new InvalidOperationException("The bundled schema.org vocabulary is missing.");
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        var parents = root.GetProperty("types").EnumerateObject()
            .ToDictionary(type => type.Name, type => type.Value.EnumerateArray().Select(parent => parent.GetString()!).ToArray(), StringComparer.Ordinal);
        var properties = root.GetProperty("properties").EnumerateObject().ToDictionary(
            property => property.Name,
            property => (property.Value.GetProperty("domains").EnumerateArray().Select(domain => domain.GetString()!).ToArray(), property.Value.GetProperty("dates").GetBoolean()),
            StringComparer.Ordinal);
        var currencies = root.GetProperty("currencies").EnumerateArray().Select(code => code.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new SchemaVocabulary(parents, properties, currencies);
    }
}
