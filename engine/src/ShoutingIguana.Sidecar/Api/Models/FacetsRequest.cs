using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record FacetsRequest
{
    public IReadOnlyList<UrlFilterDto>? Filters { get; init; }
    public string? Search { get; init; }

    /// <summary>The facets to count; all of them when left out.</summary>
    public IReadOnlyList<string>? Facets { get; init; }

    /// <summary>For the URL grid's facets (E6): only the URLs at one end of the links these link filters match.</summary>
    public LinkEndsDto? LinkEnds { get; init; }

    /// <summary>
    /// The checks the user hid, by id (round 5 review): the issue counts (<c>issues</c>, <c>errors</c>, <c>warnings</c>,
    /// <c>notices</c>) leave their issues out wherever they're read, sorted or filtered, at either end of a link too.
    /// </summary>
    public IReadOnlyList<string>? HiddenChecks { get; init; }
}
