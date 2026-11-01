using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// The last analysis as one snapshot: when it ran, the URLs each check flagged (0 for a check that ran and found
/// nothing; a check it skipped is missing), what it couldn't do, why each skipped check was skipped, and the crawl's
/// stamp as it stood at the same moment.
/// </summary>
/// <param name="CrawlUpdatedAt">The crawl's <see cref="CrawlFileSummary.UpdatedAt"/>, read in the same snapshot.</param>
/// <param name="Skipped">As <see cref="IssueReader.ReadSkippedChecks"/> returns them.</param>
public sealed record AnalysisResults(
    AnalysisInfo Analysis,
    DateTimeOffset CrawlUpdatedAt,
    IReadOnlyDictionary<string, long> Counts,
    IReadOnlyList<string> Notes,
    IReadOnlyList<SkippedCheck> Skipped)
{
    /// <summary>Why the analysis no longer stands, if it doesn't, as of the snapshot.</summary>
    /// <param name="settings">The thresholds an analysis would run with now.</param>
    public IReadOnlyList<OutOfDateReason> OutOfDate(AnalysisSettings settings) => AnalysisFreshness.Check(Analysis, CrawlUpdatedAt, settings);
}
