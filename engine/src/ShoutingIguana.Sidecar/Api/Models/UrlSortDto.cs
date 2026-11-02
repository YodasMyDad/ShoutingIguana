using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record UrlSortDto
{
    public string? Column { get; init; }
    public bool Desc { get; init; }
}
