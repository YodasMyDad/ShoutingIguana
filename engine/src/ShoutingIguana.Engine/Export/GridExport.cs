using System.Globalization;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Export.Models;
using ShoutingIguana.Engine.Export.Tables;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Export;

/// <summary>
/// A grid's rows written to a table: the URL grid, the links grid, a sitemap's URLs, a check's rows, an origin's blocked
/// URLs. The columns are the ones asked for, in the order asked, under the engine's own titles for them
/// (<see cref="UrlColumnInfo.Title"/>, with the unit a number is in); a value is written as a grid's cell says it: a
/// number as a number, an enum's value and a true or false in words, nothing where it's unknown.
/// </summary>
internal static class GridExport
{
    /// <summary>A sort by one of these holds while a crawl runs: a URL keeps its place in crawl order and its text, whatever else changes.</summary>
    private static readonly HashSet<string> STEADY_SORTS = new(StringComparer.Ordinal) { "id", "url" };

    private static readonly Dictionary<string, UrlColumnInfo> URL_COLUMNS = UrlGridReader.IssueRowColumns.ToDictionary(column => column.Id, StringComparer.Ordinal);

    private static readonly Dictionary<string, UrlColumnInfo> LINK_COLUMNS = LinkGridReader.Columns.ToDictionary(column => column.Id, StringComparer.Ordinal);

    /// <summary>What a grid's file writes when no columns are asked for: the grid's own.</summary>
    private static readonly string[] DEFAULT_URL_COLUMNS = ["url"];

    /// <summary>A check's rows without columns asked for: the URL, what was found, the check's evidence, then the other URL.</summary>
    private static string[] IssueColumns(string checkId) =>
        ["url", "detail", .. IssueCatalog.Find(checkId)?.Evidence.Where(column => column is not ("url" or "detail" or "relatedUrl")) ?? [], "relatedUrl"];

    private static readonly string[] DEFAULT_LINK_COLUMNS = ["from.url", "to.url", "kind", "text"];

    public static void Write(ExportRun run, ITableWriter table)
    {
        var request = run.Request;
        switch (request.Kind)
        {
            case ExportKind.Urls:
                WriteGrid(run, table, "URLs", Columns(request.Query.Columns, DEFAULT_URL_COLUMNS, URL_COLUMNS), links: false, checkId: null, sitemapId: null);
                break;
            case ExportKind.SitemapUrls:
                WriteGrid(run, table, "Sitemap URLs", Columns(request.Query.Columns, DEFAULT_URL_COLUMNS, URL_COLUMNS), links: false, checkId: null, request.SitemapId);
                break;
            case ExportKind.IssueRows:
                WriteGrid(run, table, IssueCatalog.Find(request.CheckId!)!.Title, Columns(request.Query.Columns, IssueColumns(request.CheckId!), URL_COLUMNS), links: false,
                    request.CheckId, sitemapId: null, extra: WhereFoundColumns.For(run, request.CheckId));
                break;
            case ExportKind.Links:
                WriteGrid(run, table, "Links", Columns(request.Query.Columns, DEFAULT_LINK_COLUMNS, LINK_COLUMNS), links: true, checkId: null, sitemapId: null);
                break;
            case ExportKind.RobotsBlocked:
                WriteBlocked(run, table);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(run), request.Kind, "Not a grid.");
        }
    }

    /// <summary>
    /// Every row of a grid's query, a page at a time, each page read on from the one before by the grid's own cursor. An
    /// export of several grids (the workbook's sheets) holds them all to the crawl as its first page found it.
    /// </summary>
    /// <param name="columns">The columns written, which the query is asked for.</param>
    /// <param name="titles">The header's words, where they aren't the columns' own.</param>
    /// <param name="extra">Columns after the grid's own, read a page of rows at a time (where a check's URLs are found).</param>
    public static void WriteGrid(ExportRun run, ITableWriter table, string name, IReadOnlyList<UrlColumnInfo> columns, bool links, string? checkId, int? sitemapId,
        IReadOnlyList<string>? titles = null, IssueGroupKey? group = null, UrlQuery? query = null, ExportExtraColumns? extra = null)
    {
        query = (query ?? run.Request.Query) with { Columns = [.. columns.Select(column => column.Id).Distinct(StringComparer.Ordinal)] };
        group ??= run.Request.Kind == ExportKind.IssueRows ? run.Request.Group : null;
        var cells = new object?[columns.Count + (extra?.Titles.Count ?? 0)];
        var places = new int[columns.Count];
        var begun = false;
        string? after = null;
        do
        {
            run.Token.ThrowIfCancellationRequested();
            var page = Page(run, query, links, checkId, group, sitemapId, after, run.Began?.Stamp);
            var began = run.Began ??= page.Moment;
            if (!begun)
            {
                begun = true;
                Begin(run, table, name, [.. titles ?? [.. columns.Select(Title)], .. extra?.Titles ?? []], page, query);
            }

            for (var index = 0; index < columns.Count; index++)
            {
                places[index] = IndexOf(page.Columns, columns[index].Id);
            }

            // What a running crawl found after the export began is no part of it.
            var bound = !began.Live ? long.MaxValue : links ? began.MaxLinkId : began.MaxUrlId;
            long written = 0;
            var added = Extra(extra, [.. page.Rows.Where(row => row.Id <= bound).Select(row => (int)row.Id)]);
            foreach (var row in page.Rows)
            {
                if (row.Id > bound)
                {
                    continue;
                }

                for (var index = 0; index < columns.Count; index++)
                {
                    cells[index] = places[index] < 0 ? null : Cell(columns[index], row.Values[places[index]]);
                }

                if (extra is not null)
                {
                    var cellsOf = added.GetValueOrDefault((int)row.Id);
                    for (var index = 0; index < extra.Titles.Count; index++)
                    {
                        cells[columns.Count + index] = cellsOf?[index];
                    }
                }

                table.WriteRow(cells);
                written++;
            }

            run.Wrote(written);
            after = page.Next;
        }
        while (after is not null);

        table.End();
    }

    /// <summary>The added cells of a page's rows, read as many URLs at a time as the reader takes.</summary>
    private static Dictionary<int, object?[]> Extra(ExportExtraColumns? extra, IReadOnlyList<int> ids)
    {
        Dictionary<int, object?[]> cells = [];
        if (extra is null)
        {
            return cells;
        }

        foreach (var chunk in ids.Chunk(FoundOnReader.MAX_URLS))
        {
            foreach (var (id, row) in extra.Read(chunk))
            {
                cells[id] = row;
            }
        }

        return cells;
    }

    /// <summary>What an export of what an analysis found says of a crawl that has none.</summary>
    public const string NOT_ANALYZED = "This crawl hasn't been analysed yet, so there's nothing of this kind to export.";

    /// <summary>
    /// What an export that found its crawl changed says. It names what ends one: a crawl paused or resumed under an
    /// export is the same crawl still, and the export goes on.
    /// </summary>
    public const string CRAWL_CHANGED =
        "The crawl changed while it was being exported: it was analysed again, continued or it finished. Export it again.";

    /// <summary>A column's header: its title, and the unit its numbers are in.</summary>
    public static string Title(UrlColumnInfo column) => column.Unit is null ? column.Title : $"{column.Title} ({column.Unit})";

    /// <summary>A value as a file's cell says it: an enum's value and a true or false in the grid's words, everything else as it is.</summary>
    public static object? Cell(UrlColumnInfo column, object? value) => value switch
    {
        null => null,
        bool flag => flag ? ColumnWords.YES : ColumnWords.NO,
        string text when column.Type == UrlColumnType.Enum => column.ValueTitles.GetValueOrDefault(text, text),
        int whole => (long)whole,
        RedirectChain or RobotsRefusal => UrlComputedColumns.Text(value),
        _ => value
    };

    /// <summary>The first page is read: the export knows the crawl it's of, and how many rows to expect, and the table begins.</summary>
    private static void Begin(ExportRun run, ITableWriter table, string name, IReadOnlyList<string> titles, GridExportPage page, UrlQuery query)
    {
        if (page.Moment.Live && query.Sort.FirstOrDefault(key => !STEADY_SORTS.Contains(key.Column)) is { } moving)
        {
            throw UrlQueryException.For("sort",
                $"This crawl is still running, so its rows can be exported in crawl order or by URL only: '{moving.Column}' changes as it runs. Remove the sort, or export once the crawl has finished.");
        }

        if (run.Request.Format == ExportFormat.Xlsx && page.Matching > run.Settings.MaxXlsxRows)
        {
            throw new ExportException(ExportFailure.TooLarge, string.Create(CultureInfo.InvariantCulture,
                $"These {page.Matching:N0} rows are more than an Excel file can hold here ({run.Settings.MaxXlsxRows:N0}). Export them as CSV, which has no limit, or narrow them with a filter."));
        }

        run.CrawlWasRunning = page.Moment.Live;
        run.Unavailable = page.Unavailable;
        if (run.Expected is null)
        {
            run.Expect(page.Matching);
        }

        table.Begin(name, titles);
    }

    /// <summary>
    /// The next page of a grid, read on from the page before's cursor. A crawl that has changed since the first page ends
    /// the export: by its stamp, or by the cursor itself, which the grid refuses once its row has gone or changed.
    /// </summary>
    private static GridExportPage Page(ExportRun run, UrlQuery query, bool links, string? checkId, IssueGroupKey? group, int? sitemapId, string? after, string? began)
    {
        try
        {
            if (links)
            {
                return GridExportReader.Links(run.CrawlPath, query, after, began, run.Settings.Cache, run.Token) ?? throw new ExportException(ExportFailure.CrawlChanged, CRAWL_CHANGED);
            }

            var (page, missing) = GridExportReader.Urls(run.CrawlPath, query, checkId, group, sitemapId, after, began, run.Settings.Cache, run.Token);
            return page ?? throw (missing switch
            {
                GridExportMissing.CrawlChanged => new ExportException(ExportFailure.CrawlChanged, CRAWL_CHANGED),
                GridExportMissing.NotAnalyzed => new ExportException(ExportFailure.NotAnalyzed, NOT_ANALYZED),
                GridExportMissing.SitemapNotFound => new ExportException(ExportFailure.SitemapNotFound, "This crawl has no sitemap with that id."),
                _ => new ExportException(ExportFailure.CheckSkipped, "This check didn't run in the last analysis, so it has no rows to export.")
            });
        }
        catch (UrlQueryException exception) when (after is not null && exception.Errors.Keys.All(field => field == "after"))
        {
            throw new ExportException(ExportFailure.CrawlChanged, CRAWL_CHANGED, exception);
        }
    }

    /// <summary>
    /// An origin's blocked URLs, in crawl order, with why each was refused and what blocking it costs (the pages linking to
    /// it, the first sitemap listing it, a menu linking to it, and whether that conflicts), as its list shows them; empty
    /// where those aren't known, a crawl changed since its analysis. The list is worked out once per origin and
    /// kept, so it's read by its places; a running crawl adds to its end, and the export stops where the list ended when
    /// it began.
    /// </summary>
    private static void WriteBlocked(ExportRun run, ITableWriter table)
    {
        var request = run.Request;
        var cells = new object?[BLOCKED_TITLES.Length];
        long? expected = null;
        var offset = 0;
        while (expected is null || offset < expected)
        {
            run.Token.ThrowIfCancellationRequested();
            var page = RobotsReader.ReadBlocked(run.CrawlPath, request.Origin!, request.Line, request.Reason, offset, UrlGridReader.MAX_LIMIT, run.Settings.Cache, run.Token,
                           conflict: request.Conflict)
                       ?? throw new ExportException(ExportFailure.OriginNotFound, "This crawl has no robots.txt for that origin.");
            if (expected is null)
            {
                expected = page.Matching;
                if (request.Format == ExportFormat.Xlsx && expected > run.Settings.MaxXlsxRows)
                {
                    throw new ExportException(ExportFailure.TooLarge, XlsxTableWriter.TooLarge(CrawlExporter.MAX_SHEETS));
                }

                run.Expect(page.Matching);
                table.Begin("Blocked URLs", BLOCKED_TITLES);
            }

            if (page.Rows.Count == 0)
            {
                break;
            }

            var take = (int)Math.Min(page.Rows.Count, expected.Value - offset);
            foreach (var url in page.Rows.Take(take))
            {
                cells[0] = url.Url;
                cells[1] = url.Reason switch
                {
                    RobotsBlockReason.Rule => "A rule",
                    RobotsBlockReason.Unreachable => "robots.txt couldn’t be read",
                    _ => "No rule kept"
                };
                cells[2] = url.Line is { } line ? (long)line : null;
                cells[3] = url.Rule;
                cells[4] = url.Error;
                cells[5] = url.LinkingPages;
                cells[6] = YesOrNo(url.InSitemap);
                cells[7] = url.Sitemap?.Url;
                cells[8] = YesOrNo(url.InMenu);
                cells[9] = RobotsOriginWords.Conflict(url);
                table.WriteRow(cells);
            }

            offset += take;
            run.Wrote(take);
        }

        table.End();
    }

    /// <summary>An origin's blocked URLs' columns, under the list's titles.</summary>
    private static readonly string[] BLOCKED_TITLES =
        ["Blocked URL", "Blocked by", "Line", "Rule", "Error", "Linked from (pages)", "In a sitemap", "Sitemap", "In a menu", "Conflict"];

    private static string? YesOrNo(bool? value) => value is { } known ? known ? ColumnWords.YES : ColumnWords.NO : null;

    /// <summary>The columns to write: those asked for, or the grid's own, each as the grid describes it.</summary>
    private static List<UrlColumnInfo> Columns(IReadOnlyList<string> asked, string[] defaults, Dictionary<string, UrlColumnInfo> known) =>
        [.. (asked.Count == 0 ? defaults : asked).Select(id => known[id])];

    private static int IndexOf(IReadOnlyList<string> columns, string id)
    {
        for (var index = 0; index < columns.Count; index++)
        {
            if (columns[index] == id)
            {
                return index;
            }
        }

        return -1;
    }
}
