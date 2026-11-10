using System.Globalization;
using ShoutingIguana.Engine.Crawling.Models;

namespace ShoutingIguana.Sidecar.Events.Models;

/// <summary>
/// The part of a progress event's figures that is the crawler's own probes of the site's other versions, each shaped as
/// the event's own and counted within it: a figure less its part here is the URLs the site itself led to.
/// </summary>
/// <param name="Fetched">The probes with an outcome: among the event's <c>fetched</c>.</param>
internal sealed record SiteVersionsDto(long Fetched, StatusClassesDto StatusClasses, IReadOnlyDictionary<string, long> StatusCodes, NoResponseDto NoResponse)
{
    public static SiteVersionsDto From(CrawlSiteVersionCounts counts) => new(
        counts.Fetched,
        StatusClassesDto.From(counts.StatusClasses),
        counts.StatusCodes.ToDictionary(code => code.Key.ToString(CultureInfo.InvariantCulture), code => code.Value, StringComparer.Ordinal),
        NoResponseDto.From(counts.NoResponse));
}
