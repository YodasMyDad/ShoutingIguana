namespace ShoutingIguana.Sidecar.Events.Models;

/// <summary>An event as a stream sends it: its name, and its data already serialised once for every client.</summary>
/// <param name="CrawlId">The crawl it is about, if any, which keeps its progress in order with it.</param>
internal sealed record SidecarEvent(string Name, string Data, string? CrawlId = null);
