using System.IO.Compression;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Export.Models;
using ShoutingIguana.Engine.Export.Tables;
using ShoutingIguana.Engine.Export.Xlsx;
using ShoutingIguana.Engine.Reporting;
using ShoutingIguana.Engine.Reporting.Models;
using ShoutingIguana.Engine.Storage;

namespace ShoutingIguana.Engine.Export;

/// <summary>
/// Writes what a crawl holds to a file: what a grid shows, as CSV or as an Excel workbook; the workbook of everything;
/// and the report. An export only reads the crawl, a page of rows at a time, each page on a snapshot of its own
/// (<see cref="GridExportReader"/>), so its memory is a page's whatever the crawl's size, it never keeps an analysis or a
/// continue out of the file, and it can be stopped between any two pages. The file is written beside where it's wanted
/// and moved into place once whole (<see cref="ExportTarget"/>): an export that's cancelled or fails leaves nothing.
/// <para>
/// <b>A crawl that's running</b> is exported as it stood when the export began: nothing the crawl finds later is
/// written. Each row is read, and tested against the filters, as its page is read, so a URL shows the status it had
/// then. Its rows are in crawl order, or by URL: a sort by anything a running crawl changes (a status, a count) is
/// refused, as rows would change places between two pages.
/// </para>
/// <para>
/// <b>A crawl that changes under an export</b> (analysed again, continued or finished) ends it with
/// <see cref="ExportFailure.CrawlChanged"/> and no file: its pages would be of two different crawls. A pause changes
/// nothing of the crawl, and doesn't.
/// </para>
/// </summary>
public static class CrawlExporter
{
    /// <summary>
    /// The sheets a grid's rows may fill in a workbook. A sheet holds 1,048,576 rows, so this is 16.7 million rows of
    /// data: more than that is refused before anything is written (<see cref="ExportFailure.TooLarge"/>), as CSV has no
    /// limit and a workbook that size opens in nothing.
    /// </summary>
    public const int MAX_SHEETS = 16;

    /// <summary>The rows of data an Excel export holds at most: every sheet full, under its header row.</summary>
    public const long MAX_XLSX_ROWS = (long)MAX_SHEETS * (XlsxWriter.MAX_ROWS - 1);

    /// <summary>
    /// Checks what can be checked of an export without the crawl: that the kind can be written in the format, that it has
    /// what it needs, and that a grid's query is one the grid would run.
    /// </summary>
    /// <exception cref="ExportException">The request can't be carried out, with why, and for which field.</exception>
    /// <exception cref="UrlQueryException">The grid's query can't be run: the errors say why, by field.</exception>
    public static void Check(ExportRequest request)
    {
        var formats = Formats(request.Kind);
        if (!formats.Contains(request.Format))
        {
            throw new ExportException(ExportFailure.InvalidRequest, $"This can be saved as {string.Join(" or ", formats.Select(Name))}, not as {Name(request.Format)}.") { Field = "format" };
        }

        switch (request.Kind)
        {
            case ExportKind.Urls:
                UrlGridReader.ThrowIfInvalid(GridExportReader.ValidateUrls(request.Query, null, null));
                break;
            case ExportKind.SitemapUrls:
                if (request.SitemapId is null)
                {
                    throw new ExportException(ExportFailure.InvalidRequest, "Give the sitemap's id.") { Field = "sitemapId" };
                }

                UrlGridReader.ThrowIfInvalid(GridExportReader.ValidateUrls(request.Query, null, null));
                break;
            case ExportKind.Links:
                UrlGridReader.ThrowIfInvalid(GridExportReader.ValidateLinks(request.Query));
                break;
            case ExportKind.RobotsBlocked:
                if (string.IsNullOrWhiteSpace(request.Origin))
                {
                    throw new ExportException(ExportFailure.InvalidRequest, "Give the origin, as the list of origins names it.") { Field = "origin" };
                }

                break;
            case ExportKind.IssueRows:
                RequireCheck(request);
                UrlGridReader.ThrowIfInvalid(GridExportReader.ValidateUrls(request.Query, request.CheckId, request.Group));
                break;
            case ExportKind.Check:
                RequireCheck(request);
                break;
            case ExportKind.Report:
                Within(request.Report.Examples, "examples");
                Within(request.Report.LookExamples, "lookExamples");
                break;
        }
    }

    /// <summary>
    /// The full path an export would write, from the path asked for: a file, with the format's extension, that isn't the
    /// crawl itself.
    /// </summary>
    /// <exception cref="ExportException"><see cref="ExportFailure.InvalidPath"/>, saying what's wrong with it.</exception>
    public static string CheckPath(string targetPath, ExportFormat format, string crawlPath) => ExportTarget.Check(targetPath, format, crawlPath);

    /// <summary>
    /// Says, before anything is read or written, when a file can't be written at a path <see cref="CheckPath"/> gave: its
    /// folder isn't there, or a file of that name is read-only or open in another program. An export finds the same out
    /// for itself as it starts; this is for a caller that would rather refuse at once than start one.
    /// </summary>
    /// <exception cref="ExportException"><see cref="ExportFailure.NotWritable"/> or <see cref="ExportFailure.FileInUse"/>, with why.</exception>
    public static void CheckWritable(string path) => ExportTarget.CheckWritable(path);

    /// <summary>Writes the export to <paramref name="targetPath"/>, in place of any file there, and says what it wrote.</summary>
    /// <param name="crawlPath">The crawl file, which is only read.</param>
    /// <param name="targetPath">The file to write. Its extension is the format's.</param>
    /// <exception cref="ExportException">Nothing was written, with why (<see cref="ExportFailure"/>), in a plain sentence.</exception>
    /// <exception cref="UrlQueryException">The grid's query can't be run: the errors say why, by field.</exception>
    /// <exception cref="NeedsAnalysisException">The query filters or sorts on the URL summary, and there's none that describes the crawl.</exception>
    /// <exception cref="InvalidOperationException">The crawl file isn't a crawl, or was written with another schema.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled: nothing is left behind.</exception>
    public static ExportResult Export(string crawlPath, ExportRequest request, string targetPath, ExportSettings? settings = null, CancellationToken cancellationToken = default)
    {
        // What a grid's reads count is kept between an export's pages, as it's kept between a grid's: without it, every
        // page of a filtered or sorted export would count its rows, and order its targets, again.
        settings ??= new ExportSettings();
        settings = settings.Cache is null ? settings with { Cache = new ReadCache() } : settings;
        Check(request);
        var path = ExportTarget.Check(targetPath, request.Format, crawlPath);
        cancellationToken.ThrowIfCancellationRequested();

        var run = new ExportRun(crawlPath, request, settings, cancellationToken);
        using var target = ExportTarget.Open(path, settings.Journal);
        ITableWriter? table = null;
        try
        {
            switch (request.Kind)
            {
                case ExportKind.Report:
                    ReportExport.Write(run, target.Stream);
                    break;
                case ExportKind.Workbook:
                    table = Workbook(target);
                    WorkbookExport.WriteWorkbook(run, (XlsxTableWriter)table);
                    break;
                case ExportKind.Check:
                    table = Table(request.Format, target);
                    WorkbookExport.WriteCheck(run, table);
                    break;
                default:
                    table = Table(request.Format, target);
                    GridExport.Write(run, table);
                    break;
            }

            table?.Complete();
            cancellationToken.ThrowIfCancellationRequested();
            var (sheets, truncated) = (table?.Sheets ?? 0, table?.TruncatedCells ?? 0);

            // The writer lets go of the file before it's moved into place.
            table?.Dispose();
            var bytes = target.Commit();
            return new ExportResult(path, run.Rows, bytes)
            {
                Sheets = sheets,
                TruncatedCells = truncated,
                UnavailableColumns = run.Unavailable,
                CrawlWasRunning = run.CrawlWasRunning
            };
        }
        finally
        {
            try
            {
                table?.Dispose();
            }
            catch (Exception exception) when (exception is ExportException || ExportTarget.IsFileError(exception))
            {
                // The export has failed already, or been given up: closing what it was writing says nothing new.
            }
        }
    }

    /// <summary>The formats a kind can be written in, the usual one first.</summary>
    public static IReadOnlyList<ExportFormat> Formats(ExportKind kind) => kind switch
    {
        ExportKind.Workbook => [ExportFormat.Xlsx],
        ExportKind.Report => [ExportFormat.Html],
        ExportKind.Check => [ExportFormat.Xlsx, ExportFormat.Csv],
        _ => [ExportFormat.Csv, ExportFormat.Xlsx]
    };

    private static ITableWriter Table(ExportFormat format, ExportTarget target) =>
        format == ExportFormat.Csv ? new CsvTableWriter(target.Stream) : Workbook(target);

    private static XlsxTableWriter Workbook(ExportTarget target) =>
        new(new XlsxWriter(target.Stream, target.OpenBeside("strings"), CompressionLevel.Fastest), MAX_SHEETS);

    private static void RequireCheck(ExportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CheckId))
        {
            throw new ExportException(ExportFailure.InvalidRequest, "Give the check's id.") { Field = "checkId" };
        }

        if (IssueCatalog.Find(request.CheckId) is null)
        {
            throw new ExportException(ExportFailure.CheckNotFound, $"There's no check called '{request.CheckId}'.");
        }
    }

    private static void Within(int examples, string field)
    {
        if (examples is < 0 or > ReportOptions.MAX_EXAMPLES)
        {
            throw new ExportException(ExportFailure.InvalidRequest, $"{field} must be from 0 to {ReportOptions.MAX_EXAMPLES}.") { Field = field };
        }
    }

    private static string Name(ExportFormat format) => format switch
    {
        ExportFormat.Csv => "CSV",
        ExportFormat.Xlsx => "an Excel workbook",
        _ => "HTML"
    };
}
