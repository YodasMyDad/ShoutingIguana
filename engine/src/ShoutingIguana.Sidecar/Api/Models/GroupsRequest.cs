using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record GroupsRequest
{
    public int? Offset { get; init; }
    public int? Limit { get; init; }
}
