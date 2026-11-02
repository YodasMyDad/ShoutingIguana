using System.Text.Json.Serialization;
using ShoutingIguana.Engine.Crawling.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>
/// The facts an alert is about, for the UI to offer its action. Which are there depends on the code (see the API
/// document): the host and how many responses were blocked for <c>blocked</c>, the origin for <c>robots-unreachable</c>,
/// the URL to start from for <c>start-redirects-away</c>, and so on.
/// </summary>
/// <param name="Origin">Scheme, host and port.</param>
/// <param name="Count">
/// <c>blocked</c>: the host's responses blocked, 1 on the live event and so far when read from the file.
/// <c>mostly-forbidden</c>: the 403s. <c>sitemaps</c>: the sitemaps read.
/// </param>
/// <param name="Sampled"><c>mostly-forbidden</c>: of how many of the site's first URLs.</param>
/// <param name="UrlCount"><c>sitemaps</c>: the URLs they list.</param>
/// <param name="DurationMs">
/// <c>site-down</c>: how long it hadn't answered. <c>site-unreachable</c>: the pause between tries. <c>robots-unreachable</c>:
/// until robots.txt is asked again.
/// </param>
internal sealed record AlertDataDto(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Host,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Origin,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Url,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? Count,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? Sampled,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? UrlCount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? DurationMs)
{
    public static AlertDataDto From(CrawlNoticeData data) => new(data.Host, data.Origin, data.Url, data.Reason, data.Count, data.Sampled,
        data.UrlCount, data.Duration is { } duration ? (long)duration.TotalMilliseconds : null);
}
