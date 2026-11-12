namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>The URLs to count by value (<see cref="UrlGridReader.Facets"/>): as a grid's filters and search select them.</summary>
public sealed record UrlFacetQuery
{
    public IReadOnlyList<UrlFilter> Filters { get; init; } = [];

    public string? Search { get; init; }

    /// <summary>Only the URLs at one end of the links these link filters match (<see cref="UrlQuery.LinkEnds"/>).</summary>
    public UrlLinkEnds? LinkEnds { get; init; }

    /// <summary>Facet ids from <see cref="UrlGridReader.FACETS"/>; none means all of them.</summary>
    public IReadOnlyList<string> Facets { get; init; } = [];
}
