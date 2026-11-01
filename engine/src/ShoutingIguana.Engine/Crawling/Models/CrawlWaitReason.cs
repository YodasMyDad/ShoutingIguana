namespace ShoutingIguana.Engine.Crawling.Models;

/// <summary>Why part of a crawl is waiting. Each says what <see cref="CrawlWait"/>'s host and duration are.</summary>
public enum CrawlWaitReason
{
    /// <summary>URLs wait for their origin's robots.txt to be read, which comes before a host's first fetch. Urls: how many.</summary>
    RobotsTxt,

    /// <summary>The site's robots.txt asks for a Crawl-delay, which spaces every request to it. Duration: the delay.</summary>
    CrawlDelay,

    /// <summary>
    /// The site asked the crawler to slow down (429, 503, a block page) or keeps failing, so requests are spaced further
    /// apart, easing off by half every half second. Duration: the extra spacing now.
    /// </summary>
    Backoff,

    /// <summary>
    /// Requests to the site are held back: it sent Retry-After, or it stopped answering and is asked again a minute after
    /// each try. Duration: how long until they may go again.
    /// </summary>
    HeldBack,

    /// <summary>An origin of the site has stopped answering and is waited out; the crawl stops after ten minutes. Duration: how long so far.</summary>
    SiteDown,

    /// <summary>
    /// robots.txt couldn't be read, so the URLs it refused wait for it to be asked again before their one more try.
    /// Duration: how long until it is.
    /// </summary>
    RobotsTxtCoolDown
}
