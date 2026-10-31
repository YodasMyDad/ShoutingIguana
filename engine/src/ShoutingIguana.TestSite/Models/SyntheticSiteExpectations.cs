namespace ShoutingIguana.TestSite.Models;

/// <summary>What a correct crawl of a <see cref="SyntheticSite"/> must find.</summary>
/// <param name="HtmlPages">Internal 200 HTML pages: home, products, categories, static pages and sitemap-only orphans.</param>
/// <param name="Redirects">Distinct redirecting URLs (single hops and both hops of each chain).</param>
/// <param name="RobotsDisallowed">Distinct URLs blocked by robots.txt.</param>
/// <param name="NofollowOnly">Distinct URLs reachable only through nofollow links, so never fetched.</param>
/// <param name="DuplicateTitlePages">Pages whose title is shared with another page.</param>
public sealed record SyntheticSiteExpectations(
    int HtmlPages,
    int Products,
    int Orphans,
    int BrokenUrls,
    int ServerErrorUrls,
    int Redirects,
    int RobotsDisallowed,
    int NofollowOnly,
    int ExternalOk,
    int ExternalMissing,
    int Images,
    int Noindex,
    int Canonicalised,
    int DuplicateTitlePages,
    int MissingDescriptions,
    int ImagesMissingAlt);
