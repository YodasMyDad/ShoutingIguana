using System.Text.Json.Serialization;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>One page of a grid. For a check the analysis skipped, there are no rows, and the counts are null, never 0.</summary>
internal sealed record UrlPageDto(
    IReadOnlyList<string> Columns,
    UrlRowsDto Rows,
    int? Offset,
    long? Matching,
    long? Total,
    string? Next,
    SummaryState Summary,
    IReadOnlyList<string> Unavailable)
{
    /// <summary>With <c>compareWith</c> on a check's rows (the fix loop): what they were compared with, each row then having its <c>change</c>. Left out without one.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ComparedDto? Compared { get; init; }
}
