using System.Text.Json.Serialization;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>
/// A page of what a check flagged in the other crawl and doesn't flag now. <see cref="Compared"/> is null, with no total
/// and no rows, for a crawl that has no previous crawl to compare with.
/// </summary>
/// <param name="Level"><c>rows</c> or <c>groups</c>.</param>
/// <param name="Total">The rows or causes that match; null when the comparison is counts only, or the check didn't run in the other crawl.</param>
internal sealed record FixedPageDto(string CheckId, ComparedDto? Compared, string Level, long? Total, int Offset)
{
    /// <summary>With level <c>rows</c>: each URL, in the other crawl's order by importance.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ClearedIssue>? Rows { get; init; }

    /// <summary>With level <c>groups</c>: each cause with no URL now, the largest then first.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ClearedGroup>? Groups { get; init; }
}
