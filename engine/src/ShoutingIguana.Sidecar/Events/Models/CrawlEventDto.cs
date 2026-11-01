using System.Text.Json.Serialization;
using ShoutingIguana.Sidecar.Api.Models;

namespace ShoutingIguana.Sidecar.Events.Models;

/// <summary>
/// A crawl's status changed: it started, paused, resumed, is stopping, stopped, completed, failed, is being analysed, or
/// was analysed. The status is the library's, so the UI can set it as it is.
/// </summary>
/// <param name="Error">Why the crawl or its analysis failed.</param>
/// <param name="ErrorType">
/// The problem type of <paramref name="Error"/> when the UI can act on it: <c>crawl-busy</c> when an analysis found the
/// file busy (someone reading it, or another program writing to it), so trying again can work.
/// </param>
internal sealed record CrawlEventDto(
    string CrawlId,
    CrawlListStatus Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Error = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ErrorType = null);
