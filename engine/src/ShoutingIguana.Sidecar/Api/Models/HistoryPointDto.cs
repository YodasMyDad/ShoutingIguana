using System.Text.Json.Serialization;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>
/// A crawl's numbers at one moment. A point taken live has them all; one counted back from the crawl file has what the
/// file can tell the time of, the fetches and the site's own URLs found, and leaves the rest out.
/// </summary>
/// <param name="InternalFound">The site's own URLs found, as the progress event's <c>internalFound</c>: the chart's "found" line.</param>
/// <param name="ElapsedMs">Live points: the time spent in the run they belong to.</param>
/// <param name="Discovered">Live points: found so far, as the progress event's <c>discovered</c>.</param>
internal sealed record HistoryPointDto(
    DateTime At,
    long Fetched,
    long HtmlPages,
    long InternalFetched,
    long InternalFound,
    long Errors,
    long Blocked,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ElapsedMs = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? Discovered = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? Queued = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? Skipped = null)
{
    public static HistoryPointDto Live(DateTime at, CrawlProgress progress) => new(at, progress.Fetched, progress.HtmlPages,
        progress.InternalFetched, progress.InternalFound, progress.Errors, progress.Blocked, (long)progress.Elapsed.TotalMilliseconds,
        progress.Discovered, progress.Queued, progress.Skipped);

    public static HistoryPointDto FromFile(CrawlHistoryPoint point) =>
        new(point.At.UtcDateTime, point.Fetched, point.HtmlPages, point.InternalFetched, point.InternalFound, point.Errors, point.Blocked);
}
