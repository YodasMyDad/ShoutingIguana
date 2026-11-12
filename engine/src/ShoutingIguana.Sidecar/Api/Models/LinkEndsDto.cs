using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>
/// A URL grid narrowed to one end of some links: the pages the links a set of link filters matches are on
/// (<c>from</c>), or the URLs they lead to (<c>to</c>).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record LinkEndsDto
{
    /// <summary><c>from</c> or <c>to</c>.</summary>
    public string? End { get; init; }

    /// <summary>The links grid's filters; none for every link.</summary>
    public IReadOnlyList<UrlFilterDto>? Filters { get; init; }

    /// <summary>The links grid's search.</summary>
    public string? Search { get; init; }
}
