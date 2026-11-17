namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>How each row of a page of a check's rows moved since another crawl (<see cref="CrawlComparisonReader.ReadRowChanges"/>).</summary>
/// <param name="Changes">One for each URL id asked about, in its order: null when the comparison is counts only, or the check doesn't flag that URL now.</param>
public sealed record RowChanges(PageComparison Compared, IReadOnlyList<RowChange?> Changes);
