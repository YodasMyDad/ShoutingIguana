using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <param name="Value">An enum's name, a bucket's id, a number, a boolean, or null for unknown.</param>
/// <param name="Filter">Added to the query's filters, it gives a grid of exactly <paramref name="Count"/> rows.</param>
internal sealed record FacetBucketDto(JsonElement Value, long Count, UrlFilterDto Filter)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Min { get; init; }

    /// <summary>A range bucket's highest value: left out for the last, which has none.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Max { get; init; }
}
