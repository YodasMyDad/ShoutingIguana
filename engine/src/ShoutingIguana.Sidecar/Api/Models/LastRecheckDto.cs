namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>A crawl's latest recheck in brief: when it ended, of which check, how many URLs it judged and, once settled, how many it found fixed.</summary>
/// <param name="Fixed">Null until the analysis after it settled it.</param>
internal sealed record LastRecheckDto(int Id, DateTime? EndedAt, string? CheckId, int Judged, int? Fixed);
