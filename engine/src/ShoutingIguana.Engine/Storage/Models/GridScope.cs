using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>The rows a grid lists: every URL of the crawl, or the URLs one check flagged, narrowed to one of its groups.</summary>
internal sealed record GridScope(string? CheckId, IssueGroupKey? Group, IssueGrouping Grouping)
{
    public static GridScope AllUrls { get; } = new(null, null, IssueGrouping.None);

    public bool IsCheck => CheckId is not null;
}
