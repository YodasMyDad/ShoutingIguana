namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A crawl's fetching over time, as its file can tell it.</summary>
/// <param name="StartedAt">When the crawl first started.</param>
/// <param name="Points">
/// From a zero at <paramref name="StartedAt"/>, how many URLs had been fetched by each moment, oldest first, downsampled.
/// A URL fetched again (a status check made a full fetch) counts from its last fetch.
/// </param>
public sealed record CrawlHistory(DateTimeOffset StartedAt, IReadOnlyList<CrawlHistoryPoint> Points);
