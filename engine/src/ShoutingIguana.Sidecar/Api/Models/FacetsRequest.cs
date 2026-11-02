using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record FacetsRequest
{
    public IReadOnlyList<UrlFilterDto>? Filters { get; init; }
    public string? Search { get; init; }

    /// <summary>The facets to count; all of them when left out.</summary>
    public IReadOnlyList<string>? Facets { get; init; }
}
