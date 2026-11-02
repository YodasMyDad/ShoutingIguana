using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>How the crawl found a URL, and what it did with it.</summary>
/// <param name="Depth">Clicks from the start URL; null for a URL only a sitemap lists.</param>
/// <param name="FoundOn">The URL it was first found on; null for the start URL and the sitemaps' own URLs.</param>
/// <param name="FoundVia">The kind of link it was first found through.</param>
/// <param name="NotFetchedBecause">Why a URL found wasn't fetched, when it was skipped.</param>
public sealed record UrlDiscovery(
    DiscoverySource Source,
    int? Depth,
    UrlRef? FoundOn,
    LinkKind? FoundVia,
    FetchMode FetchMode,
    UrlState State,
    SkipReason? NotFetchedBecause);
