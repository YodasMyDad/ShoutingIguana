namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>A stretch of time a crawl spent crawling, unbroken by a pause or a stop.</summary>
/// <param name="To">When it ended; for the one going now, when the engine last stored it, some seconds ago at most.</param>
internal sealed record HistoryStretchDto(DateTime From, DateTime To);
