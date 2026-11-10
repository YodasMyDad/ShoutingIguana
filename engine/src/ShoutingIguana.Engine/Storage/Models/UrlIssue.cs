using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A check that flagged a URL, as the last analysis stored it.</summary>
/// <param name="Severity">As the analysis stored it; null for one this engine doesn't know.</param>
/// <param name="Related">The other URL involved: the broken target, the duplicate, the canonical.</param>
public sealed record UrlIssue(string CheckId, IssueSeverity? Severity, string? Detail, UrlRef? Related)
{
    /// <summary>
    /// The group the issue is in among its check's groups (<see cref="IssueGroupReader.ReadGroups"/>), to open that group's
    /// rows by; null for a check that isn't grouped, or one this engine doesn't know. It's the group's key, not the issue's
    /// own values: a check grouped by the other URL has no detail in it, and one grouped by a fault has the fault.
    /// </summary>
    public IssueGroupKey? Group { get; init; }
}
