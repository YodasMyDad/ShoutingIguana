namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One page of the URLs a check flagged, as a URL grid (<see cref="IssueGroupReader.ReadRows"/>).</summary>
/// <param name="Page">Null when the analysis skipped the check: it has no rows, and no total, never 0.</param>
/// <param name="Summary">Whether the URL summary describes the crawl, so the columns that need it have values.</param>
public sealed record IssueRowPage(UrlPage? Page, SummaryState Summary);
