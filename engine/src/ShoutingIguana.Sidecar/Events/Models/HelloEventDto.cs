namespace ShoutingIguana.Sidecar.Events.Models;

/// <summary>The first event on every stream. The latest progress of each live crawl follows it.</summary>
/// <param name="Live">The crawls running, paused or stopping in this process.</param>
internal sealed record HelloEventDto(IReadOnlyList<string> Live);
