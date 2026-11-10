using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>One origin's robots.txt, the origin in the body: a POST to one fixed URL per crawl, so no preflight per origin.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record RobotsFileRequest
{
    public string? Origin { get; init; }
}
