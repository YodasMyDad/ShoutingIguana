namespace ShoutingIguana.Engine.Crawling.Models;

/// <summary>Something holding part of a crawl up right now. <see cref="CrawlWaitReason"/> says which fields are set.</summary>
/// <param name="Host">
/// One of the site's hosts, named as <c>CrawlScope.HostName</c> names it: the start URL's host for it and its www twin,
/// which are one. For <see cref="CrawlWaitReason.SiteDown"/>, the origin that is down: its scheme, host and port.
/// </param>
/// <param name="Urls">How many URLs are waiting.</param>
public sealed record CrawlWait(CrawlWaitReason Reason, string? Host = null, long? Urls = null, TimeSpan? Duration = null);
