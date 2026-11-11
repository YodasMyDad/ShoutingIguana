using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>A crawl's progress since it started, for the "found and crawled" chart, oldest first and downsampled.</summary>
/// <param name="StartedAt">When the crawl first started: the chart's zero.</param>
/// <param name="LiveFrom">
/// When the points taken live begin: the start of the run this sidecar is watching. Points before it are counted back
/// from the crawl file, which keeps when each URL was fetched but not when it was found, nor when each run began and
/// ended. Null when the crawl isn't live here, and every point is from the file.
/// </param>
/// <param name="CrawlingMs">The time spent crawling over every run, or null when it isn't known: <paramref name="Runs"/> added up.</param>
/// <param name="Runs">
/// When the crawl was crawling: each run, with the stretches it spent crawling in, so a chart can leave out the time
/// between runs and the time paused. Null when the crawl's time isn't known, or it was paused too often to list.
/// </param>
internal sealed record HistoryDto(string CrawlId, DateTime StartedAt, DateTime? LiveFrom, IReadOnlyList<HistoryPointDto> Points, long? CrawlingMs = null,
    IReadOnlyList<HistoryRunDto>? Runs = null)
{
    public HistoryDto With(CrawlRuns? runs) =>
        this with { CrawlingMs = runs is null ? null : (long)runs.TimeCrawling.TotalMilliseconds, Runs = runs?.Runs is { } all ? [.. all.Select(HistoryRunDto.From)] : null };
}
