using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>A page of the links a links view matches, grouped by target: the view's filters and search, and the groups' order.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record LinkGroupsRequest
{
    public IReadOnlyList<UrlFilterDto>? Filters { get; init; }

    public string? Search { get; init; }

    /// <summary>One key at most: <c>pages</c> (the default, descending), <c>links</c>, or a column of the target the links grid sorts by.</summary>
    public IReadOnlyList<UrlSortDto>? Sort { get; init; }

    public int? Offset { get; init; }

    public int? Limit { get; init; }

    /// <summary>The checks the user hid: a link filter or a sort on an end's issue counts leaves them out.</summary>
    public IReadOnlyList<string>? HiddenChecks { get; init; }
}
