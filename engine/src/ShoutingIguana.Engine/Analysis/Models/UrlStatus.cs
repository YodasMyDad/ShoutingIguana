using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>
/// What the crawl learned about one URL, as link checks need it, adjusted to what search engines make of it (see
/// <see cref="UrlTable"/>): a page with an instant meta refresh is a redirect, and a canonical that leads back to the
/// page or nowhere doesn't make it non-indexable.
/// </summary>
/// <param name="Fetched">A response (or a robots.txt verdict) was recorded. Skipped URLs were never checked.</param>
/// <param name="IsParsed">The HTML body was parsed. Status-checked URLs (outside the start folder, iframes) only have headers.</param>
/// <param name="RedirectTargetId">Where a redirect points, or 0 (also 0 for a redirect without a usable Location header).</param>
/// <param name="IsMetaRefresh">A 200 page that moves on with an instant meta refresh, so it counts as a redirect.</param>
/// <param name="IsResource">An image, video, audio file, font, stylesheet or script rather than a page (see <see cref="ResourceTypes"/>).</param>
/// <param name="Source">How the crawl found it. <see cref="DiscoverySource.SiteVersion"/>: only a probe of the site's other versions did.</param>
/// <param name="IsRateLimited">
/// Asked, but the site only ever answered 429 Too Many Requests, however long the crawl waited: stored as not checked
/// (<see cref="Fetched"/> false), so nothing judges it, but the crawl did try to read it.
/// </param>
/// <param name="ResponseMs">How long the whole response took, as the crawl measured it; 0 when it wasn't fetched.</param>
/// <param name="ContentClass">What a successful response's Content-Type says it is (<see cref="Models.ContentClass"/>); none for any other.</param>
/// <param name="PixelWidth">An image's own width, read from its file; null for anything else and a crawl from before it was read.</param>
/// <param name="PixelHeight">An image's own height, as <paramref name="PixelWidth"/>.</param>
/// <param name="BodyBytes">
/// The body's size once decompressed, where the crawl read or counted it: a page, and a script or stylesheet from
/// 2.1.1 on (<see cref="SizeBytes"/> is the bytes sent). Null for anything else.
/// </param>
/// <param name="IsLegacyImage">A successful response sent as JPEG, PNG, GIF or BMP, which WebP or AVIF would usually make smaller.</param>
/// <param name="IsPrivateHost">
/// Its host is localhost (or a .localhost name) or a private or loopback address written into the URL
/// (<see cref="Fetching.PrivateNetworkGuard.IsPrivateLiteral"/>): an address only the visitor's own machine or network has.
/// </param>
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
    bool InSitemap,
    bool IsMetaRefresh = false,
    bool IsResource = false,
    DiscoverySource Source = DiscoverySource.Link,
    bool IsRateLimited = false,
    int ResponseMs = 0,
    bool IsPrivateHost = false,
    ContentClass ContentClass = ContentClass.None,
    int? PixelWidth = null,
    bool IsLegacyImage = false,
    int? PixelHeight = null,
    long? BodyBytes = null)
{
    /// <summary>Statuses many sites send crawlers but not people (Amazon answers unknown bots with 503, LinkedIn with 999).</summary>
    private static readonly HashSet<int> BOT_REFUSALS = [401, 403, 405, 429, 503, 999];

    /// <summary>An HTML page the crawler fetched and parsed: one of the site's own pages in scope.</summary>
    public bool IsPage => Fetched && Outcome == FetchOutcome.Success && IsHtml && IsParsed;

    public bool IsIndexablePage => IsPage && IndexabilityReason == IndexabilityReason.None;

    /// <summary>
    /// Only the crawler's probe of the site's other versions led here, or where one redirects: nothing on the site links
    /// to it. How it answers is the duplicate-site-version check's to judge, not a broken URL of the site's.
    /// </summary>
    public bool IsSiteVersionProbe => Source == DiscoverySource.SiteVersion;

    /// <summary>
    /// The URL was checked and does not work: 4xx, 5xx, timeout or connection failure. On other sites, a status
    /// that is often only sent to crawlers doesn't count, because the link may well work in a browser.
    /// </summary>
    public bool IsBroken => Fetched && (Outcome is FetchOutcome.Timeout or FetchOutcome.NetworkError ||
                                        (Outcome is FetchOutcome.ClientError or FetchOutcome.ServerError && (IsInternal || !BOT_REFUSALS.Contains(StatusCode))));
}
