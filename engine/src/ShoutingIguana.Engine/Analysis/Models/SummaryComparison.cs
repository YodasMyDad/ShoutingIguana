using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>
/// A crawl compared with another crawl of the site at the check level, from the two crawls' summaries alone
/// (<see cref="CrawlComparability.FromSummaries"/>): what a list of crawls shows without opening either file. It agrees
/// with <see cref="Storage.CrawlComparisonReader.ReadSummary"/> on how far the two can be compared, what the other needs
/// before it is like for like, the score and each check's counts; a check at 0 now is fixed or fewer by the rule the
/// comparison has without its rows, since a summary has none.
/// </summary>
/// <param name="PreviousNeedsAnalysis">As <see cref="CrawlComparison.PreviousNeedsAnalysis"/>: empty when the two are like for like.</param>
/// <param name="ScoreChange">Now less then; null when either has no score, or the two were scored by different versions.</param>
/// <param name="Partial">Either crawl didn't read all it set out to (its score is partial, or it has none and didn't complete): a change "so far".</param>
/// <param name="Checks">Every check that ran in either crawl, in this crawl's fix-first order, then catalog order, then any the catalog doesn't know.</param>
public sealed record SummaryComparison(
    Comparability Comparable,
    IReadOnlyList<OutOfDateReason> PreviousNeedsAnalysis,
    int? ScoreChange,
    bool Partial,
    IReadOnlyList<CheckCountChange> Checks);
