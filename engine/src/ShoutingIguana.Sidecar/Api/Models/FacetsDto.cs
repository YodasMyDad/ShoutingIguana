using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

internal sealed record FacetsDto(
    long Matching,
    long Total,
    SummaryState Summary,
    IReadOnlyList<string> Unavailable,
    IReadOnlyDictionary<string, IReadOnlyList<FacetBucketDto>> Facets);
