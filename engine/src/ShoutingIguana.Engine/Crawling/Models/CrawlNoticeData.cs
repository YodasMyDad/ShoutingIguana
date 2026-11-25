namespace ShoutingIguana.Engine.Crawling.Models;

/// <summary>
/// What a notice is about, for a UI to offer its action. Which facts are set depends on the code:
/// <list type="bullet">
/// <item><c>blocked</c>: Host, Reason (the firewall or challenge), Count (the host's responses blocked: 1 as raised, so far when read from the file), Tried (what the browser fallback did about it), Browser (the browser a person was shown, when one was), Url (where the page went after its check, for Tried <c>wentElsewhere</c>), and, when the crawl slowed down by itself for a host that started refusing it after pages were read, Retried (its refused URLs asked again) and Duration (the longest wait between its pages).</item>
/// <item><c>blocked-slowdown</c>: Host, Reason (the refusal), Count (the URLs refused in a row that slowed it down), Duration (the wait between its pages to start).</item>
/// <item><c>human-check</c>: Host, Url (the page the check is on), Reason (the check), Browser (whose window was shown).</item>
/// <item><c>human-check-passed</c>: Host, Url, Browser, Duration (how long the person took).</item>
/// <item><c>browser-fallback</c>: Host, Reason (the block the plain HTTP fetch got, before the browser got past it), Browser (the computer's own browser, when that is what reads the host).</item>
/// <item><c>rate-limited</c>: Host, Reason (429 Too Many Requests), Duration (the pause it asked for, when it did).</item>
/// <item><c>rate-limited-unchecked</c>: Host (of the first such URL), Reason (429 Too Many Requests), Count (the site's URLs stored as not checked: refused, or never asked).</item>
/// <item><c>shopify-unsigned</c>: Host (the first to refuse an unsigned request), Count (the site's URLs refused so far; said again as it grows), Reason (why the crawl's signature wasn't sent, when it had one: expired, or for the www twin or bare domain; else null).</item>
/// <item><c>shopify-signature-refused</c>: Host, Reason (which problem the signature may have: made under an hour ago, or not active yet, expired or for another domain).</item>
/// <item><c>mostly-forbidden</c>: Host, Count (the 403s), Sampled (the site's first URLs fetched).</item>
/// <item><c>start-redirects-away</c>: Url (where the start URL leads).</item>
/// <item><c>robots-unreachable</c>: Origin, Reason (the status or error), Duration (until robots.txt is asked again).</item>
/// <item><c>site-unreachable</c>: Origin, Reason (the last error), Duration (the pause between tries).</item>
/// <item><c>site-down</c>: Origin, Duration (how long it hadn't answered).</item>
/// <item><c>sitemaps</c>: Count (the sitemaps read), UrlCount (the URLs they list, each once).</item>
/// <item><c>sitemaps-not-read</c>: Url (the sitemap left out), Reason.</item>
/// <item><c>sitemaps-failed</c>: Reason (the error).</item>
/// </list>
/// </summary>
/// <param name="Origin">Scheme, host and port, as robots.txt and reachability go by.</param>
/// <param name="Reason">Why, in the engine's words or the site's.</param>
/// <param name="Browser">What the browser a person was shown is called: "Chrome", "Edge".</param>
/// <param name="Tried">
/// For <c>blocked</c>, what the browser fallback did, as one stable word: <c>off</c> (the crawl has it turned off),
/// <c>needsPerson</c> (the check needs a person and the crawl was not told to ask one), <c>notCompleted</c> (a window was
/// shown and the check was not completed in time), <c>windowClosed</c> (the window was closed first),
/// <c>refusedAfterCheck</c> (the check was completed and the site refused the page all the same), <c>wentElsewhere</c>
/// (the check passed and the page left for another address), <c>noDisplay</c>, <c>noBrowser</c>, <c>ownBrowserOff</c>
/// (the crawl was told not to use the computer's own browser), <c>refused</c> (a real browser was refused too), and
/// <c>notTried</c> (no browser was tried for what was blocked).
/// </param>
/// <param name="Retried">
/// For <c>blocked</c>, set only when the crawl slowed down by itself for the host: how many of its refused URLs it asked
/// again, more slowly, before it recorded them blocked.
/// </param>
public sealed record CrawlNoticeData(
    string? Host = null,
    string? Origin = null,
    string? Url = null,
    string? Reason = null,
    long? Count = null,
    long? Sampled = null,
    long? UrlCount = null,
    TimeSpan? Duration = null,
    string? Browser = null,
    string? Tried = null,
    long? Retried = null);
