using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>One page of a crawl's origins with their robots.txt.</summary>
/// <param name="Matching">The origins the search and the filter match, which the page is of.</param>
/// <param name="Counts">The origins the search matches, by whether they blocked anything: what a filter bar shows.</param>
internal sealed record RobotsOriginPageDto(long Total, int Offset, bool Respected, string UserAgent, long Blocked, IReadOnlyList<RobotsOriginDto> Origins, long Matching,
    RobotsOriginCounts Counts);
