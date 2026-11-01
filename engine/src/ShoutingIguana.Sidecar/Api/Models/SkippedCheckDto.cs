namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>A check the last analysis didn't run, which is not the same as one that found nothing.</summary>
/// <param name="Reason">Null until the engine stores each skip's reason; the analysis notes say why meanwhile.</param>
internal sealed record SkippedCheckDto(string CheckId, string? Reason);
