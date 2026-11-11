using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <param name="IsPartial">Null when an engine from before it was stored ran the analysis; the notes say why then.</param>
/// <param name="OutOfDate">The analysis no longer stands; <paramref name="OutOfDateReasons"/> says why.</param>
/// <param name="Counts">Every check that ran, in catalog order.</param>
/// <param name="Skipped">Every catalog check missing from the counts.</param>
/// <param name="CrawledUrlCount">
/// The URLs crawled as they stood when the analysis ran: what a check on any URL flagged is a share of, as
/// <paramref name="PagesAnalyzed"/> is for a page check. Null when an engine from before it was stored ran the analysis.
/// </param>
/// <param name="FixFirst">The checks that found something, in the order to fix them. Hidden checks are in it: hiding is the app's.</param>
/// <param name="Score">The site's score as the analysis stored it; null when there is none.</param>
/// <param name="NoScoreReason">Why there is no score; null when there is one.</param>
internal sealed record AnalysisDto(
    DateTime AnalyzedAt,
    string EngineVersion,
    long PagesAnalyzed,
    long DurationMs,
    bool? IsPartial,
    bool OutOfDate,
    IReadOnlyList<OutOfDateReason> OutOfDateReasons,
    IReadOnlyList<string> Notes,
    IReadOnlyList<CheckCountDto> Counts,
    IReadOnlyList<SkippedCheckDto> Skipped,
    long? CrawledUrlCount,
    IReadOnlyList<string> FixFirst,
    SiteScore? Score,
    NoScoreReason? NoScoreReason);
