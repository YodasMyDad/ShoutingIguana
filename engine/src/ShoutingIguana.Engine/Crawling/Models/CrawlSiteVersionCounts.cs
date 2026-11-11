namespace ShoutingIguana.Engine.Crawling.Models;

/// <summary>
/// The part of a crawl's counts that is its own probes of the site's other versions (the bare domain or www, over http or
/// https), which nothing on the site leads to: how many have an answer, and how they answered, each counted as
/// <see cref="CrawlProgress"/> counts every URL, and within its counts. A probe the site itself turns out to lead to
/// becomes one of the site's URLs, and leaves these.
/// </summary>
/// <param name="Fetched">The probes with a stored outcome: among <see cref="CrawlProgress.Fetched"/>.</param>
/// <param name="StatusClasses">How they answered: among <see cref="CrawlProgress.StatusClasses"/>, and adding up to <paramref name="Fetched"/>.</param>
/// <param name="StatusCodes">Those with a status that weren't blocked, by status code: among <see cref="CrawlProgress.StatusCodes"/>.</param>
/// <param name="NoResponse">Why those without a status have none: among <see cref="CrawlProgress.NoResponse"/>.</param>
public sealed record CrawlSiteVersionCounts(long Fetched, CrawlStatusClasses StatusClasses, IReadOnlyDictionary<int, long> StatusCodes, CrawlNoResponseCounts NoResponse)
{
    public static CrawlSiteVersionCounts None { get; } = new(0, CrawlStatusClasses.None, new Dictionary<int, long>(), CrawlNoResponseCounts.None);
}
