namespace ShoutingIguana.Engine.Crawling.Models;

/// <summary>
/// A crawl's totals at one moment in the writer's work, all from that same moment, so they agree with each other: the
/// status classes add up to <see cref="Fetched"/>, each class's codes add up to it, and the reasons for no response add
/// up to <see cref="CrawlStatusClasses.NoResponse"/>. The writer publishes a new one after each batch it stores, so
/// they are what the file has.
/// </summary>
/// <param name="Bytes">Bytes of the bodies the crawler read, as sent; sizes it was only told by Content-Length aren't counted.</param>
/// <param name="InternalFound">The site's own URLs (internal, not probes of its other versions) queued, in flight or fetched.</param>
/// <param name="InternalFetched">The site's own URLs with a stored response.</param>
/// <param name="StatusCodes">The responses with a status that weren't blocked, by status code: only the codes seen.</param>
internal sealed record CrawlCounts(
    long Discovered,
    long Queued,
    long Skipped,
    long InFlight,
    long Fetched,
    long HtmlPages,
    long Redirects,
    long Errors,
    long Blocked,
    long RobotsDisallowed,
    long Bytes,
    long InternalFound,
    long InternalFetched,
    CrawlStatusClasses StatusClasses,
    CrawlNoResponseCounts NoResponse,
    IReadOnlyDictionary<int, long> StatusCodes)
{
    public static CrawlCounts None { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, CrawlStatusClasses.None, CrawlNoResponseCounts.None,
        new Dictionary<int, long>());

    /// <summary>The part of these that is the probes of the site's other versions.</summary>
    public CrawlSiteVersionCounts SiteVersions { get; init; } = CrawlSiteVersionCounts.None;
}
