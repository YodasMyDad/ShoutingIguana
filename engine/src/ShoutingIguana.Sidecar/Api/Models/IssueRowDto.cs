namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>A flagged URL. Crawled text comes back exactly as stored: the UI shows it as text, never as HTML.</summary>
internal sealed record IssueRowDto(string Url, string? Detail, string? RelatedUrl);
