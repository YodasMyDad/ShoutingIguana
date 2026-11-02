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
    IReadOnlyList<string> Unavailable);
