namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>What became of a sitemap the crawl looked for.</summary>
public enum SitemapState
{
    Read,

    /// <summary>It couldn't be read, or not to its end; the URLs read before the failure are still listed.</summary>
    Failed,

    /// <summary>Left unread at the crawler's limits (how many sitemaps, how deep an index); there may be more like it.</summary>
    NotRead,

    /// <summary>Found, and not read yet: the crawl is running, or stopped first.</summary>
    Pending,

    /// <summary>
    /// The crawl read it and counted the URLs it lists, and the file holds none of them any more: a recheck by an earlier
    /// version of the engine deleted its entries, then failed (18 November 2026). What it lists isn't known; crawling the
    /// site again restores it.
    /// </summary>
    ListLost
}
