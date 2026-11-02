namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// One URL grid column: its type, where it lives, and its SQL in each mode. Summary SQL reads <c>url_summary s</c> (with
/// <c>urls u</c> for the URL, and <c>issues i</c> for a check's columns); crawl SQL reads <c>urls u</c>,
/// <c>responses r</c> and <c>effective_indexability e</c>, so a crawl without a URL summary still has it.
/// </summary>
internal sealed record UrlColumnDefinition(string Id, UrlColumnType Type)
{
    public ColumnSource Source { get; init; } = ColumnSource.Summary;

    /// <summary>SQL with a URL summary; the crawl SQL serves when null.</summary>
    public string? SummarySql { get; init; }

    /// <summary>SQL without one; null when the column needs the summary.</summary>
    public string? CrawlSql { get; init; }

    /// <summary>For a column whose value is another URL's text: that URL's id, which a filter matches through <c>urls</c>.</summary>
    public string? UrlIdSql { get; init; }

    /// <summary>The URL summary index its filters and sort can walk.</summary>
    public bool Indexed { get; init; }

    public bool Nullable { get; init; } = true;

    public bool Sortable { get; init; } = true;

    public bool Filterable { get; init; } = true;

    /// <summary>An enum's names, by the number its SQL gives.</summary>
    public IReadOnlyList<string> Values { get; init; } = [];

    /// <summary>A check's own column, on its rows only.</summary>
    public bool IsIssueColumn => Source == ColumnSource.Issues;

    /// <summary>It reads the URL summary, so has no values without one.</summary>
    public bool NeedsAnalysis => CrawlSql is null && Source != ColumnSource.Details;

    /// <summary>The SQL that gives its value in the mode asked for, or null when it has none there.</summary>
    public string? Sql(GridMode mode) => mode == GridMode.Summary ? SummarySql ?? CrawlSql : CrawlSql;
}
