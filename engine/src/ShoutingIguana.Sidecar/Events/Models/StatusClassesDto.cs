using System.Text.Json.Serialization;
using ShoutingIguana.Engine.Crawling.Models;

namespace ShoutingIguana.Sidecar.Events.Models;

/// <summary>Every URL fetched, once, by how it answered: the classes add up to <c>fetched</c>. A block page is blocked whatever its status.</summary>
/// <param name="Other">A status outside 200–599, such as LinkedIn's 999.</param>
/// <param name="NoResponse">No status was recorded: <see cref="NoResponseDto"/> says why.</param>
internal sealed record StatusClassesDto(
    [property: JsonPropertyName("2xx")] long Success,
    [property: JsonPropertyName("3xx")] long Redirect,
    [property: JsonPropertyName("4xx")] long ClientError,
    [property: JsonPropertyName("5xx")] long ServerError,
    long Other,
    long NoResponse,
    long Blocked)
{
    public static StatusClassesDto From(CrawlStatusClasses classes) => new(classes.Success, classes.Redirect, classes.ClientError,
        classes.ServerError, classes.OtherStatus, classes.NoResponse, classes.Blocked);
}
