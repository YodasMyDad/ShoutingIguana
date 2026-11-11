using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// The part of a crawl's stored outcomes that is its own probes of the site's other versions (the bare domain or www,
/// over http or https), which nothing on the site leads to: <see cref="CrawlStats.Outcomes"/> and
/// <see cref="CrawlStats.StatusCodes"/> count them among the rest, and these say how many of each they are.
/// </summary>
/// <param name="Fetched">The probes with a stored outcome.</param>
public sealed record SiteVersionStats(long Fetched, IReadOnlyDictionary<FetchOutcome, long> Outcomes, IReadOnlyDictionary<int, long> StatusCodes)
{
    public static SiteVersionStats None { get; } = new(0, new Dictionary<FetchOutcome, long>(), new Dictionary<int, long>());
}
