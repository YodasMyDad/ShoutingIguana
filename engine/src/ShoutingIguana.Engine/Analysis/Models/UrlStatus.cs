using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>What the crawl learned about one URL, as link checks need it.</summary>
/// <param name="Fetched">A response (or a robots.txt verdict) was recorded. Skipped URLs were never checked.</param>
/// <param name="RedirectTargetId">Where a redirect points, or 0 (also 0 for a redirect without a Location header).</param>
internal readonly record struct UrlStatus(
    bool Known,
    bool IsInternal,
    bool IsHttps,
    int Depth,
    bool Fetched,
    FetchOutcome Outcome,
    int StatusCode,
    bool IsHtml,
    IndexabilityReason IndexabilityReason,
    int RedirectTargetId,
    long? SizeBytes,
    bool InSitemap)
{
    public bool IsPage => Fetched && Outcome == FetchOutcome.Success && IsHtml;

    public bool IsIndexablePage => IsPage && IndexabilityReason == IndexabilityReason.None;

    /// <summary>The URL was checked and does not work: 4xx, 5xx, timeout or connection failure.</summary>
    public bool IsBroken => Fetched && Outcome is FetchOutcome.ClientError or FetchOutcome.ServerError
        or FetchOutcome.Timeout or FetchOutcome.NetworkError;
}
