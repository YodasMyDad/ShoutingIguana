namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>
/// The part of <c>/stats</c>'s <c>outcomes</c> and <c>statusCodes</c> that is the crawler's own probes of the site's other
/// versions, keyed as those are: a crawl at rest's answer to the progress event's <c>siteVersions</c>.
/// </summary>
/// <param name="Fetched">The probes with a stored outcome.</param>
internal sealed record SiteVersionStatsDto(long Fetched, IReadOnlyDictionary<string, long> Outcomes, IReadOnlyDictionary<string, long> StatusCodes);
