using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>A page of a URL's links: of the kinds named (every kind when left out), from an offset or after a cursor.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record LinksRequest
{
    public IReadOnlyList<string>? Kinds { get; init; }
    public int? Offset { get; init; }
    public int? Limit { get; init; }
    public string? After { get; init; }
}
