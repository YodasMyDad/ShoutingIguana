namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>The URLs a query matches, counted by the values of each facet.</summary>
/// <param name="Unavailable">Facets asked for that need the URL summary, which isn't there (<paramref name="Summary"/>).</param>
/// <param name="Facets">Each facet's values with URLs, in order.</param>
public sealed record UrlFacets(
    long Matching,
    long Total,
    SummaryState Summary,
    IReadOnlyList<string> Unavailable,
    IReadOnlyDictionary<string, IReadOnlyList<FacetBucket>> Facets);
