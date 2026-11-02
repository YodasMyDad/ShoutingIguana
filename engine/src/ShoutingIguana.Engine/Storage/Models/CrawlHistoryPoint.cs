namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>How many URLs a crawl had fetched, and found, by <paramref name="At"/>, as its file can count them back.</summary>
/// <param name="HtmlPages">Pages, as every count of them goes by (<see cref="CrawlSchema.PAGE"/>).</param>
/// <param name="InternalFetched">The site's own URLs: internal, not the probes of its other versions.</param>
/// <param name="Errors">4xx, 5xx, timeouts, network and processing errors, as a crawl's progress counts them.</param>
/// <param name="InternalFound">
/// The site's own URLs found and not skipped, as a crawl's progress counts them: each from when the page that first linked
/// it was fetched; the start URL and those only sitemaps list from the crawl's start.
/// </param>
public sealed record CrawlHistoryPoint(DateTimeOffset At, long Fetched, long HtmlPages, long InternalFetched, long Errors, long Blocked, long InternalFound);
