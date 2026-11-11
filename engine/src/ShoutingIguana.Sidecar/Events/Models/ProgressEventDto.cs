using System.Globalization;
using System.Text.Json;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Sidecar.Api.Models;

namespace ShoutingIguana.Sidecar.Events.Models;

/// <summary>
/// A live crawl's numbers, twice a second (the engine's pace). Counts include earlier runs of a continued crawl. The rate
/// is of every URL fetched, pages and the rest alike (the engine calls it PagesPerSecond). Every count is of one moment,
/// so they agree with each other.
/// </summary>
/// <param name="At">When the numbers were taken, the time the history keeps them under.</param>
/// <param name="MaxUrls">The crawl's URL limit, null when it has none: with <c>internalFetched</c>'s rate, an ETA.</param>
/// <param name="StatusCodes">The URLs with a status that weren't blocked, by code: each class's codes add up to it.</param>
/// <param name="Waits">At most five for each reason, the longest first.</param>
/// <param name="MoreWaits">For each reason with more waits than are listed, how many more: "and 212 more hosts".</param>
/// <param name="Crawled">The URLs crawled, as the URL grid counts them (fetched): <c>fetched</c> less those that weren't checked.</param>
/// <param name="SiteVersions">The part of the figures that is the crawler's own probes of the site's other versions.</param>
/// <param name="CrawlingMs">
/// The time spent crawling over every run of the crawl, this run's without its pauses, where <c>elapsedMs</c> is this
/// run's with them. Null when an earlier run's time wasn't kept.
/// </param>
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
    int? MaxUrls,
    StatusClassesDto StatusClasses,
    IReadOnlyDictionary<string, long> StatusCodes,
    NoResponseDto NoResponse,
    IReadOnlyList<CrawlWaitDto> Waits,
    IReadOnlyDictionary<string, int> MoreWaits,
    long Crawled,
    SiteVersionsDto SiteVersions,
    long? CrawlingMs)
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
        progress.MaxUrls,
        StatusClassesDto.From(progress.StatusClasses),
        progress.StatusCodes.ToDictionary(code => code.Key.ToString(CultureInfo.InvariantCulture), code => code.Value, StringComparer.Ordinal),
        NoResponseDto.From(progress.NoResponse),
        [.. progress.Waits.Select(CrawlWaitDto.From)],
        // Keyed by the reason's name as the waits spell it.
        progress.MoreWaits.ToDictionary(more => JsonNamingPolicy.CamelCase.ConvertName(more.Key.ToString()), more => more.Value, StringComparer.Ordinal),
        progress.Crawled,
        SiteVersionsDto.From(progress.SiteVersions),
        progress.TimeCrawling is { } time ? (long)time.TotalMilliseconds : null);
}
