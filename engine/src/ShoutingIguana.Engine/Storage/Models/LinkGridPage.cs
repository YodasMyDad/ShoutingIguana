namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One page of the links grid (<see cref="LinkGridReader.Query"/>).</summary>
/// <param name="Columns">What each row's values are: the link's id first, then the columns asked for that have values.</param>
/// <param name="Offset">Where the page starts, or null when it was read on from a cursor.</param>
/// <param name="Matching">The links the filters and search match.</param>
/// <param name="Total">Every link in the crawl.</param>
/// <param name="Next">The cursor for the link after the last one, or null at the end.</param>
/// <param name="Unavailable">Columns asked for that have no values, as the URL summary isn't there (<paramref name="Summary"/>).</param>
public sealed record LinkGridPage(
    IReadOnlyList<string> Columns,
    IReadOnlyList<LinkGridRow> Rows,
    int? Offset,
    long Matching,
    long Total,
    string? Next,
    SummaryState Summary,
    IReadOnlyList<string> Unavailable)
{
    /// <summary>
    /// Whether the crawl measured images' pixel sizes (<see cref="CrawlSchema.PIXEL_COLUMNS"/>): false for a crawl made before
    /// they were read, and not continued since, whose images' widths and heights are all unknown.
    /// </summary>
    public bool PixelSizes { get; init; } = true;
}
