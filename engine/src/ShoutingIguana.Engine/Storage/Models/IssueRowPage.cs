namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One page of the URLs a check flagged, as a URL grid (<see cref="IssueGroupReader.ReadRows"/>).</summary>
/// <param name="Page">Null when the analysis skipped the check: it has no rows, and no total, never 0.</param>
/// <param name="Summary">Whether the URL summary describes the crawl, so the columns that need it have values.</param>
public sealed record IssueRowPage(UrlPage? Page, SummaryState Summary)
{
    /// <summary>
    /// With another crawl to compare with, how far the two can be compared; null without one, and when the other crawl was
    /// never analysed.
    /// </summary>
    public PageComparison? Compared { get; init; }

    /// <summary>
    /// With another crawl to compare with, how each row of <see cref="Page"/> moved since, in its order: null for every
    /// row when the comparison is counts only. Null without one.
    /// </summary>
    public IReadOnlyList<RowChange?>? Changes { get; init; }
}
