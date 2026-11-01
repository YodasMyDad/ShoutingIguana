using System.Text.Json.Serialization;
using ShoutingIguana.Sidecar.Api.Models;

namespace ShoutingIguana.Sidecar.Events.Models;

/// <summary>
/// A crawl's status changed: it started, paused, resumed, is stopping, stopped, completed, failed, is being analysed, or
/// was analysed. The status is the library's, so the UI can set it as it is.
/// </summary>
/// <param name="Error">Why the crawl or its analysis failed.</param>
internal sealed record CrawlEventDto(
    string CrawlId,
    CrawlListStatus Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Error = null);
