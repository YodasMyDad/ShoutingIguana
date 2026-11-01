using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// One URL as the grids show it, from the last analysis (<see cref="UrlSummaryReader"/>). Null means unknown: the crawl
/// never fetched the URL, or couldn't ask because its host had stopped answering, never read what decides the value (a
/// page that was only status-checked), or couldn't tell (a sitemap left unread).
/// </summary>
public sealed record UrlSummary
{
    public required int UrlId { get; init; }

    public required bool IsInternal { get; init; }

    /// <summary>
    /// How the crawl found the URL. <see cref="DiscoverySource.SiteVersion"/> is the crawler's probe of the site's other
    /// versions (www or not, http or https), or where one redirects: nothing on the site leads there, so the grids hide
    /// or label it, and the checks and the stats leave its answer to the duplicate-site-version check.
    /// </summary>
    public required DiscoverySource Source { get; init; }

    /// <summary>Clicks from the start URL; null for a URL only a sitemap lists, which no link leads to.</summary>
    public int? Depth { get; init; }

    public required ResourceType ResourceType { get; init; }

    public int? StatusCode { get; init; }

    /// <summary>
    /// Fetched successfully and indexable as search engines will see it: the same judgement the checks make (an instant
    /// meta refresh is a redirect, a canonical that leads back to the page doesn't count). Null for a page whose HTML was
    /// never read, being only status-checked (outside the start folder, in an iframe, on another site), unless its
    /// response alone rules it out (an X-Robots-Tag noindex, an error, a redirect).
    /// </summary>
    public bool? IsIndexable { get; init; }

    /// <summary><see cref="IndexabilityReason.None"/> when indexable, otherwise why not.</summary>
    public IndexabilityReason? IndexabilityReason { get; init; }

    /// <summary>Where the URL's redirects end, meta refreshes included: the URL itself when it doesn't redirect.</summary>
    public required int FinalUrlId { get; init; }

    /// <summary>The status at <see cref="FinalUrlId"/>, when that was fetched.</summary>
    public int? FinalStatusCode { get; init; }

    /// <summary>
    /// Listed in a sitemap. False only when every sitemap was read, as the not-in-sitemap check needs; null when sitemaps
    /// weren't read, or not all of them, so a URL none of those read lists may still be in one.
    /// </summary>
    public bool? InSitemap { get; init; }

    /// <summary>Hyperlinks to the URL on the pages audited, each one counted, not counting a page's links to itself.</summary>
    public required int Inlinks { get; init; }

    /// <summary>Of <see cref="Inlinks"/>, the ones search engines follow.</summary>
    public required int FollowedInlinks { get; init; }

    /// <summary>Of <see cref="Inlinks"/>, the nofollow ones: by their rel, or by the robots directives of their page.</summary>
    public required int NofollowInlinks { get; init; }

    /// <summary>The pages <see cref="Inlinks"/> are on, each once.</summary>
    public required int LinkingPages { get; init; }

    /// <summary>
    /// Every hyperlink on the page, links to other parts of the page itself included; 0 for a page without any, null for a
    /// URL whose HTML wasn't read (not a page, or only status-checked).
    /// </summary>
    public int? Outlinks { get; init; }

    /// <summary>The checks that flagged the URL.</summary>
    public required int Issues { get; init; }

    public required int Errors { get; init; }

    public required int Warnings { get; init; }

    public required int Notices { get; init; }

    /// <summary>Bytes on the wire.</summary>
    public long? SizeBytes { get; init; }

    /// <summary>Null as well when no request was made, robots.txt having refused it.</summary>
    public int? ResponseMs { get; init; }

    /// <summary>Main-content words, for pages whose HTML was parsed.</summary>
    public int? WordCount { get; init; }
}
