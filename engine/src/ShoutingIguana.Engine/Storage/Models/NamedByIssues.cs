namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>Issues on other URLs that name a URL as the other URL involved, by check, most first.</summary>
public sealed record NamedByIssues(long Total, IReadOnlyList<CheckCount> Checks);
