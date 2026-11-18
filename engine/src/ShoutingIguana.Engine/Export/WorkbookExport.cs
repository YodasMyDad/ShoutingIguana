using System.Globalization;
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
/// every URL it flagged, the page's title, what was found, and the other URL involved, then where to fix it where the
/// check's URLs are referred to (<see cref="WhereFoundColumns"/>). A check's sheet is named by its
/// number in the report and the start of its title ("3 Large images that slow pag…"), so the report's "the full list is
/// on sheet 3" finds it, and the summary lists each number with its sheet and its full title.
/// <para>One check's sheet can also be written as a file of its own, as a workbook or as CSV.</para>
/// </summary>
internal static class WorkbookExport
{
    /// <summary>A check's sheet: the URL flagged, its page's title, what was found, and the other URL involved.</summary>
    private static readonly string[] CHECK_COLUMNS = ["url", "title", "detail", "relatedUrl"];

    private static readonly string[] CHECK_TITLES = ["Address", "Page title", "What was found", "Other address"];

    /// <summary>A check of the site's own files: the robots.txt or sitemaps it's about, and what was found.</summary>
    private static readonly string[] FILE_TITLES = ["File", "What was found"];

    /// <summary>The evidence columns that name another URL, left empty where it's the address itself, and out when it always is.</summary>
    private static readonly string[] ELSEWHERE_COLUMNS = ["finalUrl", "canonicalUrl"];

    private static readonly double[] SUMMARY_WIDTHS = [26, 34, 16, 14, 12, 48, 44, 34, 20, 12, 12, 16, 10, 12, 60, 12, 16];

    /// <summary>The sheet of what the check flagged then and doesn't now (the fix loop): which check, the URL, how it moved, and what was found then.</summary>
    private static readonly string[] FIXED_TITLES = ["Problem", "Check ID", "Address", "Now", "Why not checked", "What was found then", "Other address then"];

    private static readonly Dictionary<string, UrlColumnInfo> COLUMNS = UrlGridReader.IssueRowColumns.ToDictionary(column => column.Id, StringComparer.Ordinal);

    public static void WriteWorkbook(ExportRun run, XlsxTableWriter table)
    {
        var model = ReportReader.Read(run.CrawlPath, _ => 0, run.Settings, cancellationToken: run.Token, listing: run.Request.Report);
        var cleared = Cleared(run, model);
        run.Expect(model.Problems.Sum(problem => problem.Count) + cleared.Sum(check => check.Total));

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

        if (model.Comparison is { } since && cleared.Count > 0)
        {
            WriteFixed(run, table, since, cleared, model.TimeZone);
        }

        // The sheets are of the analysis the summary was read from only if it's still the last.
        if (IssueReader.ReadAnalysis(run.CrawlPath)?.AnalyzedAt != model.AnalyzedAt)
        {
            throw new ExportException(ExportFailure.CrawlChanged, GridExport.CRAWL_CHANGED);
        }
    }

    /// <summary>
    /// One check's sheet as a file of its own: named as the workbook with the same checks hidden names it (numbered
    /// afresh without them, as the report is), when the check found something and isn't hidden itself.
    /// </summary>
    public static void WriteCheck(ExportRun run, ITableWriter table)
    {
        var results = IssueReader.ReadResults(run.CrawlPath) ?? throw new ExportException(ExportFailure.NotAnalyzed, GridExport.NOT_ANALYZED);
        var check = IssueCatalog.Find(run.Request.CheckId!)!;
        var hidden = run.Request.Report.HiddenChecks;
        var number = ReportSources.FixFirst(results).Where(id => !hidden.Contains(id, StringComparer.Ordinal)).ToList().IndexOf(check.Id) + 1;
        var title = ReportSources.Words(check, ReportSources.Settings(results)).Title;
        WriteSheet(run, table, number > 0 ? SheetTitle(number, title) : title, check.Id);
    }

    /// <summary>
    /// The checks whose rows then the workbook lists as no longer flagged (the fix loop): every check of the comparison that
    /// flagged something then, with how many it no longer flags. None when the crawl isn't compared, or only by its counts.
    /// </summary>
    private static List<(ReportChange Change, long Total)> Cleared(ExportRun run, ReportModel model)
    {
        if (model.Comparison is not { Comparison.Comparable: not Comparability.CountsOnly } since)
        {
            return [];
        }

        List<(ReportChange Change, long Total)> cleared = [];
        foreach (var change in since.Checks.Where(change => change.Then > 0))
        {
            var total = CrawlComparisonReader.ReadCleared(run.CrawlPath, since.OtherPath, change.Check.Id, new ClearedQuery(Limit: 1), run.Settings.Cache, run.Token)?.Total ?? 0;
            if (total > 0)
            {
                cleared.Add((change, total));
            }
        }

        return cleared;
    }

    /// <summary>The sheet of what each check flagged then and doesn't now, check by check, each in the other crawl's order by importance.</summary>
    private static void WriteFixed(ExportRun run, ITableWriter table, ReportComparison since, List<(ReportChange Change, long Total)> cleared, TimeZoneInfo zone)
    {
        table.Begin($"Fixed since {Short(since.StartedAt, zone)}", FIXED_TITLES);
        foreach (var (change, _) in cleared)
        {
            for (var offset = 0; ; offset += UrlGridReader.MAX_LIMIT)
            {
                var page = CrawlComparisonReader.ReadCleared(run.CrawlPath, since.OtherPath, change.Check.Id, new ClearedQuery(Offset: offset, Limit: UrlGridReader.MAX_LIMIT),
                    run.Settings.Cache, run.Token);
                if (page is null || page.Rows.Count == 0)
                {
                    break;
                }

                foreach (var row in page.Rows)
                {
                    table.WriteRow([change.Words.Title, change.Check.Id, row.Url, Moved(row.State, change.Check), row.Reason?.Text, row.DetailThen, row.RelatedUrlThen]);
                }

                run.Wrote(page.Rows.Count);
                if (page.Rows.Count < UrlGridReader.MAX_LIMIT)
                {
                    break;
                }
            }
        }

        table.End();
    }

    /// <summary>How a URL flagged then moved, in the report's words: gone is no longer linked for a check of what others refer to.</summary>
    private static string Moved(RowChangeState state, IssueDefinition check) => state switch
    {
        RowChangeState.Fixed => "Fixed",
        RowChangeState.Gone => check.References == IssueReferences.Sources ? "No longer linked" : "No longer found",
        _ => "Not checked this time"
    };

    /// <summary>A check's change since the other crawl, in a word or two.</summary>
    private static string Changed(CheckChangeState state) => state switch
    {
        CheckChangeState.New => "New",
        CheckChangeState.Fixed => "Fixed",
        CheckChangeState.Fewer => "Fewer",
        CheckChangeState.More => "More",
        CheckChangeState.Same => "Same",
        CheckChangeState.NewCheck => "Not looked for then",
        _ => "Not checked now"
    };

    /// <summary>"3 Nov": a date as a column's title says it.</summary>
    private static string Short(DateTimeOffset moment, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(moment, zone).ToString("d MMM", CultureInfo.InvariantCulture);

    /// <summary>A problem's sheet, before it's cut to what a sheet's name holds: its number, then its title.</summary>
    internal static string SheetTitle(int number, string title) => $"{number} {title}";

    /// <summary>
    /// A check's sheet: its own four columns, then its evidence (each under its title, but the page's title, which is there
    /// already), then where to fix it. Its rows come by importance, as the check's grid gives them. A column empty on every
    /// row is left out, and so is "Other address" where where-to-fix names the same URLs; a final URL or canonical that is
    /// the address itself is left empty, and out when it always is. A check of the site's own files names them instead of
    /// the home page it's reported on (<see cref="WriteFileSheet"/>).
    /// </summary>
    private static void WriteSheet(ExportRun run, ITableWriter table, string name, string checkId)
    {
        var check = IssueCatalog.Find(checkId);
        if (check?.About is not null && IssueFileReader.Read(run.CrawlPath, run.Settings.Cache, run.Token)?.Checks.GetValueOrDefault(checkId) is { Count: > 0 } files)
        {
            WriteFileSheet(run, table, name, checkId, files);
            return;
        }

        var used = IssueSheetReader.Read(run.CrawlPath, checkId, run.Settings.Cache, run.Token);
        bool[] keep = [true, used.Titles, used.Details, used.RelatedUrls && check?.References != IssueReferences.Targets];
        bool Elsewhere(string column) => column switch
        {
            "finalUrl" => used.FinalUrlsElsewhere,
            "canonicalUrl" => used.CanonicalsElsewhere,
            _ => true
        };

        var own = CHECK_COLUMNS.Where((_, index) => keep[index]).Select(id => COLUMNS[id]).ToList();
        var evidence = (check?.Evidence ?? []).Where(id => !CHECK_COLUMNS.Contains(id) && COLUMNS.ContainsKey(id) && Elsewhere(id)).Select(id => COLUMNS[id]).ToList();
        List<UrlColumnInfo> columns = [.. own, .. evidence];
        GridExport.WriteGrid(run, table, name, columns, links: false, checkId, sitemapId: null,
            [.. CHECK_TITLES.Where((_, index) => keep[index]), .. evidence.Select(GridExport.Title)], query: new UrlQuery(), extra: WhereFoundColumns.For(run, checkId),
            rewrite: (index, cell, row) => ELSEWHERE_COLUMNS.Contains(columns[index].Id) && Equals(cell, row[0]) ? null : cell);
    }

    /// <summary>
    /// The sheet of a check of the site's robots.txt or sitemaps: the files it's about, one a line, where its row would name
    /// the home page, and what was found. One row, as the check has.
    /// </summary>
    private static void WriteFileSheet(ExportRun run, ITableWriter table, string name, string checkId, IReadOnlyList<IssueFile> files)
    {
        var text = string.Join('\n', files.Select(file => file.Url));
        GridExport.WriteGrid(run, table, name, [COLUMNS["url"], COLUMNS["detail"]], links: false, checkId, sitemapId: null, FILE_TITLES, query: new UrlQuery(),
            rewrite: (index, cell, _) => index == 0 ? text : cell);
    }

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
        if (model.Comparison is { } compared)
        {
            Row(Label("Compared with"), XlsxCell.Text($"The check of {ReportWords.Date(compared.StartedAt, model.TimeZone)}, {ReportWords.Time(compared.StartedAt, model.TimeZone)}"));
        }
        else if (model.NotCompared is { } notCompared)
        {
            Row(Label("Compared with"), XlsxCell.Text(notCompared));
        }

        if (model.Rechecks.Count > 0)
        {
            Row(Label("Rechecked"), Count(model.Rechecks.Count), XlsxCell.Text("times: other pages are as the check found them"));
        }

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
        if (model.HiddenProblems > 0)
        {
            Row(Label("Hidden checks left out"), Count(model.HiddenProblems), XlsxCell.Text("Checks that found something, hidden in the app. The score still counts them."));
        }

        if (model.Narrowed)
        {
            Row(Label("Checks listed"), XlsxCell.Text("Only the checks the view listed when this was saved"));
        }

        Row();
        Row(
        [
            .. new[]
            {
                "Number", "Sheet", "Result", "Severity", "Called", "Problem", "Check", "Check ID", "Category", "Found on", "Out of", "Of what", "Share", "Points off",
                "Why it couldn’t run"
            }.Select(Label),
            .. model.Comparison is { } since ? (XlsxCell[])[Label($"Found on {Short(since.StartedAt, model.TimeZone)}"), Label("Change")] : []
        ]);

        // Each check's count then and how it changed, when the crawl is compared with another (the fix loop).
        var changes = model.Comparison?.Checks.ToDictionary(change => change.Check.Id, StringComparer.Ordinal);
        XlsxCell[] Then(string checkId) => changes is null ? []
            : changes.TryGetValue(checkId, out var change) ? [change.Then is { } then ? Count(then) : XlsxCell.Empty, XlsxCell.Text(Changed(change.State))]
            : [XlsxCell.Empty, XlsxCell.Empty];

        foreach (var problem in model.Problems)
        {
            var line = problem.Line;
            var once = problem.Check.ReportedOnce || line is { Unit: ScoreUnit.Site };
            Row([
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
                line is null ? XlsxCell.Empty : XlsxCell.Number(line.Points),
                .. changes is null ? [] : (XlsxCell[])[XlsxCell.Empty, .. Then(problem.Check.Id)]
            ]);
        }

        foreach (var passed in model.Passed)
        {
            Row([.. Rest(passed.Check, passed.Words, "Nothing wrong", Count(0), XlsxCell.Empty), .. Then(passed.Check.Id)]);
        }

        foreach (var skipped in model.Skipped)
        {
            Row([.. Rest(skipped.Check, skipped.Words, "Couldn’t run", XlsxCell.Empty, XlsxCell.Text(skipped.Action is null ? skipped.Why : $"{skipped.Why} {skipped.Action}")),
                .. Then(skipped.Check.Id)]);
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
