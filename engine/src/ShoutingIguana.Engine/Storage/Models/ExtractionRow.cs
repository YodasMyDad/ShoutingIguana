using ShoutingIguana.Engine.Configuration.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One rule's result on one page, as the crawl file stores it.</summary>
/// <param name="Url">The page.</param>
/// <param name="Name">The rule.</param>
/// <param name="SelectorType">How the rule finds its text.</param>
/// <param name="Selector">The selector or pattern.</param>
/// <param name="Value">The matched texts, one a line, or null when the rule failed.</param>
/// <param name="Count">How many matched.</param>
/// <param name="Error">Why the rule failed on this page, or null.</param>
public sealed record ExtractionRow(
    string Url,
    string Name,
    ExtractionSelector SelectorType,
    string Selector,
    string? Value,
    int Count,
    string? Error);
