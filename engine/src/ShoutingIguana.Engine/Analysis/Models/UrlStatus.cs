using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>What the crawl learned about one URL, as link checks need it.</summary>
/// <param name="Fetched">A response (or a robots.txt verdict) was recorded. Skipped URLs were never checked.</param>
/// <param name="IsParsed">The HTML body was parsed. Status-checked URLs (outside the start folder, iframes) only have headers.</param>
/// <param name="RedirectTargetId">Where a redirect points, or 0 (also 0 for a redirect without a usable Location header).</param>
internal readonly record struct UrlStatus(
    bool Known,
    bool IsInternal,
    bool IsHttps,
    int Depth,
    bool Fetched,
    FetchOutcome Outcome,
    int StatusCode,
    bool IsHtml,
    bool IsParsed,
    IndexabilityReason IndexabilityReason,
    int RedirectTargetId,
    long? SizeBytes,
    bool InSitemap)
{
    /// <summary>Statuses many sites send crawlers but not people (Amazon answers unknown bots with 503, LinkedIn with 999).</summary>
    private static readonly HashSet<int> BOT_REFUSALS = [401, 403, 405, 429, 503, 999];

    /// <summary>An HTML page the crawler fetched and parsed: one of the site's own pages in scope.</summary>
    public bool IsPage => Fetched && Outcome == FetchOutcome.Success && IsHtml && IsParsed;

    public bool IsIndexablePage => IsPage && IndexabilityReason == IndexabilityReason.None;

    /// <summary>
    /// The URL was checked and does not work: 4xx, 5xx, timeout or connection failure. On other sites, a status
    /// that is often only sent to crawlers doesn't count, because the link may well work in a browser.
    /// </summary>
    public bool IsBroken => Fetched && (Outcome is FetchOutcome.Timeout or FetchOutcome.NetworkError ||
                                        (Outcome is FetchOutcome.ClientError or FetchOutcome.ServerError && (IsInternal || !BOT_REFUSALS.Contains(StatusCode))));
}
