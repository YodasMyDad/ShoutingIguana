using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using NoScore = ShoutingIguana.Engine.Analysis.Models.NoScoreReason;

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
    /// <summary>
    /// The score the analysis stored, with all it takes to show how it was worked out. Null when there is none:
    /// <see cref="NoScoreReason"/> says why.
    /// </summary>
    public SiteScore? Score { get; init; }

    /// <summary>
    /// The checks whose every issue is something the site's platform sets, which its owner can't change (a Shopify store's
    /// security headers and Shopify's cookies), by id, each with the platform and why. They are in <see cref="Counts"/>, and
    /// no part of the score or <see cref="FixFirst"/>. Empty for an analysis by an engine from before it was told.
    /// </summary>
    public IReadOnlyDictionary<string, PlatformOwner> PlatformOwned { get; init; } = new Dictionary<string, PlatformOwner>();

    /// <summary>
    /// Why there's no score, or null when there is one: as <see cref="AnalysisInfo.NoScoreReason"/> says, and a score
    /// this engine can't read (a later engine's) is no score either.
    /// </summary>
    public NoScoreReason? NoScoreReason => Score is not null ? null : Analysis.NoScoreReason ?? NoScore.NotScored;

    /// <summary>
    /// The checks that found something, in the order to fix them (<see cref="FixFirstOrder"/>): the most severe, a fault
    /// of the whole site leading its severity, then the most URLs flagged, then catalog order. Checks the user has hidden
    /// in the app are in it; checks of what the site's platform set (<see cref="PlatformOwned"/>) aren't.
    /// </summary>
    public IReadOnlyList<string> FixFirst => FixFirstOrder.Of(Counts, PlatformOwned);

    /// <summary>Why the analysis no longer stands, if it doesn't, as of the snapshot.</summary>
    /// <param name="settings">The thresholds an analysis would run with now.</param>
    public IReadOnlyList<OutOfDateReason> OutOfDate(AnalysisSettings settings) => AnalysisFreshness.Check(Analysis, CrawlUpdatedAt, settings);
}
