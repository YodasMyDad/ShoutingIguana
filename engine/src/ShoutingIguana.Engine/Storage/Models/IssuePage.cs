namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// One page of the URLs a check flagged, in crawl order, with the check's total from the same analysis. The total is
/// null, and there are no rows, when the analysis didn't run the check.
/// </summary>
public sealed record IssuePage(long? Total, IReadOnlyList<IssueRow> Rows);
