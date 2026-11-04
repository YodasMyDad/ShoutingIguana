using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One page of a check's issues grouped by their cause (<see cref="IssueGroupReader.ReadGroups"/>), largest first.</summary>
/// <param name="Grouping">How the check's issues group; a check that isn't grouped has no groups.</param>
/// <param name="Total">The URLs the check flagged; null when the analysis skipped the check, never 0.</param>
/// <param name="GroupCount">How many groups there are; null, as the total is, when the analysis skipped the check.</param>
public sealed record IssueGroupPage(string CheckId, IssueGrouping Grouping, long? Total, long? GroupCount, int Offset, IReadOnlyList<IssueGroup> Groups);
