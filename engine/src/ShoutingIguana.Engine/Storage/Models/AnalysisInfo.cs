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
    /// The analysis stored a URL summary this engine reads (<see cref="UrlSummaryReader"/>): one built by this
    /// <see cref="AnalysisFreshness.VERSION"/>, as the summary's columns change with it. Engines from before the summary
    /// leave this false, as does an older engine that analyses the crawl again, and so does a newer engine's analysis.
    /// Whether the summary still describes the crawl is <see cref="UrlSummaryReader.IsAvailable(string)"/>'s to say.
    /// </summary>
    public bool HasUrlSummary => AnalysisVersion == AnalysisFreshness.VERSION;
}
