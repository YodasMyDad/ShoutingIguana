namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A page of what a check flagged in the other crawl and doesn't flag now (<see cref="CrawlComparisonReader.ReadCleared"/>).</summary>
/// <param name="States">
/// Narrows rows (and the causes' counts) to these of <see cref="RowChangeState.Fixed"/>, <see cref="RowChangeState.Gone"/>
/// and <see cref="RowChangeState.NotChecked"/>; all three when null or empty.
/// </param>
/// <param name="Group">Narrows rows to one cause then; null for every row.</param>
public sealed record ClearedQuery(
    ClearedLevel Level = ClearedLevel.Rows,
    IReadOnlyList<RowChangeState>? States = null,
    ClearedGroupKey? Group = null,
    int Offset = 0,
    int Limit = UrlGridReader.DEFAULT_LIMIT);
