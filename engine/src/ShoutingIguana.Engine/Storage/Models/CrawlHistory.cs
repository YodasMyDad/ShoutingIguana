namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A crawl's fetching over time, as its file can tell it.</summary>
/// <param name="StartedAt">When the crawl first started.</param>
/// <param name="Points">
/// From a zero at <paramref name="StartedAt"/>, how many URLs had been fetched by each moment, oldest first, downsampled.
/// A URL fetched again (a status check made a full fetch) counts from its last fetch.
/// </param>
public sealed record CrawlHistory(DateTimeOffset StartedAt, IReadOnlyList<CrawlHistoryPoint> Points)
{
    /// <summary>
    /// When the crawl was crawling: its runs, and the time they took between them, so a chart can leave out the time
    /// between runs and the time paused. Null when that isn't known (<see cref="CrawlRunReader"/>).
    /// </summary>
    public CrawlRuns? Runs { get; init; }
}
