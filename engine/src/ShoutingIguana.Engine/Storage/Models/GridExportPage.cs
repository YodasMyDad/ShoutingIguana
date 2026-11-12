namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One page of a grid's rows, read for an export (<see cref="GridExportReader"/>).</summary>
/// <param name="Columns">What each row's values are: the row's id first, then the columns asked for that have values.</param>
/// <param name="Matching">The rows the query matches, as this page's read counted them.</param>
/// <param name="Next">The cursor for the row after the last one, or null at the end.</param>
/// <param name="Unavailable">Columns asked for that have no values, as the URL summary isn't there.</param>
internal sealed record GridExportPage(
    IReadOnlyList<string> Columns,
    IReadOnlyList<GridExportRow> Rows,
    long Matching,
    string? Next,
    GridExportMoment Moment,
    IReadOnlyList<string> Unavailable);
