using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>The images a page shows: the page by its id, and how many of them to list.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record UrlImagesRequest
{
    public long? UrlId { get; init; }

    public int? Limit { get; init; }
}
