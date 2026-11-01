using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// When and by which engine a crawl was last analysed, and what it was run against, for
/// <see cref="AnalysisFreshness.Check"/>. The properties are null for an analysis by an engine that didn't store them.
/// </summary>
public sealed record AnalysisInfo(DateTimeOffset AnalyzedAt, string EngineVersion, long PagesAnalyzed, TimeSpan Duration)
{
    /// <summary>Checks were skipped, or a rule failed on some pages (<see cref="AnalysisSummary.IsPartial"/>).</summary>
    public bool? IsPartial { get; init; }

    /// <summary>The thresholds the analysis used.</summary>
    public AnalysisSettings? Settings { get; init; }

    /// <summary>The crawl's <see cref="CrawlFileSummary.UpdatedAt"/> as it stood when the analysis ran.</summary>
    public DateTimeOffset? CrawlUpdatedAt { get; init; }

    /// <summary>The engine's <see cref="AnalysisFreshness.VERSION"/> when it ran.</summary>
    public int? AnalysisVersion { get; init; }

    /// <summary>The engine's <see cref="AnalysisFreshness.ChecksFingerprint"/> when it ran.</summary>
    public string? ChecksFingerprint { get; init; }

    /// <summary>
    /// The analysis stored the URL summary (<see cref="UrlSummaryReader"/>) and why each skipped check was skipped. Engines
    /// from before it did leave this false; so does an older engine that analyses the crawl again.
    /// </summary>
    public bool HasUrlSummary => AnalysisVersion is not null;
}
