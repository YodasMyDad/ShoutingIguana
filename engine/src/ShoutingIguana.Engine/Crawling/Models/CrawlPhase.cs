namespace ShoutingIguana.Engine.Crawling.Models;

/// <summary>What a crawl is doing as a whole. Whether it is paused is its status; what holds parts of it up is in its waits.</summary>
public enum CrawlPhase
{
    /// <summary>The file is open and the start URL is being queued.</summary>
    Starting,

    /// <summary>Every URL being worked on waits for its site's robots.txt, as the start URL does before the first fetch.</summary>
    ReadingRobotsTxt,

    Crawling,

    /// <summary>Nothing else is left to fetch until the sitemaps still being read are done.</summary>
    ReadingSitemaps,

    /// <summary>
    /// The site's URLs that got no response (or met an unreadable robots.txt) get their one more try, after robots.txt's
    /// cool-down where it couldn't be read: from when nothing else is left until the last of those tries is over. What
    /// they lead to is fetched meanwhile. Whatever is fetched after that (the URLs only sitemaps list, say) is crawling
    /// again, and URLs that get no response then are tried once more in another round of this.
    /// </summary>
    Retrying,

    /// <summary>Nothing more is fetched: the last requests settle and the file is closed. Nothing is waited for.</summary>
    Finishing
}
