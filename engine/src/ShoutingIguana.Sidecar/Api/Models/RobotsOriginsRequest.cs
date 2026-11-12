using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>A page of a crawl's origins with their robots.txt, narrowed by a text in their name and by whether they blocked anything.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record RobotsOriginsRequest
{
    public int? Offset { get; init; }

    public int? Limit { get; init; }

    /// <summary>Only the origins whose name contains this text.</summary>
    public string? Search { get; init; }

    /// <summary>True for only the origins that blocked something, false for only those that blocked nothing.</summary>
    public bool? Blocked { get; init; }
}
