namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// Where a URL grid column's values live, which decides how a filter on it reads: the URL summary's narrow row, or one of
/// the crawl's own tables, through a subquery, so a sort over the summary never joins them.
/// </summary>
internal enum ColumnSource
{
    /// <summary><c>url_summary s</c>, or <c>urls u</c> and <c>responses r</c> without a summary.</summary>
    Summary,

    /// <summary><c>urls u</c>.</summary>
    Urls,

    /// <summary><c>responses r</c>.</summary>
    Responses,

    /// <summary><c>issues i</c>: a check's own columns, on its rows only.</summary>
    Issues,

    /// <summary>The stored page details, decoded per row: shown only.</summary>
    Details
}
