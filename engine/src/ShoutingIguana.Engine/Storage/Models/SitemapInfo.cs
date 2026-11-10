namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One sitemap a crawl looked for (<see cref="SitemapListReader.Read"/>), stored under the URL it was found at.</summary>
/// <param name="ParentId">The sitemap index that lists it, if one does.</param>
/// <param name="FoundVia">
/// How the crawl came by it, worked out from the stored robots.txt and the crawl's options; null when neither names it any
/// more (robots.txt was read again by a later run, and had changed).
/// </param>
/// <param name="Entries">The URL entries the crawl read in the file, as it counted them, repeats included.</param>
/// <param name="Sitemaps">The sitemaps an index lists that the crawl has.</param>
/// <param name="Error">Why it couldn't be read, or wasn't.</param>
public sealed record SitemapInfo(
    int Id,
    string Url,
    int? ParentId,
    SitemapSource? FoundVia,
    SitemapState State,
    int? StatusCode,
    bool IsIndex,
    int Entries,
    int Sitemaps,
    string? Error,
    SitemapUrlCounts Urls);
