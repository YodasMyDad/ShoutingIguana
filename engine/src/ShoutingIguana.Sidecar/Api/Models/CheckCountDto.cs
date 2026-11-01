namespace ShoutingIguana.Sidecar.Api.Models;

/// <param name="Count">URLs the check flagged: 0 means it ran and found nothing.</param>
internal sealed record CheckCountDto(string CheckId, long Count);
