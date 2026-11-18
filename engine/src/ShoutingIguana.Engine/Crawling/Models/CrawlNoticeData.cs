namespace ShoutingIguana.Engine.Crawling.Models;

/// <summary>
/// What a notice is about, for a UI to offer its action. Which facts are set depends on the code:
/// <list type="bullet">
/// <item><c>blocked</c>: Host, Reason (the firewall or challenge), Count (the host's responses blocked: 1 as raised, so far when read from the file).</item>
/// <item><c>rate-limited</c>: Host, Reason (429 Too Many Requests), Duration (the pause it asked for, when it did).</item>
/// <item><c>rate-limited-unchecked</c>: Host (of the first such URL), Reason (429 Too Many Requests), Count (the site's URLs stored as not checked: refused, or never asked).</item>
/// <item><c>mostly-forbidden</c>: Host, Count (the 403s), Sampled (the site's first URLs fetched).</item>
/// <item><c>start-redirects-away</c>: Url (where the start URL leads).</item>
/// <item><c>robots-unreachable</c>: Origin, Reason (the status or error), Duration (until robots.txt is asked again).</item>
/// <item><c>site-unreachable</c>: Origin, Reason (the last error), Duration (the pause between tries).</item>
/// <item><c>site-down</c>: Origin, Duration (how long it hadn't answered).</item>
/// <item><c>sitemaps</c>: Count (the sitemaps read), UrlCount (the URLs they list).</item>
/// <item><c>sitemaps-not-read</c>: Url (the sitemap left out), Reason.</item>
/// <item><c>sitemaps-failed</c>: Reason (the error).</item>
/// </list>
/// </summary>
/// <param name="Origin">Scheme, host and port, as robots.txt and reachability go by.</param>
/// <param name="Reason">Why, in the engine's words or the site's.</param>
public sealed record CrawlNoticeData(
    string? Host = null,
    string? Origin = null,
    string? Url = null,
    string? Reason = null,
    long? Count = null,
    long? Sampled = null,
    long? UrlCount = null,
    TimeSpan? Duration = null);
