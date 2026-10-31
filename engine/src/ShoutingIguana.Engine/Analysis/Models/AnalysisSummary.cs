namespace ShoutingIguana.Engine.Analysis.Models;

/// <param name="Issues">Every check in the catalog with the number of URLs it flagged, in catalog order.</param>
public sealed record AnalysisSummary(long PagesAnalyzed, IReadOnlyList<IssueCount> Issues, TimeSpan Duration);
