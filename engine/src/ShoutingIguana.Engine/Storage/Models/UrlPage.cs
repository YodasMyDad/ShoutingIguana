namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One page of the URL grid.</summary>
/// <param name="Columns">What each row's values are: the URL's id first, then the columns asked for that have values.</param>
/// <param name="Offset">Where the page starts, or null when it was read on from a cursor.</param>
/// <param name="Matching">The URLs the filters and search match.</param>
/// <param name="Total">Every URL in the crawl.</param>
/// <param name="Next">The cursor for the row after the last one, or null at the end.</param>
/// <param name="Unavailable">Columns asked for that have no values, as the URL summary isn't there (<paramref name="Summary"/>).</param>
public sealed record UrlPage(
    IReadOnlyList<string> Columns,
    IReadOnlyList<UrlRow> Rows,
    int? Offset,
    long Matching,
    long Total,
    string? Next,
    SummaryState Summary,
    IReadOnlyList<string> Unavailable);
