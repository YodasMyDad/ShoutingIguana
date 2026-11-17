using System.Text.Json.Serialization;
using ShoutingIguana.Engine.Crawling.Models;

namespace ShoutingIguana.Sidecar.Events.Models;

/// <summary>Something holding part of a live crawl up now. Which fields are there depends on the reason.</summary>
/// <param name="Host">
/// One of the site's hosts, as the engine names them: the start URL's host for it and its www twin, which are one site;
/// another of the site's hosts without a leading www. For <c>siteDown</c>, the origin that is down.
/// </param>
/// <param name="Urls">For <c>robotsTxt</c> and <c>otherSitesRobotsTxt</c>: the URLs waiting. For <c>robotsTxtCoolDown</c>: the URLs it refused, if any.</param>
/// <param name="DurationMs">
/// <c>crawlDelay</c>: the delay. <c>backoff</c>: the time between requests now. <c>heldBack</c> and <c>robotsTxtCoolDown</c>: how
/// long until it ends. <c>siteDown</c>: how long it hasn't answered. <c>sectionLimited</c>: how long until the section's next
/// URL is tried.
/// </param>
/// <param name="Section">For <c>sectionLimited</c>: the section the host limits, "/products/". <c>urls</c> is how many are left.</param>
internal sealed record CrawlWaitDto(
    CrawlWaitReason Reason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Host,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? Urls,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? DurationMs,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Section = null)
{
    public static CrawlWaitDto From(CrawlWait wait) =>
        new(wait.Reason, wait.Host, wait.Urls, wait.Duration is { } duration ? (long)duration.TotalMilliseconds : null, wait.Section);
}
