namespace ShoutingIguana.Engine.Fetching.Models;

/// <summary>
/// One member of an RFC 8941 dictionary, as <see cref="StructuredFields"/> reads it: its key, its value (an inner list's
/// items, or a single item), and the value's parameters. Items are kept as their bare text: a string without its quotes
/// and escapes, a byte sequence without its colons, a number or token as written.
/// </summary>
/// <param name="ItemKind">What the single item is, or <see cref="StructuredItemKind.InnerList"/> for a list.</param>
internal sealed record StructuredMember(
    string Key,
    StructuredItemKind ItemKind,
    string? Item,
    IReadOnlyList<string> InnerList,
    IReadOnlyDictionary<string, (StructuredItemKind Kind, string Value)> Parameters);
