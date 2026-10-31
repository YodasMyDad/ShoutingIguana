namespace ShoutingIguana.Engine.Analysis.Models;

/// <param name="Issues">
/// Every check that ran, with the number of URLs it flagged, in catalog order. Checks the crawl can't support (orphans
/// on a depth-limited crawl, say) are left out, and <see cref="Notes"/> says why.
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
}
