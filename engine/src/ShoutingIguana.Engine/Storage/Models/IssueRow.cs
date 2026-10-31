namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A flagged URL as stored by the last analysis.</summary>
public sealed record IssueRow(string Issue, string Url, string? Detail, string? RelatedUrl);
