namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>The URLs one page's issue names: how many, and the first of them.</summary>
public sealed record IssueTargetsOfUrl(int UrlId, long Total, IReadOnlyList<IssueTarget> Targets);
