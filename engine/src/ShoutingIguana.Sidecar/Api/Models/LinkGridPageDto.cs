using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>One page of the links grid: the URL grid's page, of links.</summary>
internal sealed record LinkGridPageDto(
    IReadOnlyList<string> Columns,
    LinkRowsDto Rows,
    int? Offset,
    long Matching,
    long Total,
    string? Next,
    SummaryState Summary,
    IReadOnlyList<string> Unavailable)
{
    /// <summary>Whether the crawl measured images' pixel sizes: false for one made before they were read (step 3b).</summary>
    public bool PixelSizes { get; init; } = true;
}
