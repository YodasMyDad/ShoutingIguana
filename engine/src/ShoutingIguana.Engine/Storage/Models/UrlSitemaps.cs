namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>The sitemaps that list a URL, in the order the crawl found them.</summary>
/// <param name="Total">How many list it.</param>
/// <param name="Sitemaps">The first of them, at most <see cref="UrlInspectorReader.MAX_SITEMAPS_NAMED"/>.</param>
public sealed record UrlSitemaps(long Total, IReadOnlyList<SitemapRef> Sitemaps);
