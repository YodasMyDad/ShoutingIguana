namespace ShoutingIguana.Engine.Crawling.Models;

/// <summary>A point-in-time view of a crawl. Counts include work restored from earlier runs of a resumed crawl.</summary>
/// <param name="Elapsed">Time spent in this run.</param>
/// <param name="PagesPerSecond">Responses recorded per second over the last few seconds.</param>
public sealed record CrawlProgress(
    CrawlStatus Status,
    TimeSpan Elapsed,
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
    double PagesPerSecond);
