namespace ShoutingIguana.Sidecar.Events.Models;

/// <summary>
/// The first event on every stream. The latest progress of each live crawl follows it, then the latest analysis event of
/// each crawl being analysed, then the latest <c>export</c> event of each export running.
/// </summary>
/// <param name="Live">The crawls running, paused or stopping in this process.</param>
/// <param name="Analyzing">The crawls being analysed in this process: the <c>analysis</c> events that follow are theirs.</param>
/// <param name="Exports">The exports running in this process, by id (E5): the <c>export</c> events that follow are theirs.</param>
internal sealed record HelloEventDto(IReadOnlyList<string> Live, IReadOnlyList<string> Analyzing, IReadOnlyList<string> Exports);
