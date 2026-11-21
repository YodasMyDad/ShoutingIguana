using ShoutingIguana.Engine.Configuration.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One of the crawl's rules, and how many pages it matched or failed on.</summary>
/// <param name="Name">The rule's name.</param>
/// <param name="SelectorType">How it finds its text.</param>
/// <param name="Selector">The selector or pattern.</param>
/// <param name="Enabled">Whether the crawl applied it.</param>
/// <param name="Matches">Pages it found something on.</param>
/// <param name="Errors">Pages it failed on. A failed rule is not tried on later pages, so this is at most one.</param>
public sealed record ExtractionRuleCount(
    string Name,
    ExtractionSelector SelectorType,
    string Selector,
    bool Enabled,
    long Matches,
    long Errors);
