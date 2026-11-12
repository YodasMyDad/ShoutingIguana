namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One page of a crawl's origins with their robots.txt: the site's own first, then by the URLs each blocked.</summary>
/// <param name="Total">Every origin.</param>
/// <param name="Respected">False for a crawl told to ignore robots.txt, which blocks nothing.</param>
/// <param name="UserAgent">The name the crawl looked for its group by.</param>
/// <param name="Blocked">Every URL the crawl stored as refused by robots.txt.</param>
public sealed record RobotsOriginPage(long Total, int Offset, bool Respected, string UserAgent, long Blocked, IReadOnlyList<RobotsOrigin> Origins)
{
    /// <summary>The origins the search and the filter asked for match, which the page is of; every origin with neither.</summary>
    public long Matching { get; init; } = Total;

    /// <summary>The origins the search matches, counted by whether they blocked anything.</summary>
    public RobotsOriginCounts Counts { get; init; } = new(Total, 0, Total);
}
