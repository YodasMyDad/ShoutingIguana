using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// One URL as the grids show it, from the last analysis (<see cref="UrlSummaryReader"/>). Null means unknown: the crawl
/// never fetched the URL, or couldn't ask because its host had stopped answering.
/// </summary>
public sealed record UrlSummary
{
    public required int UrlId { get; init; }

    public required bool IsInternal { get; init; }

    /// <summary>Clicks from the start URL; <see cref="Crawling.CrawlDepth.UNLINKED"/> for a URL only a sitemap lists.</summary>
    public required int Depth { get; init; }

    public required ResourceType ResourceType { get; init; }

    public int? StatusCode { get; init; }

    /// <summary>
    /// Fetched successfully and indexable as search engines will see it: the same judgement the checks make (an instant
    /// meta refresh is a redirect, a canonical that leads back to the page doesn't count).
    /// </summary>
    public bool? IsIndexable { get; init; }

    /// <summary><see cref="IndexabilityReason.None"/> when indexable, otherwise why not.</summary>
    public IndexabilityReason? IndexabilityReason { get; init; }

    /// <summary>Where the URL's redirects end, meta refreshes included: the URL itself when it doesn't redirect.</summary>
    public required int FinalUrlId { get; init; }

    /// <summary>The status at <see cref="FinalUrlId"/>, when that was fetched.</summary>
    public int? FinalStatusCode { get; init; }

    public required bool InSitemap { get; init; }

    /// <summary>Hyperlinks to the URL on the pages audited, each one counted, not counting a page's links to itself.</summary>
    public required int Inlinks { get; init; }

    /// <summary>Of <see cref="Inlinks"/>, the ones search engines follow.</summary>
    public required int FollowedInlinks { get; init; }

    /// <summary>Of <see cref="Inlinks"/>, the nofollow ones: by their rel, or by the robots directives of their page.</summary>
    public required int NofollowInlinks { get; init; }

    /// <summary>The pages <see cref="Inlinks"/> are on, each once.</summary>
    public required int LinkingPages { get; init; }

    /// <summary>Every hyperlink on the page, links to other parts of the page itself included; 0 for a URL that isn't a page.</summary>
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
