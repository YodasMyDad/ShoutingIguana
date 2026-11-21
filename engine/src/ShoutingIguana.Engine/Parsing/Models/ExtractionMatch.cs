using ShoutingIguana.Engine.Configuration.Models;

namespace ShoutingIguana.Engine.Parsing.Models;

/// <summary>
/// What one extraction rule found on one page, from the document the page was parsed into. Not a fact the checks
/// read: the writer stores it in its own table, and a page with nothing matched stores nothing.
/// </summary>
/// <param name="Name">The rule's name.</param>
/// <param name="SelectorType">How the rule finds its text.</param>
/// <param name="Selector">The selector or pattern, as the rule has it.</param>
/// <param name="Values">The matched texts, trimmed, at most <see cref="CustomExtractor.MAX_VALUES"/> of them.</param>
/// <param name="Count">How many matched, which can be more than <paramref name="Values"/> holds.</param>
/// <param name="Error">Why the rule failed on this page, when it did. The rule is not tried on later pages.</param>
public sealed record ExtractionMatch(
    string Name,
    ExtractionSelector SelectorType,
    string Selector,
    IReadOnlyList<string> Values,
    int Count,
    string? Error);
