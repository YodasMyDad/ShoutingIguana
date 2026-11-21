namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>The crawl's extraction rules, and one page of what they found.</summary>
/// <param name="Rules">Every rule the crawl was given, whether or not it matched.</param>
/// <param name="Total">Rows the query matches, before the page.</param>
/// <param name="Rows">The page, in URL order then rule name.</param>
public sealed record ExtractionPage(IReadOnlyList<ExtractionRuleCount> Rules, long Total, IReadOnlyList<ExtractionRow> Rows);
