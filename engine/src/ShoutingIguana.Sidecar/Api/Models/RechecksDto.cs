namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>A crawl's rechecks, newest first: none for a crawl never rechecked.</summary>
internal sealed record RechecksDto(IReadOnlyList<RecheckDto> Rechecks);
