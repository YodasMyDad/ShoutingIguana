using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>
/// A page of URLs, by id (1 to 100), and how many references of each to list (0 to 50, 5 when left out): where each is
/// found, or, for a check's flagged pages, the URLs each one's issue names.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record FoundOnRequest
{
    public IReadOnlyList<long>? UrlIds { get; init; }
    public int? Limit { get; init; }
}
