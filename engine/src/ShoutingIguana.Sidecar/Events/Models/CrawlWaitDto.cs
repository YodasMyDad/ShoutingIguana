using System.Text.Json.Serialization;
using ShoutingIguana.Engine.Crawling.Models;

namespace ShoutingIguana.Sidecar.Events.Models;

/// <summary>Something holding part of a live crawl up now. Which fields are there depends on the reason.</summary>
/// <param name="Host">The site's host, or for <c>siteDown</c> the origin (scheme, host and port).</param>
/// <param name="Urls">For <c>robotsTxt</c>: the URLs waiting.</param>
/// <param name="DurationMs">
/// <c>crawlDelay</c>: the delay. <c>backoff</c>: the extra spacing now. <c>heldBack</c> and <c>robotsTxtCoolDown</c>: how long
/// until it ends. <c>siteDown</c>: how long it hasn't answered.
/// </param>
internal sealed record CrawlWaitDto(
    CrawlWaitReason Reason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Host,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? Urls,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? DurationMs)
{
    public static CrawlWaitDto From(CrawlWait wait) =>
        new(wait.Reason, wait.Host, wait.Urls, wait.Duration is { } duration ? (long)duration.TotalMilliseconds : null);
}
