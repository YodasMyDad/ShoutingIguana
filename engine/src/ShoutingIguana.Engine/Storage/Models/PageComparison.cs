namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A check's groups or rows read with another crawl to compare with: how far the two can be compared.</summary>
/// <param name="FixedGroups">
/// On a page of groups, how many causes then have no URL now (what <see cref="CrawlComparisonReader.ReadCleared"/> lists by
/// cause). Null on a page of rows, for a check that isn't grouped or didn't run in both crawls, and when the comparison is
/// <see cref="Comparability.CountsOnly"/>.
/// </param>
public sealed record PageComparison(Comparability Comparable, long? FixedGroups = null);
