using ShoutingIguana.Engine.Reporting.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Export.Models;

/// <summary>
/// What to export from a crawl. A grid's export is the grid's own read: its filters, search and sort, with the columns to
/// write, so the file holds what the grid shows, in its order.
/// </summary>
public sealed record ExportRequest
{
    public required ExportKind Kind { get; init; }

    public required ExportFormat Format { get; init; }

    /// <summary>
    /// For the grids (<see cref="ExportKind.Urls"/>, <see cref="ExportKind.Links"/>, <see cref="ExportKind.SitemapUrls"/>,
    /// <see cref="ExportKind.IssueRows"/>): the grid's query. Its columns are the file's, in the order given (none takes
    /// the grid's own defaults); where a page starts and how many rows it holds are ignored, as an export writes every
    /// row. A URL grid's export (<see cref="ExportKind.Urls"/>) can be narrowed to a links view's pages or targets by
    /// <see cref="UrlQuery.LinkEnds"/>, as the grid can. For <see cref="ExportKind.RobotsOrigins"/> only its
    /// <see cref="UrlQuery.Search"/> is read: the origins whose name contains it.
    /// </summary>
    public UrlQuery Query { get; init; } = new();

    /// <summary>
    /// For <see cref="ExportKind.RobotsOrigins"/>: true for only the origins that blocked at least one URL, false for only
    /// those that blocked none, as the list's filter takes it; every origin when null.
    /// </summary>
    public bool? Blocked { get; init; }

    /// <summary>For <see cref="ExportKind.SitemapUrls"/>: the sitemap.</summary>
    public int? SitemapId { get; init; }

    /// <summary>For <see cref="ExportKind.RobotsBlocked"/>: the origin, as the list of origins names it.</summary>
    public string? Origin { get; init; }

    /// <summary>For <see cref="ExportKind.RobotsBlocked"/>: only the URLs this line of the file refused.</summary>
    public int? Line { get; init; }

    /// <summary>For <see cref="ExportKind.RobotsBlocked"/>: only the URLs refused for this reason.</summary>
    public RobotsBlockReason? Reason { get; init; }

    /// <summary>For <see cref="ExportKind.IssueRows"/> and <see cref="ExportKind.Check"/>: the check.</summary>
    public string? CheckId { get; init; }

    /// <summary>For <see cref="ExportKind.IssueRows"/>: one of the check's groups, as its groups name it.</summary>
    public IssueGroupKey? Group { get; init; }

    /// <summary>For <see cref="ExportKind.Report"/>: the cut, and how many example pages each problem lists.</summary>
    public ReportOptions Report { get; init; } = new();
}
