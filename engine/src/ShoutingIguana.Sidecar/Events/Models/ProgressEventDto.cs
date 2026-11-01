using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Sidecar.Api.Models;

namespace ShoutingIguana.Sidecar.Events.Models;

/// <summary>A live crawl's numbers, twice a second (the engine's pace). Counts include earlier runs of a continued crawl.</summary>
internal sealed record ProgressEventDto(
    string CrawlId,
    CrawlListStatus Status,
    long ElapsedMs,
    long Discovered,
    long Queued,
    long InFlight,
    long Fetched,
    long HtmlPages,
    long Redirects,
    long Errors,
    long Blocked,
    long RobotsDisallowed,
    long Skipped,
    long BytesDownloaded,
    double PagesPerSecond)
{
    public static ProgressEventDto From(string crawlId, CrawlListStatus status, CrawlProgress progress) => new(
        crawlId,
        status,
        (long)progress.Elapsed.TotalMilliseconds,
        progress.Discovered,
        progress.Queued,
        progress.InFlight,
        progress.Fetched,
        progress.HtmlPages,
        progress.Redirects,
        progress.Errors,
        progress.Blocked,
        progress.RobotsDisallowed,
        progress.Skipped,
        progress.BytesDownloaded,
        Math.Round(progress.PagesPerSecond, 2));
}
