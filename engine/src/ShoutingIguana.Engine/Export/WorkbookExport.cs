using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Export.Models;
using ShoutingIguana.Engine.Export.Tables;
using ShoutingIguana.Engine.Export.Xlsx;
using ShoutingIguana.Engine.Export.Xlsx.Models;
using ShoutingIguana.Engine.Reporting;
using ShoutingIguana.Engine.Reporting.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Export;

/// <summary>
/// The workbook that goes with the report: a summary sheet (the score, the counts, and every check with its severity,
/// its plain and technical titles, its count and its share), then a sheet for each check that found something, with
/// every URL it flagged, the page's title, what was found, and the other URL involved. A check's sheet is named by its
/// number in the report and the start of its title ("3 Large images that slow pag…"), so the report's "the full list is
/// on sheet 3" finds it, and the summary lists each number with its sheet and its full title.
/// <para>One check's sheet can also be written as a file of its own, as a workbook or as CSV.</para>
/// </summary>
internal static class WorkbookExport
{
    /// <summary>A check's sheet: the URL flagged, its page's title, what was found, and the other URL involved.</summary>
    private static readonly string[] CHECK_COLUMNS = ["url", "title", "detail", "relatedUrl"];

    private static readonly string[] CHECK_TITLES = ["Address", "Page title", "What was found", "Other address"];

    private static readonly double[] SUMMARY_WIDTHS = [26, 34, 16, 14, 12, 48, 44, 34, 20, 12, 12, 16, 10, 12, 60];

    private static readonly Dictionary<string, UrlColumnInfo> COLUMNS = UrlGridReader.IssueRowColumns.ToDictionary(column => column.Id, StringComparer.Ordinal);

    public static void WriteWorkbook(ExportRun run, XlsxTableWriter table)
    {
        var model = ReportReader.Read(run.CrawlPath, _ => 0, run.Settings, cancellationToken: run.Token);
        run.Expect(model.Problems.Sum(problem => problem.Count));

        // The names first: the summary, which is written first, says which sheet each problem is on.
        List<string> names = ["Summary"];
        foreach (var problem in model.Problems)
        {
            names.Add(XlsxWriter.SheetName(SheetTitle(problem.Number, problem.Words.Title), names));
        }

        WriteSummary(table.Workbook, table.Reserve(names[0]), model, names);
        foreach (var problem in model.Problems)
        {
            WriteSheet(run, table, names[problem.Number], problem.Check.Id);
        }

        // The sheets are of the analysis the summary was read from only if it's still the last.
        if (IssueReader.ReadAnalysis(run.CrawlPath)?.AnalyzedAt != model.AnalyzedAt)
        {
            throw new ExportException(ExportFailure.CrawlChanged, GridExport.CRAWL_CHANGED);
        }
    }

    /// <summary>One check's sheet as a file of its own: named as the workbook names it, when the check found something.</summary>
    public static void WriteCheck(ExportRun run, ITableWriter table)
    {
        var results = IssueReader.ReadResults(run.CrawlPath) ?? throw new ExportException(ExportFailure.NotAnalyzed, GridExport.NOT_ANALYZED);
        var check = IssueCatalog.Find(run.Request.CheckId!)!;
        var number = ReportSources.FixFirst(results).ToList().IndexOf(check.Id) + 1;
        var title = ReportSources.Words(check, ReportSources.Settings(results)).Title;
        WriteSheet(run, table, number > 0 ? SheetTitle(number, title) : title, check.Id);
    }

    /// <summary>A problem's sheet, before it's cut to what a sheet's name holds: its number, then its title.</summary>
    internal static string SheetTitle(int number, string title) => $"{number} {title}";

    private static void WriteSheet(ExportRun run, ITableWriter table, string name, string checkId) =>
        GridExport.WriteGrid(run, table, name, [.. CHECK_COLUMNS.Select(id => COLUMNS[id])], links: false, checkId, sitemapId: null, CHECK_TITLES,
            query: new UrlQuery());

    private static void WriteSummary(XlsxWriter workbook, string name, ReportModel model, IReadOnlyList<string> sheets)
    {
        workbook.BeginSheet(name, SUMMARY_WIDTHS);
        void Row(params XlsxCell[] cells) => workbook.WriteRow(cells);
        static XlsxCell Label(string text) => XlsxCell.Text(text, XlsxStyle.Bold);
        static XlsxCell Count(long number) => XlsxCell.Number(number, XlsxStyle.Count);

        Row(XlsxCell.Text($"Website check: {model.Site}", XlsxStyle.Title));
        Row();
        Row(Label("Started from"), XlsxCell.Text(model.StartUrl));
        Row(Label("Checked on"), XlsxCell.DateTime(TimeZoneInfo.ConvertTime(model.StartedAt, model.TimeZone).DateTime));
        Row(Label("Results worked out on"), XlsxCell.DateTime(TimeZoneInfo.ConvertTime(model.AnalyzedAt, model.TimeZone).DateTime));
        Row(Label("Finished?"), XlsxCell.Text(
            model.CrawlWentOn ? "The check has gone on since these results were worked out" : model.Finished ? "Yes" : "No: this covers only what was read"));
        Row(Label("Pages read"), Count(model.Pages));
        Row(Label("Addresses checked"), Count(model.Urls));
        Row(Label("Checks run"), XlsxCell.Text($"{model.ChecksRun} of {model.ChecksInCatalog}"));
        if (model.Score is { } score)
        {
            Row(Label("Score"), XlsxCell.Number(score.Value), XlsxCell.Text(ReportWords.Band(score.Band) + (score.Partial ? ", so far" : string.Empty)));
            Row(Label("Score version"), XlsxCell.Number(score.Version));
        }
        else
        {
            Row(Label("Score"), XlsxCell.Text(model.NoScore == NoScoreReason.NotScored
                ? "No score: these results were worked out before the score was. Work them out again to score the site"
                : "No score: no page could be read"));
        }

        Row();
        Row(Label("Serious problems"), Count(model.ChecksOf(IssueSeverity.Error)));
        Row(Label("Problems worth fixing"), Count(model.ChecksOf(IssueSeverity.Warning)));
        Row(Label("Things worth a look"), Count(model.ChecksOf(IssueSeverity.Notice)));
        Row(Label("Checks that found nothing wrong"), Count(model.Passed.Count));
        Row(Label("Checks that couldn’t run"), Count(model.Skipped.Count));
        Row();
        Row(
        [
            .. new[]
            {
                "Number", "Sheet", "Result", "Severity", "Called", "Problem", "Check", "Check ID", "Category", "Found on", "Out of", "Of what", "Share", "Points off",
                "Why it couldn’t run"
            }.Select(Label)
        ]);

        foreach (var problem in model.Problems)
        {
            var line = problem.Line;
            var once = problem.Check.ReportedOnce || line is { Unit: ScoreUnit.Site };
            Row(
                XlsxCell.Number(problem.Number),
                XlsxCell.Text(sheets[problem.Number]),
                XlsxCell.Text("Found"),
                XlsxCell.Text(ReportWords.Severity(problem.Check.Severity)),
                XlsxCell.Text(ReportWords.Technical(problem.Check.Severity)),
                XlsxCell.Text(problem.Words.Title),
                XlsxCell.Text(problem.Check.Title),
                XlsxCell.Text(problem.Check.Id),
                XlsxCell.Text(ReportSources.Category(problem.Check.Category)),
                Count(problem.Count),
                // A check reported once has no share: one issue stands for the whole site, whatever the site's size.
                line is null || once ? XlsxCell.Empty : Count(line.Of),
                XlsxCell.Text(once ? ReportWords.CHECKED_ONCE : line?.Unit switch { ScoreUnit.Pages => "pages", ScoreUnit.Urls => "addresses", _ => null }),
                line is null || once || line.Of <= 0 ? XlsxCell.Empty : XlsxCell.Number(Math.Min(1d, (double)problem.Count / line.Of), XlsxStyle.Percent),
                line is null ? XlsxCell.Empty : XlsxCell.Number(line.Points));
        }

        foreach (var passed in model.Passed)
        {
            Row(Rest(passed.Check, passed.Words, "Nothing wrong", Count(0), XlsxCell.Empty));
        }

        foreach (var skipped in model.Skipped)
        {
            Row(Rest(skipped.Check, skipped.Words, "Couldn’t run", XlsxCell.Empty, XlsxCell.Text(skipped.Action is null ? skipped.Why : $"{skipped.Why} {skipped.Action}")));
        }

        workbook.EndSheet();
    }

    /// <summary>
    /// The row of a check that found nothing, or couldn't run: no number, no sheet, no share. It is named by what it looks
    /// for where it says (<see cref="CheckWords.Named"/>): a title that is a sentence about the site would read as a finding.
    /// </summary>
    private static XlsxCell[] Rest(IssueDefinition check, CheckWords words, string result, XlsxCell count, XlsxCell why) =>
    [
        XlsxCell.Empty,
        XlsxCell.Empty,
        XlsxCell.Text(result),
        XlsxCell.Text(ReportWords.Severity(check.Severity)),
        XlsxCell.Text(ReportWords.Technical(check.Severity)),
        XlsxCell.Text(words.Named),
        XlsxCell.Text(check.Title),
        XlsxCell.Text(check.Id),
        XlsxCell.Text(ReportSources.Category(check.Category)),
        count,
        XlsxCell.Empty,
        XlsxCell.Empty,
        XlsxCell.Empty,
        XlsxCell.Empty,
        why
    ];
}
