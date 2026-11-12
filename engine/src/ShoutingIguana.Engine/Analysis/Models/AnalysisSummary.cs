namespace ShoutingIguana.Engine.Analysis.Models;

/// <param name="Issues">
/// Every check that ran, with the number of URLs it flagged, in catalog order. Checks the crawl can't support (orphans
/// on a depth-limited crawl, say) are left out, and <see cref="Skipped"/> and <see cref="Notes"/> say why.
/// </param>
/// <param name="IsPartial">
/// Some results are missing: checks were skipped because the crawl doesn't cover what they need (it was stopped,
/// limited or kept to a folder, sitemaps weren't read or one couldn't be), or a rule failed on some pages.
/// <see cref="Notes"/> says which.
/// </param>
public sealed record AnalysisSummary(long PagesAnalyzed, IReadOnlyList<IssueCount> Issues, TimeSpan Duration, bool IsPartial = false)
{
    /// <summary>What the analysis couldn't do and why, one sentence each, for the analysis output. Also stored with the issues.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>The checks left out of <see cref="Issues"/>, each with why, in catalog order. Also stored with the issues.</summary>
    public IReadOnlyList<SkippedCheck> Skipped { get; init; } = [];

    /// <summary>The URLs crawled, as they stood when the analysis ran: what a check on any URL flagged is a share of. Also stored with the issues.</summary>
    public long CrawledUrlCount { get; init; }

    /// <summary>The site's score (<see cref="ScoreCalculator"/>), or null when no page was read. Also stored with the issues.</summary>
    public SiteScore? Score { get; init; }
}
