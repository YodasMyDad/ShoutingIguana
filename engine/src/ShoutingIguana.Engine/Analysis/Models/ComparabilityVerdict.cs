using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>How far two crawls can be compared, and why (<see cref="CrawlComparability.Judge"/>).</summary>
/// <param name="Differences">Every way they differ, in the order of <see cref="ComparisonDifferenceCode"/>.</param>
/// <param name="PreviousNeedsAnalysis">Why the other crawl's analysis isn't like this one's, or no longer stands for its crawl; empty when like for like.</param>
public sealed record ComparabilityVerdict(Comparability Comparable, IReadOnlyList<ComparisonDifference> Differences, IReadOnlyList<OutOfDateReason> PreviousNeedsAnalysis);
