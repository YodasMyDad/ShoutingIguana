using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// A crawl compared with another crawl of the site (<see cref="CrawlComparisonReader.ReadSummary"/>): how far they can be
/// compared and why, the score then and now, and every check that ran in either with how it and its rows moved.
/// </summary>
/// <param name="PreviousNeedsAnalysis">
/// Why the other crawl's analysis isn't like this one's (made by another engine, or with other settings), or no longer
/// stands for its own crawl: analysing it again mends it. Empty when the two are like for like.
/// </param>
/// <param name="Score">The score then and now; null when either crawl has no score.</param>
/// <param name="Checks">Every check that ran in either crawl, in this crawl's fix-first order, then catalog order, then any the catalog doesn't know.</param>
public sealed record CrawlComparison(
    Comparability Comparable,
    IReadOnlyList<ComparisonDifference> Differences,
    IReadOnlyList<OutOfDateReason> PreviousNeedsAnalysis,
    ScoreChange? Score,
    IReadOnlyList<CheckChange> Checks,
    ComparisonTotals Totals);
