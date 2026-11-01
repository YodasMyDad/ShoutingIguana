using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <param name="IsPartial">Null when an engine from before it was stored ran the analysis; the notes say why then.</param>
/// <param name="OutOfDate">The analysis no longer stands; <paramref name="OutOfDateReasons"/> says why.</param>
/// <param name="Counts">Every check that ran, in catalog order.</param>
/// <param name="Skipped">Every catalog check missing from the counts.</param>
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
    IReadOnlyList<SkippedCheckDto> Skipped);
