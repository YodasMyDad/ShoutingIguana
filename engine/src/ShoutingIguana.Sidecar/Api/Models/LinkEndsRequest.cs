using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>A page of the URLs at one end of the links a links view matches: the view's filters and search, and which end.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record LinkEndsRequest
{
    /// <summary><c>from</c> or <c>to</c>.</summary>
    public string? End { get; init; }

    public IReadOnlyList<UrlFilterDto>? Filters { get; init; }

    public string? Search { get; init; }

    public int? Offset { get; init; }

    public int? Limit { get; init; }
}
