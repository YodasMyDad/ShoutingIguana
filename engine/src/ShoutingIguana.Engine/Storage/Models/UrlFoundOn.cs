namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>Where a URL is found: the links to it, the pages they're on, the sitemaps listing it.</summary>
/// <param name="Links">Every link to the URL, of every kind (hyperlinks, images, canonicals, hreflang, redirects...), a page's links to itself left out.</param>
/// <param name="Pages">The pages those links are on: exact, or a floor where <paramref name="PagesExact"/> is false.</param>
/// <param name="PagesExact">
/// The pages were counted from every link: false past <see cref="FoundOnReader.PAGES_COUNTED_LINKS"/> links, where
/// <paramref name="Pages"/> is the more of the pages the first of them are on and the pages linking to it by hyperlink.
/// </param>
/// <param name="Sources">The first links to it, in crawl order, each naming the page it's on (<see cref="LinkRow.Id"/>, <see cref="LinkRow.Url"/>) and that page's status.</param>
/// <param name="Sitemaps">The sitemaps that list it.</param>
/// <param name="Start">It's the crawl's start URL, which the person crawling gave rather than found.</param>
public sealed record UrlFoundOn(int UrlId, long Links, long Pages, bool PagesExact, IReadOnlyList<LinkRow> Sources, UrlSitemaps Sitemaps, bool Start);
