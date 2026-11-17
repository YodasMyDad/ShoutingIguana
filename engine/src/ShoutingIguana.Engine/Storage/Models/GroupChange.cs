namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>How one group of a check moved since the other crawl (<see cref="IssueGroup.Change"/>).</summary>
/// <param name="Then">The URLs the matching cause had then; 0 when it is <see cref="GroupChangeState.New"/>.</param>
public sealed record GroupChange(GroupChangeState State, long Then);
