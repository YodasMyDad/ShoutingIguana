namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>Every sitemap a crawl looked for, in the order it found them.</summary>
/// <param name="Summary">Whether the URL counts are the analysis's judgement (a URL summary describes the crawl) or the crawl's own.</param>
public sealed record SitemapList(SitemapDiscovery Discovery, IReadOnlyList<SitemapInfo> Sitemaps, SummaryState Summary);
