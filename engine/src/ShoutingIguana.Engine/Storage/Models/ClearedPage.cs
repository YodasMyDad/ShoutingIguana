namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One page of what a check flagged in the other crawl and doesn't flag now (<see cref="CrawlComparisonReader.ReadCleared"/>).</summary>
/// <param name="Total">
/// The rows or causes that match; null when the comparison is <see cref="Comparability.CountsOnly"/> (no URL can be
/// matched) or the check didn't run in the other crawl.
/// </param>
/// <param name="Rows">With <see cref="ClearedLevel.Rows"/>: in the other crawl's order by importance, then by its URL id.</param>
/// <param name="Groups">With <see cref="ClearedLevel.Groups"/>: the largest then first.</param>
public sealed record ClearedPage(
    string CheckId,
    Comparability Comparable,
    ClearedLevel Level,
    long? Total,
    int Offset,
    IReadOnlyList<ClearedIssue> Rows,
    IReadOnlyList<ClearedGroup> Groups);
