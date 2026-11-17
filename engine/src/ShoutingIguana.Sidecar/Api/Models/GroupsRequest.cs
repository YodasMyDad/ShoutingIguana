using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record GroupsRequest
{
    public int? Offset { get; init; }
    public int? Limit { get; init; }

    /// <summary>The groups' order (evidence): <c>pages</c>, the largest first, or <c>size</c>, the largest file first. The check's own when left out.</summary>
    public string? Sort { get; init; }
}
