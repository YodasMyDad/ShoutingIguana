namespace ShoutingIguana.Engine.Reporting.Models;

/// <summary>One of a problem's first pages, as the report lists it (<see cref="ReportRows"/>).</summary>
/// <param name="Evidence">The check's evidence for the page, a value each, as a line of the report says it.</param>
internal sealed record ReportRow(int UrlId, string Url, string? Detail, string? RelatedUrl, IReadOnlyList<string> Evidence);
