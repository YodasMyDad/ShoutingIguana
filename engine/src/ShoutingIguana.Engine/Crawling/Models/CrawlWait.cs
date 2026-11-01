namespace ShoutingIguana.Engine.Crawling.Models;

/// <summary>Something holding part of a crawl up right now. <see cref="CrawlWaitReason"/> says which fields are set.</summary>
/// <param name="Host">The site's host (an origin, scheme and port included, for <see cref="CrawlWaitReason.SiteDown"/>).</param>
/// <param name="Urls">How many URLs are waiting.</param>
public sealed record CrawlWait(CrawlWaitReason Reason, string? Host = null, long? Urls = null, TimeSpan? Duration = null);
