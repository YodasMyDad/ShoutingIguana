using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One check compared between two crawls (<see cref="CrawlComparisonReader.ReadSummary"/>).</summary>
/// <param name="Severity">Its severity in this crawl's analysis; null when it didn't run now.</param>
/// <param name="SeverityThen">Its severity in the other crawl's analysis; null when it didn't run then.</param>
/// <param name="Then">The URLs it flagged in the other crawl (<c>issue_types.url_count</c>); null when it didn't run then.</param>
/// <param name="Now">The URLs it flags in this crawl; null when it didn't run now.</param>
/// <param name="Rows">How its rows moved; null when the comparison is <see cref="Comparability.CountsOnly"/>.</param>
public sealed record CheckChange(
    string CheckId,
    CheckChangeState State,
    IssueSeverity? Severity,
    IssueSeverity? SeverityThen,
    long? Then,
    long? Now,
    RowSplit? Rows);
