namespace ShoutingIguana.Engine.Analysis.Models;

/// <param name="Issues">Every check in the catalog with the number of URLs it flagged, in catalog order.</param>
/// <param name="IsPartial">
/// The crawl was stopped or its sitemaps weren't all read, so orphan and sitemap-coverage checks were skipped.
/// </param>
public sealed record AnalysisSummary(long PagesAnalyzed, IReadOnlyList<IssueCount> Issues, TimeSpan Duration, bool IsPartial = false);
