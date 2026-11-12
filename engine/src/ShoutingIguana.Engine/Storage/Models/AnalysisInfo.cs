using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using NoScore = ShoutingIguana.Engine.Analysis.Models.NoScoreReason;

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
    /// The URLs crawled, as they stood when the analysis ran: those the crawl requested and those robots.txt kept it from
    /// requesting, as <see cref="CrawlFileSummary.CrawledUrlCount"/> counts them. What a check on any URL
    /// (<see cref="IssueSubject.Url"/>) flagged is a share of these, as what a page check flagged is of
    /// <see cref="PagesAnalyzed"/>: both are the analysis's own, so a crawl continued since can't shift them.
    /// </summary>
    public long? CrawledUrlCount { get; init; }

    /// <summary>The engine's <see cref="ScoreCalculator.VERSION"/> when it ran.</summary>
    public int? ScoreVersion { get; init; }

    /// <summary>
    /// The score's headline, as the analysis stored it. Null when it stored none: it read no page, or its engine didn't
    /// score. The whole of it, with every line, is <see cref="AnalysisResults.Score"/>.
    /// </summary>
    public SiteScoreSummary? Score { get; init; }

    /// <summary>
    /// Why the analysis has no score, or null when it has one: it read no page, so there was nothing to score, or an
    /// engine from before the score ran it.
    /// </summary>
    public NoScoreReason? NoScoreReason => Score is not null ? null : ScoreVersion is not null && PagesAnalyzed == 0 ? NoScore.NoPages : NoScore.NotScored;

    /// <summary>
    /// The analysis stored a URL summary this engine reads (<see cref="UrlSummaryReader"/>): one built by this
    /// <see cref="AnalysisFreshness.VERSION"/>, as the summary's columns change with it. Engines from before the summary
    /// leave this false, as does an older engine that analyses the crawl again, and so does a newer engine's analysis.
    /// Whether the summary still describes the crawl is <see cref="UrlSummaryReader.IsAvailable(string)"/>'s to say.
    /// </summary>
    public bool HasUrlSummary => AnalysisVersion == AnalysisFreshness.VERSION;
}
