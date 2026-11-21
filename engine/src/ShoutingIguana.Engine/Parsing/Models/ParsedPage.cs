namespace ShoutingIguana.Engine.Parsing.Models;

public sealed record ParsedPage(PageFacts Facts, IReadOnlyList<ExtractedLink> Links)
{
    /// <summary>
    /// What the crawl's extraction rules found on this page. Empty with no rules, no match, or a page that wasn't
    /// parsed as HTML. Not stored with the facts: the writer keeps each match in the extractions table.
    /// </summary>
    public IReadOnlyList<ExtractionMatch> Extractions { get; init; } = [];
}
