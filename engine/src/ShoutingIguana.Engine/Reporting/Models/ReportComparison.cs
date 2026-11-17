using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Reporting.Models;

/// <summary>The crawl compared with another crawl of the site (the fix loop), as the report and the workbook say it.</summary>
/// <param name="OtherPath">The other crawl's file, which the workbook's sheet of what was fixed reads.</param>
/// <param name="StartedAt">When the other crawl started: the "last check" the report names.</param>
/// <param name="Checks">The checks the lists hold (the hidden ones and those outside a view left out), in the comparison's order.</param>
internal sealed record ReportComparison(string OtherPath, DateTimeOffset StartedAt, CrawlComparison Comparison, IReadOnlyList<ReportChange> Checks);
