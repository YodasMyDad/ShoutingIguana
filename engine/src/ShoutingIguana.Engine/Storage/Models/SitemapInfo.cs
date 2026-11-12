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
    SitemapUrlCounts Urls)
{
    /// <summary>
    /// How many sitemaps the file lists, as the crawl read it: its <c>&lt;sitemap&gt;</c> entries, repeats and entries that
    /// aren't URLs included, whether or not the crawl went on to read them (<see cref="Sitemaps"/> counts those it has, which
    /// its limits of 1,000 sitemaps and 3 levels can leave far fewer). 0 for a sitemap that lists only URLs. Null when the
    /// file's body was never read (it failed first, was left unread or is still to be read), and for a crawl made by an
    /// engine that didn't keep the count.
    /// </summary>
    public int? ListedSitemaps { get; init; }
}
