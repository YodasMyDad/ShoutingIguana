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
    double PagesPerSecond)
{
    private static readonly IReadOnlyDictionary<int, long> NO_STATUS_CODES = new Dictionary<int, long>();
    private static readonly IReadOnlyDictionary<CrawlWaitReason, int> NO_MORE_WAITS = new Dictionary<CrawlWaitReason, int>();

    public CrawlPhase Phase { get; init; }

    /// <summary>Every URL fetched by how it answered: the classes add up to <see cref="Fetched"/>.</summary>
    public CrawlStatusClasses StatusClasses { get; init; } = CrawlStatusClasses.None;

    /// <summary>
    /// The URLs fetched that got a status and weren't blocked, by status code, so each class's codes add up to it: a block
    /// page's status (a firewall's 403, say) is the firewall's, not the site's.
    /// </summary>
    public IReadOnlyDictionary<int, long> StatusCodes { get; init; } = NO_STATUS_CODES;

    /// <summary>Why the URLs without a status have none.</summary>
    public CrawlNoResponseCounts NoResponse { get; init; } = CrawlNoResponseCounts.None;

    /// <summary>
    /// The time the crawl has spent crawling, over every run of it: the earlier runs', and this run's without the time it
    /// was paused, so it stands still during a pause, where <see cref="Elapsed"/> (this run's alone) runs on. Null when
    /// the time an earlier run took wasn't kept (an earlier engine's run), so the whole isn't known.
    /// </summary>
    public TimeSpan? TimeCrawling { get; init; }

    /// <summary>
    /// The URLs crawled: those asked for, and those robots.txt kept the crawl from asking. <see cref="Fetched"/> less the
    /// URLs that weren't checked (<see cref="CrawlNoResponseCounts.NotChecked"/>), which have an outcome though nothing was
    /// asked: the URL grid has those as not fetched, so this is its count of the URLs fetched.
    /// </summary>
    public long Crawled => Fetched - NoResponse.NotChecked;

    /// <summary>
    /// The part of the counts that is the crawler's own probes of the site's other versions, which nothing on the site
    /// leads to: every count here counts them, as the crawl file has them, and this says how much of each they are.
    /// </summary>
    public CrawlSiteVersionCounts SiteVersions { get; init; } = CrawlSiteVersionCounts.None;

    /// <summary>
    /// The site's own URLs (internal, not the crawler's probes of its other versions) the crawl means to fetch: queued, in
    /// flight or fetched. Those it skipped (nofollow, a limit) aren't counted, so once a URL limit is reached this is
    /// the limit, and a crawl with one heads for the smaller of the two.
    /// </summary>
    public long InternalFound { get; init; }

    /// <summary>The site's own URLs fetched: what a URL limit counts. Never more than <see cref="InternalFound"/>, nor the limit.</summary>
    public long InternalFetched { get; init; }

    /// <summary>The crawl's URL limit, or null when it has none: with <see cref="InternalFetched"/>'s rate, what an ETA needs.</summary>
    public int? MaxUrls { get; init; }

    /// <summary>
    /// What holds part of the crawl up now, if anything: robots.txt, a crawl-delay, a backoff, a site that's down. At most
    /// five for each reason, the longest first; nothing once the crawl is finishing.
    /// </summary>
    public IReadOnlyList<CrawlWait> Waits { get; init; } = [];

    /// <summary>For each reason with more waits than <see cref="Waits"/> lists, how many more there are: hosts, as a rule.</summary>
    public IReadOnlyDictionary<CrawlWaitReason, int> MoreWaits { get; init; } = NO_MORE_WAITS;

    /// <summary>For a recheck's run, how far it has got; null for a crawl's. The other counts are the whole crawl's.</summary>
    public RecheckProgress? Recheck { get; init; }
}
