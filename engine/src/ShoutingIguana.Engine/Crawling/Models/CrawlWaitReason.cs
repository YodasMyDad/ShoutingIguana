namespace ShoutingIguana.Engine.Crawling.Models;

/// <summary>Why part of a crawl is waiting. Each says which of <see cref="CrawlWait"/>'s fields are set.</summary>
public enum CrawlWaitReason
{
    /// <summary>
    /// The site's URLs wait for their origin's robots.txt to be read, which comes before a host's first fetch. Host: the
    /// site's host whose robots.txt it is. Urls: how many.
    /// </summary>
    RobotsTxt,

    /// <summary>The site's robots.txt asks for a Crawl-delay, which spaces every request to it. Host. Duration: the delay.</summary>
    CrawlDelay,

    /// <summary>
    /// The site asked the crawler to slow down (429, 503, a block page) or keeps failing, so requests are spaced further
    /// apart, easing off by half every half second. Host. Duration: the extra spacing now.
    /// </summary>
    Backoff,

    /// <summary>
    /// Requests to the site are held back: it sent Retry-After, or it stopped answering and is asked again a minute after
    /// each try. Host. Duration: how long until they may go again.
    /// </summary>
    HeldBack,

    /// <summary>
    /// An origin of the site has stopped answering and is waited out; the crawl stops after ten minutes. Host: the origin.
    /// Duration: how long so far.
    /// </summary>
    SiteDown,

    /// <summary>
    /// robots.txt couldn't be read, and something waits for it to be asked again after its cool-down: URLs it refused,
    /// whose last try comes after it (Urls: how many), or the sitemaps it declares. Duration: how long until it is.
    /// </summary>
    RobotsTxtCoolDown,

    /// <summary>
    /// Status checks of links to other sites wait for those sites' robots.txt, which comes before a host's first request.
    /// They hold up nothing of the site's own. Urls: how many.
    /// </summary>
    OtherSitesRobotsTxt
}
