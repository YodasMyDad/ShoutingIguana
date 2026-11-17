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
    /// The site asked the crawler to slow down (429, or 503 with Retry-After) or kept failing, so the crawler learned a
    /// slower pace for it, one request at a time, which rises slowly while the site answers well. Listed while it is slower
    /// than the crawl's settings. Host. Duration: the time between requests now (one over the requests a second).
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
    OtherSitesRobotsTxt,

    /// <summary>
    /// The host limits one section apart from the rest (its product pages, say): that section's URLs are read one at a
    /// time, an interval apart learned from its 429s and what got through, while the rest goes on. Host. Section: the
    /// section, "/products/". Urls: how many are left. Duration: the time between them now.
    /// </summary>
    SectionLimited
}
