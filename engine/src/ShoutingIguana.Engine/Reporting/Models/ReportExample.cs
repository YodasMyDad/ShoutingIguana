namespace ShoutingIguana.Engine.Reporting.Models;

/// <summary>One of a problem's first pages: the URL flagged, what was found on it, and the other URL involved.</summary>
internal sealed record ReportExample(string Url, string? Detail, string? RelatedUrl);
