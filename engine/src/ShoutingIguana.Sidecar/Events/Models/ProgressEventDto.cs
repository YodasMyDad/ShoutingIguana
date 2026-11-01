using System.Globalization;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Sidecar.Api.Models;

namespace ShoutingIguana.Sidecar.Events.Models;

/// <summary>
/// A live crawl's numbers, twice a second (the engine's pace). Counts include earlier runs of a continued crawl. The rate
/// is of every URL fetched, pages and the rest alike (the engine calls it PagesPerSecond).
/// </summary>
/// <param name="At">When the numbers were taken, the time the history keeps them under.</param>
/// <param name="StatusCodes">The URLs with a status that weren't blocked, by code: each class's codes add up to it.</param>
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
    double UrlsPerSecond,
    DateTime At,
    CrawlPhase Phase,
    long InternalFound,
    long InternalFetched,
    StatusClassesDto StatusClasses,
    IReadOnlyDictionary<string, long> StatusCodes,
    NoResponseDto NoResponse,
    IReadOnlyList<CrawlWaitDto> Waits)
{
    public static ProgressEventDto From(string crawlId, CrawlListStatus status, CrawlProgress progress, DateTime at) => new(
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
        Math.Round(progress.PagesPerSecond, 2),
        at,
        progress.Phase,
        progress.InternalFound,
        progress.InternalFetched,
        StatusClassesDto.From(progress.StatusClasses),
        progress.StatusCodes.ToDictionary(code => code.Key.ToString(CultureInfo.InvariantCulture), code => code.Value, StringComparer.Ordinal),
        NoResponseDto.From(progress.NoResponse),
        [.. progress.Waits.Select(CrawlWaitDto.From)]);
}
