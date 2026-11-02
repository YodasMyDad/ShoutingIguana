namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>A crawl's progress since it started, for the "found and crawled" chart, oldest first and downsampled.</summary>
/// <param name="StartedAt">When the crawl first started: the chart's zero.</param>
/// <param name="LiveFrom">
/// When the points taken live begin: the start of the run this sidecar is watching. Points before it are counted back
/// from the crawl file, which keeps when each URL was fetched but not when it was found, nor when each run began and
/// ended. Null when the crawl isn't live here, and every point is from the file.
/// </param>
internal sealed record HistoryDto(string CrawlId, DateTime StartedAt, DateTime? LiveFrom, IReadOnlyList<HistoryPointDto> Points);
