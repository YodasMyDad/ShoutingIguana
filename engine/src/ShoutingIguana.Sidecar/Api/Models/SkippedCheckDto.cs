namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>A check the last analysis didn't run, which is not the same as one that found nothing.</summary>
/// <param name="Reason">Null when an engine from before it stored reasons ran the analysis; the notes say why then.</param>
internal sealed record SkippedCheckDto(string CheckId, SkipReasonDto? Reason);
