using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>
/// The proxy to test, with its user name and password if it has them (they're used for this one fetch and kept nowhere),
/// and the address to fetch through it.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ProxyTestRequest
{
    public string? ProxyUrl { get; init; }

    public string? Url { get; init; }
}
