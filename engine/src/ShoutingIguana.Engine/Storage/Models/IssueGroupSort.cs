namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>How a check's groups are ordered (<see cref="IssueGroupReader.ReadGroups(string, string, int, int, IssueGroupSort?, ReadCache?, CancellationToken)"/>).</summary>
public enum IssueGroupSort
{
    /// <summary>The group with the most URLs first.</summary>
    Pages,

    /// <summary>
    /// The largest file first, as its related URL's redirects end; then that size times the group's URLs. Only for a check
    /// grouped by the other URL: <c>image-too-large</c>'s default.
    /// </summary>
    Size
}
