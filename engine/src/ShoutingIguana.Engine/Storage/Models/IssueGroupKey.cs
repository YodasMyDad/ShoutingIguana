namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// One group of a check's issues: those with this detail and this other URL involved. Null matches null: an issue with no
/// detail, or no other URL.
/// </summary>
public sealed record IssueGroupKey(string? Detail, int? RelatedUrlId);
