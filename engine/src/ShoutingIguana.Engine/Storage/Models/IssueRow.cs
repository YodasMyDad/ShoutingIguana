namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A flagged URL as stored by the last analysis.</summary>
/// <param name="UrlId">The flagged URL's id, which the grids and the inspector know it by.</param>
public sealed record IssueRow(string Issue, string Url, string? Detail, string? RelatedUrl, int UrlId = 0);
