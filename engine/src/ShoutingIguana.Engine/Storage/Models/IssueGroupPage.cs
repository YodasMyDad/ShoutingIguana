using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One page of a check's issues grouped by their cause (<see cref="IssueGroupReader.ReadGroups"/>), largest first.</summary>
/// <param name="Grouping">How the check's issues group; a check that isn't grouped has no groups.</param>
/// <param name="Total">The URLs the check flagged; null when the analysis skipped the check, never 0.</param>
/// <param name="GroupCount">How many groups there are; null, as the total is, when the analysis skipped the check.</param>
public sealed record IssueGroupPage(string CheckId, IssueGrouping Grouping, long? Total, long? GroupCount, int Offset, IReadOnlyList<IssueGroup> Groups)
{
    /// <summary>The order the groups are in.</summary>
    public IssueGroupSort Sort { get; init; }

    /// <summary>
    /// For a check that doesn't group, that it flagged nearly every page audited (<see cref="IssueGroupReader.IsLikelyTemplateCheck"/>),
    /// so it's probably the site's template. False for a grouped check, whose groups say it each.
    /// </summary>
    public bool LikelyTemplate { get; init; }

    /// <summary>
    /// With another crawl to compare with, how far the two can be compared and how many causes then have no URL now; each
    /// group then has its <see cref="IssueGroup.Change"/>. Null without one, and when the other crawl was never analysed.
    /// </summary>
    public PageComparison? Compared { get; init; }
}
