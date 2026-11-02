using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>
/// One URL of a crawl, by its id, for the inspector and the source view: a POST to one fixed URL per crawl, so inspecting
/// URL after URL costs no CORS preflight each.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record UrlRequest
{
    public long? UrlId { get; init; }
}
