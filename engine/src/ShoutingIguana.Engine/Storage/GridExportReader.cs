using System.Globalization;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// The grids' rows read on to their end, a page at a time, for an export. Each page is the grid's own read (the same
/// query, compiled and run by the same code, so a file holds what the grid shows, in its order), on a snapshot of its
/// own that is held for that page only: an export of five million links never holds the file, so an analysis or a
/// continue starting here is kept waiting no longer than by a grid, and a running crawl's write-ahead log is never
/// held open for the length of the export. Each page says what state of the crawl it read
/// (<see cref="GridExportMoment"/>), so the export can tell a crawl that changed under it.
/// </summary>
internal static class GridExportReader
{
    /// <summary>
    /// The URLs a page of an export holds: more than a grid's page, as each costs a connection and, for a sort without an
    /// index, a scan. Tests lower it, to read a small crawl in many pages.
    /// </summary>
    internal static int UrlPageRows { get; set; } = 5_000;

    /// <summary>
    /// The links a page of an export holds: the links grid's own largest page, which every way it reads a page was built
    /// and measured for. Tests lower it.
    /// </summary>
    internal static int LinkPageRows { get; set; } = UrlGridReader.MAX_LIMIT;

    /// <summary>What's wrong with a query over the URL grid's columns (or a check's rows'), by field; empty when it can be run.</summary>
    public static Dictionary<string, List<string>> ValidateUrls(UrlQuery query, string? checkId, IssueGroupKey? group)
    {
        var scope = Scope(checkId, group);
        var errors = UrlQueryCompiler.Validate(Unpaged(query), scope);
        if (group is not null && scope.Grouping == IssueGrouping.None)
        {
            errors["group"] = ["This check lists its issues without groups, so there's no group to narrow them to."];
        }

        return errors;
    }

    /// <summary>What's wrong with a query over the links grid's columns, by field; empty when it can be run.</summary>
    public static Dictionary<string, List<string>> ValidateLinks(UrlQuery query) => LinkGridReader.Validate(Unpaged(query));

    /// <summary>
    /// One page of a URL grid: every URL's, one sitemap's, or the rows of a check (one group's). The query is taken as
    /// checked (<see cref="ValidateUrls"/>).
    /// </summary>
    /// <param name="after">The page before's <see cref="GridExportPage.Next"/>; null for the first page.</param>
    /// <param name="began">
    /// The first page's <see cref="GridExportMoment.Stamp"/>; null for the first page. A crawl that has changed since isn't
    /// read on (<see cref="GridExportMissing.CrawlChanged"/>): the cursor is of a crawl that no longer is.
    /// </param>
    /// <inheritdoc cref="UrlGridReader.Query" path="/exception"/>
    public static (GridExportPage? Page, GridExportMissing? Missing) Urls(string path, UrlQuery query, string? checkId, IssueGroupKey? group, int? sitemapId, string? after,
        string? began, ReadCache? cache, CancellationToken cancellationToken)
    {
        var scope = Scope(checkId, group) with { SitemapId = sitemapId };
        var paged = query with { Offset = 0, Limit = UrlPageRows, After = after };
        return UrlGridReader.Refusing(query.Filters, () => GridReadContext.Read<(GridExportPage?, GridExportMissing?)>(path, cache, cancellationToken, context =>
        {
            var moment = Moment(context);
            if (began is not null && moment.Stamp != began)
            {
                return (null, GridExportMissing.CrawlChanged);
            }

            if (checkId is not null)
            {
                if (context.Analysis is null)
                {
                    return (null, GridExportMissing.NotAnalyzed);
                }

                if (context.Scalar("SELECT COUNT(*) FROM issue_types WHERE id = $check", [new("$check", checkId)]) == 0)
                {
                    return (null, GridExportMissing.CheckSkipped);
                }
            }

            if (sitemapId is { } sitemap && context.Scalar("SELECT COUNT(*) FROM sitemaps WHERE id = $id", [new("$id", sitemap)]) == 0)
            {
                return (null, GridExportMissing.SitemapNotFound);
            }

            // A check's rows without an order asked for come as its grid gives them: the URLs that matter most first. Not while
            // a crawl runs, whose counts move between two pages: then in crawl order.
            var ordered = checkId is not null && paged.Sort.Count == 0 && context.Summary.Reason != SummaryUnavailableReason.Crawling
                ? paged with { Sort = IssueGroupReader.ImportanceOrder(IssueCatalog.Find(checkId), context.Summary.Available) }
                : paged;
            var page = UrlGridReader.Read(context, ordered, scope);
            return (new GridExportPage(page.Columns, [.. page.Rows.Select(row => new GridExportRow(row.Id, row.Values))], page.Matching, page.Next, moment, page.Unavailable),
                null);
        }));
    }

    /// <summary>
    /// One page of the links grid; null when the crawl has changed since the first page. The query is taken as checked
    /// (<see cref="ValidateLinks"/>).
    /// </summary>
    /// <inheritdoc cref="Urls" path="/param"/>
    /// <inheritdoc cref="LinkGridReader.Query(string, UrlQuery, ReadCache?, CancellationToken)" path="/exception"/>
    public static GridExportPage? Links(string path, UrlQuery query, string? after, string? began, ReadCache? cache, CancellationToken cancellationToken)
    {
        var paged = query with { Offset = 0, Limit = LinkPageRows, After = after };
        return UrlGridReader.Refusing(query.Filters, () => GridReadContext.Read(path, cache, cancellationToken, context =>
        {
            // The bounds first: a running crawl's page is then of links the crawl already had when they were read.
            var moment = Moment(context);
            if (began is not null && moment.Stamp != began)
            {
                return null;
            }

            var page = LinkGridReader.Query(context, paged);
            return new GridExportPage(page.Columns, [.. page.Rows.Select(row => new GridExportRow(row.Id, row.Values))], page.Matching, page.Next, moment, page.Unavailable);
        }));
    }

    /// <summary>The crawl as this read's snapshot has it.</summary>
    internal static GridExportMoment Moment(GridReadContext context)
    {
        using var command = context.Command("SELECT (SELECT COALESCE(MAX(id), 0) FROM urls), (SELECT COALESCE(MAX(rowid), 0) FROM links)", []);
        using var reader = command.ExecuteReader();
        reader.Read();
        var stamp = context.Stamp!;
        return new GridExportMoment(
            string.Create(CultureInfo.InvariantCulture, $"{stamp.CrawlUpdatedAt}|{stamp.AnalyzedAt}|{stamp.Mode}|{stamp.Live}"),
            stamp.Live,
            reader.GetInt64(0),
            reader.GetInt64(1));
    }

    private static GridScope Scope(string? checkId, IssueGroupKey? group) =>
        checkId is null ? GridScope.AllUrls : new GridScope(checkId, group, IssueCatalog.Find(checkId)?.Grouping ?? IssueGrouping.None);

    /// <summary>The query without where a page starts, which an export sets for itself.</summary>
    private static UrlQuery Unpaged(UrlQuery query) => query with { Offset = 0, Limit = UrlGridReader.DEFAULT_LIMIT, After = null };
}
