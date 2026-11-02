using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>The rows a grid lists: every URL of the crawl, or the URLs one check flagged, narrowed to one of its groups.</summary>
internal sealed record GridScope(string? CheckId, IssueGroupKey? Group, IssueGrouping Grouping)
{
    public static GridScope AllUrls { get; } = new(null, null, IssueGrouping.None);

    public bool IsCheck => CheckId is not null;

    /// <summary>
    /// The analysis stored every other URL each issue is about, and the fault some checks group by
    /// (<see cref="GridReadContext.HasIssueTargets"/>), so a group of a check grouped by the other URL or by a fault holds
    /// the URLs stored under it, as its count on the Issues screen does.
    /// </summary>
    public bool TargetsStored { get; init; }
}
