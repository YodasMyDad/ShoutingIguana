namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>How each group of a page of a check's groups moved since another crawl (<see cref="CrawlComparisonReader.ReadGroupChanges"/>).</summary>
/// <param name="Changes">One for each group asked about, in its order: null when the comparison is counts only, or the check didn't run in both crawls.</param>
public sealed record GroupChanges(PageComparison Compared, IReadOnlyList<GroupChange?> Changes);
