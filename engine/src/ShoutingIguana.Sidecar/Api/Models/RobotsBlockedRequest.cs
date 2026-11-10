using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>A page of the URLs robots.txt blocked on an origin, narrowed to one line's or one reason's when given.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record RobotsBlockedRequest
{
    public string? Origin { get; init; }

    public int? Line { get; init; }

    /// <summary><c>rule</c>, <c>unreachable</c> or <c>unknown</c>.</summary>
    public string? Reason { get; init; }

    public int? Offset { get; init; }

    public int? Limit { get; init; }
}
