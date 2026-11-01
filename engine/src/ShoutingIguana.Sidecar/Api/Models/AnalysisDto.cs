namespace ShoutingIguana.Sidecar.Api.Models;

/// <param name="IsPartial">Null until the engine stores it.</param>
/// <param name="Counts">Every check that ran, in catalog order.</param>
/// <param name="Skipped">Every catalog check missing from the counts.</param>
internal sealed record AnalysisDto(
    DateTime AnalyzedAt,
    string EngineVersion,
    long PagesAnalyzed,
    long DurationMs,
    bool? IsPartial,
    IReadOnlyList<string> Notes,
    IReadOnlyList<CheckCountDto> Counts,
    IReadOnlyList<SkippedCheckDto> Skipped);
