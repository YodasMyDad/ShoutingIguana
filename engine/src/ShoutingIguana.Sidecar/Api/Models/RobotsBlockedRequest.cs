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

    /// <summary>Only this URL: its row, with why it was refused, or none when the origin didn't block it.</summary>
    public long? UrlId { get; init; }

    public int? Offset { get; init; }

    public int? Limit { get; init; }
}
