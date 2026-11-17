namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// What a recheck is asked to settle: a check, one of its groups, or a list of URLs (with the check to judge them by, or
/// none). A check or a list, never neither.
/// </summary>
/// <param name="CheckId">The check whose flagged URLs it judges, and whose result it gives.</param>
/// <param name="Group">One of the check's groups, as its rows are narrowed to it.</param>
/// <param name="UrlIds">The URLs to judge, by their ids in the crawl, instead of the check's.</param>
public sealed record RecheckAsk(string? CheckId, IssueGroupKey? Group = null, IReadOnlyList<int>? UrlIds = null);
