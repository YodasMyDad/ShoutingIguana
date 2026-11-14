using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Export;
using ShoutingIguana.Engine.Export.Models;
using ShoutingIguana.Engine.Reporting;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using static ShoutingIguana.Engine.Tests.Support.GridQueries;

namespace ShoutingIguana.Engine.Tests.Export;

/// <summary>
/// The workbook of everything: its summary sheet says every check once, with its count and the sheet it's on, and each
/// check that found something has a sheet holding every URL it flagged, as the check's own grid reads them. One check's
/// sheet as a file of its own is that sheet.
/// </summary>
[Collection(GridCrawlCollection.NAME)]
public sealed class WorkbookExportTests : IDisposable
{
    private static readonly string[] SHEET_COLUMNS = ["url", "title", "detail", "relatedUrl"];
    private static readonly string[] SHEET_TITLES = ["Address", "Page title", "What was found", "Other address"];

    private readonly GridCrawl _crawl;
    private readonly string _folder = Directory.CreateTempSubdirectory("si-export-").FullName;
    private readonly (int Urls, int Links) _pages = (GridExportReader.UrlPageRows, GridExportReader.LinkPageRows);

    public WorkbookExportTests(GridCrawl crawl)
    {
        _crawl = crawl;
        (GridExportReader.UrlPageRows, GridExportReader.LinkPageRows) = (97, 211);
    }

    public void Dispose()
    {
        (GridExportReader.UrlPageRows, GridExportReader.LinkPageRows) = _pages;
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void TheWorkbookHasASummaryAndASheetForEachCheckThatFoundSomething()
    {
        var path = Path.Combine(_folder, "everything.xlsx");
        var result = CrawlExporter.Export(_crawl.Path, new ExportRequest { Kind = ExportKind.Workbook, Format = ExportFormat.Xlsx }, path);

        var results = IssueReader.ReadResults(_crawl.Path)!;
        var order = results.FixFirst;
        Assert.True(order.Count > 30, $"Only {order.Count} checks found something.");
        var file = XlsxFile.Read(path);
        Assert.Equal(order.Count + 1, file.Sheets.Count);
        Assert.Equal(order.Count + 1, result.Sheets);
        Assert.Equal(order.Sum(id => results.Counts[id]), result.Rows);
        Assert.Equal("Summary", file.Sheets[0].Name);

        // The checks in the order to fix them, which is the engine's: a fault of the whole site leads its severity, whatever its count.
        Assert.Equal(FixFirstOrder.Of(results.Counts), order);
        Assert.True(IssueCatalog.Find(order[0])!.SiteWide, order[0]);

        for (var number = 1; number <= order.Count; number++)
        {
            var check = IssueCatalog.Find(order[number - 1])!;
            var sheet = file.Sheets[number];
            Assert.True(sheet.Name.Length <= 31, sheet.Name);
            Assert.StartsWith($"{number} ", sheet.Name, StringComparison.Ordinal);
            Assert.Equal(1, sheet.FrozenRows);
            Assert.Equal(SHEET_TITLES, sheet.Values[0]);
            Assert.All(sheet.Rows[0], cell => Assert.Equal(1u, cell.Style));
            AssertHoldsTheChecksRows(check.Id, sheet.Values);
        }

        AssertSummary(file.Sheets[0].Values, results, order, [.. file.Sheets.Select(sheet => sheet.Name)]);
    }

    /// <summary>Discovery decision 2: a check the user hid is left out of the workbook's sheets and its summary, which says so.</summary>
    [Fact]
    public void AWorkbookLeavesOutTheChecksTheUserHid()
    {
        var results = IssueReader.ReadResults(_crawl.Path)!;
        var order = results.FixFirst;
        string[] hidden = [order[0], order[3], "a-check-from-another-version"];
        var path = Path.Combine(_folder, "hidden.xlsx");
        var result = CrawlExporter.Export(_crawl.Path,
            new ExportRequest { Kind = ExportKind.Workbook, Format = ExportFormat.Xlsx, Report = new() { HiddenChecks = hidden } }, path);

        var kept = order.Where(id => !hidden.Contains(id)).ToList();
        var file = XlsxFile.Read(path);
        Assert.Equal(kept.Count + 1, file.Sheets.Count);
        Assert.Equal(kept.Sum(id => results.Counts[id]), result.Rows);
        // Numbered afresh: the first sheet after the summary is the first check left in.
        Assert.StartsWith("1 ", file.Sheets[1].Name, StringComparison.Ordinal);
        AssertHoldsTheChecksRows(kept[0], file.Sheets[1].Values);
        var summary = file.Sheets[0].Values;
        Assert.DoesNotContain(summary, row => row.Contains(order[0]) || row.Contains(order[3]));
        var note = Assert.Single(summary, row => Equals(row[0], "Hidden checks left out"));
        Assert.Equal(2d, Convert.ToDouble(note[1], System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>The workbook of an Issues view takes the view's checks, as a grid's file takes its filters.</summary>
    [Fact]
    public void AWorkbookOfAViewHoldsTheViewsChecksOnly()
    {
        var results = IssueReader.ReadResults(_crawl.Path)!;
        var errors = results.FixFirst.Where(id => IssueCatalog.Find(id)!.Severity == IssueSeverity.Error).ToList();
        var clean = IssueCatalog.All.First(check => results.Counts.GetValueOrDefault(check.Id, -1) == 0).Id;
        var path = Path.Combine(_folder, "errors.xlsx");
        CrawlExporter.Export(_crawl.Path,
            new ExportRequest { Kind = ExportKind.Workbook, Format = ExportFormat.Xlsx, Report = new() { Checks = [.. errors, clean] } }, path);

        var file = XlsxFile.Read(path);
        Assert.Equal(errors.Count + 1, file.Sheets.Count);
        var summary = file.Sheets[0].Values;
        Assert.Contains(summary, row => row.Contains(clean));
        Assert.DoesNotContain(summary, row => row.Length > 7 && row[7] is string id && IssueCatalog.Find(id) is { } check && check.Id != clean && !errors.Contains(id));
        Assert.Contains(summary, row => Equals(row[0], "Checks listed"));
    }

    [Theory]
    [InlineData(ExportFormat.Xlsx)]
    [InlineData(ExportFormat.Csv)]
    public void OneChecksSheetIsAFileOfItsOwn(ExportFormat format)
    {
        var results = IssueReader.ReadResults(_crawl.Path)!;
        var order = results.FixFirst;
        var check = IssueCatalog.Find(order[2])!;
        var path = Path.Combine(_folder, format == ExportFormat.Csv ? "check.csv" : "check.xlsx");
        var result = CrawlExporter.Export(_crawl.Path, new ExportRequest { Kind = ExportKind.Check, Format = format, CheckId = check.Id }, path);
        Assert.Equal(results.Counts[check.Id], result.Rows);

        if (format == ExportFormat.Csv)
        {
            var rows = CsvFile.Read(path).Select(row => row.Select(field => field.Length == 0 ? null : (object)field).ToArray()).ToList();
            Assert.Equal(SHEET_TITLES, rows[0]);
            AssertHoldsTheChecksRows(check.Id, rows, guarded: true);
            return;
        }

        var sheet = Assert.Single(XlsxFile.Read(path).Sheets);
        Assert.StartsWith("3 ", sheet.Name, StringComparison.Ordinal);
        Assert.Equal(SHEET_TITLES, sheet.Values[0]);
        AssertHoldsTheChecksRows(check.Id, sheet.Values);
    }

    /// <summary>
    /// Round 5's review (R2-9): the report and the workbook number their checks afresh once the hidden ones are left out,
    /// but one check's file was numbered against every check (the workbook's sheet "4 …", its own file "5 …"). It takes
    /// the hidden checks too, and is named as that check's sheet is in the workbook with them hidden.
    /// </summary>
    [Fact]
    public void OneChecksFileIsNumberedAsTheWorkbookWithTheSameChecksHidden()
    {
        var order = IssueReader.ReadResults(_crawl.Path)!.FixFirst;
        string[] hidden = [order[0], "a-check-from-another-version"];
        var check = order[4];

        var workbook = Path.Combine(_folder, "hidden-for-one.xlsx");
        CrawlExporter.Export(_crawl.Path, new ExportRequest { Kind = ExportKind.Workbook, Format = ExportFormat.Xlsx, Report = new() { HiddenChecks = hidden } }, workbook);
        var inTheWorkbook = XlsxFile.Read(workbook).Sheets[4].Name;
        Assert.StartsWith("4 ", inTheWorkbook, StringComparison.Ordinal);

        var one = Path.Combine(_folder, "one-with-hidden.xlsx");
        CrawlExporter.Export(_crawl.Path, new ExportRequest { Kind = ExportKind.Check, Format = ExportFormat.Xlsx, CheckId = check, Report = new() { HiddenChecks = hidden } }, one);
        Assert.Equal(inTheWorkbook, Assert.Single(XlsxFile.Read(one).Sheets).Name);
        AssertHoldsTheChecksRows(check, XlsxFile.Read(one).Sheets[0].Values);

        // The check itself hidden: its file is still what was asked for, named without a number (it has none in the list).
        var itself = Path.Combine(_folder, "itself-hidden.xlsx");
        CrawlExporter.Export(_crawl.Path, new ExportRequest { Kind = ExportKind.Check, Format = ExportFormat.Xlsx, CheckId = order[0], Report = new() { HiddenChecks = hidden } }, itself);
        Assert.False(char.IsDigit(Assert.Single(XlsxFile.Read(itself).Sheets).Name[0]));

        // And the API and the command line take hiddenChecks for a check's file.
        Assert.True(ExportFields.Takes(ExportKind.Check, "hiddenChecks"));
    }

    [Fact]
    public void ACheckThatFoundNothingIsAnEmptySheetUnderItsHeader()
    {
        var results = IssueReader.ReadResults(_crawl.Path)!;
        var clean = IssueCatalog.All.First(check => results.Counts.GetValueOrDefault(check.Id, -1) == 0);
        var path = Path.Combine(_folder, "clean.xlsx");
        var result = CrawlExporter.Export(_crawl.Path, new ExportRequest { Kind = ExportKind.Check, Format = ExportFormat.Xlsx, CheckId = clean.Id }, path);
        Assert.Equal(0, result.Rows);
        Assert.Equal([SHEET_TITLES], Assert.Single(XlsxFile.Read(path).Sheets).Values);
    }

    [Fact]
    public void ACrawlNeverAnalysedHasNoWorkbook()
    {
        var never = _crawl.Copy();
        Change(never, "DELETE FROM analysis");
        foreach (var request in new[]
                 {
                     new ExportRequest { Kind = ExportKind.Workbook, Format = ExportFormat.Xlsx },
                     new ExportRequest { Kind = ExportKind.Check, Format = ExportFormat.Xlsx, CheckId = "title-missing" },
                     new ExportRequest { Kind = ExportKind.IssueRows, Format = ExportFormat.Xlsx, CheckId = "title-missing" }
                 })
        {
            var failure = Assert.Throws<ExportException>(() => CrawlExporter.Export(never, request, Path.Combine(_folder, "never.xlsx")));
            Assert.Equal(ExportFailure.NotAnalyzed, failure.Failure);
        }

        Assert.Empty(Directory.GetFiles(_folder));
    }

    /// <summary>Analysed again part-way through: the sheets written so far are another analysis's, so no workbook is left.</summary>
    [Fact]
    public void AnAnalysisThatFinishesUnderAWorkbookEndsIt()
    {
        var copy = _crawl.Copy();
        var pages = 0;
        var settings = new ExportSettings { Progress = new Heard(_ => { if (++pages == 6) { Change(copy, "UPDATE analysis SET analyzed_at = analyzed_at + 1000"); } }) };
        var failure = Assert.Throws<ExportException>(() => CrawlExporter.Export(copy, new ExportRequest { Kind = ExportKind.Workbook, Format = ExportFormat.Xlsx },
            Path.Combine(_folder, "changed.xlsx"), settings));
        Assert.Equal(ExportFailure.CrawlChanged, failure.Failure);
        Assert.Empty(Directory.GetFiles(_folder));
    }

    /// <summary>A sheet's rows are the check's rows, as its grid reads them in crawl order.</summary>
    private void AssertHoldsTheChecksRows(string checkId, List<object?[]> sheet, bool guarded = false)
    {
        List<IReadOnlyList<object?>> rows = [];
        for (var offset = 0; ; offset += 1_000)
        {
            var page = IssueGroupReader.ReadRows(_crawl.Path, checkId, new UrlQuery { Columns = SHEET_COLUMNS, Offset = offset, Limit = 1_000 })!.Page!;
            Assert.Equal(["id", .. SHEET_COLUMNS], page.Columns);
            rows.AddRange(page.Rows.Select(row => row.Values));
            if (rows.Count >= page.Matching)
            {
                break;
            }
        }

        Assert.Equal(rows.Count, sheet.Count - 1);
        for (var row = 0; row < rows.Count; row++)
        {
            for (var column = 0; column < SHEET_COLUMNS.Length; column++)
            {
                var expected = rows[row][column + 1] as string;
                expected = string.IsNullOrEmpty(expected) ? null : guarded && "=+-@\t\r".Contains(expected[0], StringComparison.Ordinal) ? "'" + expected : expected;
                Assert.True(Equals(expected, sheet[row + 1][column]), $"{checkId}, row {row + 1}, {SHEET_TITLES[column]}: the grid has '{expected}', the sheet '{sheet[row + 1][column]}'.");
            }
        }
    }

    /// <summary>The summary names every check once: the ones that found something with their number, sheet, count and share.</summary>
    private void AssertSummary(List<object?[]> summary, AnalysisResults results, IReadOnlyList<string> order, List<string> sheets)
    {
        Assert.StartsWith("Website check: ", (string)summary[0][0]!, StringComparison.Ordinal);
        object? Fact(string label) => summary.Single(row => Equals(row[0], label))[1];
        Assert.Equal((double)results.Analysis.PagesAnalyzed, Fact("Pages read"));
        Assert.Equal((double)results.Analysis.CrawledUrlCount!.Value, Fact("Addresses checked"));
        Assert.Equal((double)order.Count(id => IssueCatalog.Find(id)!.Severity == IssueSeverity.Error), Fact("Serious problems"));
        Assert.Equal((double)order.Count(id => IssueCatalog.Find(id)!.Severity == IssueSeverity.Warning), Fact("Problems worth fixing"));
        Assert.Equal((double)order.Count(id => IssueCatalog.Find(id)!.Severity == IssueSeverity.Notice), Fact("Things worth a look"));
        Assert.Equal((double)results.Counts.Count(count => count.Value == 0), Fact("Checks that found nothing wrong"));
        Assert.Equal((double)(IssueCatalog.All.Count - results.Counts.Count), Fact("Checks that couldn’t run"));
        Assert.Equal($"{results.Counts.Count} of {IssueCatalog.All.Count}", Fact("Checks run"));

        // The score is the analysis's, with its word and its version.
        var score = results.Score!;
        var scored = summary.Single(row => Equals(row[0], "Score"));
        Assert.Equal(((double)score.Value, ReportWords.Band(score.Band)), ((double)scored[1]!, (string)scored[2]!));
        Assert.Equal((double)ScoreCalculator.VERSION, Fact("Score version"));
        var settings = results.Analysis.Settings!;

        var header = summary.FindIndex(row => Equals(row[0], "Number"));
        Assert.Equal(
            ["Number", "Sheet", "Result", "Severity", "Called", "Problem", "Check", "Check ID", "Category", "Found on", "Out of", "Of what", "Share", "Points off", "Why it couldn’t run"],
            summary[header]);
        var checks = summary.Skip(header + 1).ToList();
        Assert.Equal(IssueCatalog.All.Select(check => check.Id).Order(StringComparer.Ordinal), checks.Select(row => (string)row[7]!).Order(StringComparer.Ordinal));

        for (var number = 1; number <= order.Count; number++)
        {
            var row = checks[number - 1];
            var check = IssueCatalog.Find(order[number - 1])!;
            Assert.Equal((double)number, row[0]);
            Assert.Equal(sheets[number], row[1]);
            Assert.Equal("Found", row[2]);
            Assert.Equal(check.Severity switch { IssueSeverity.Error => "Serious", IssueSeverity.Warning => "Worth fixing", _ => "Worth a look" }, row[3]);
            Assert.Equal(check.Severity.ToString(), row[4]);
            Assert.Equal(PlainLimits.Fill(check.Plain!, settings).Title, row[5]);
            Assert.Equal(check.Title, row[6]);
            Assert.Equal(check.Id, row[7]);
            Assert.Equal(PlainWords.CategoryTitle(check.Category), row[8]);
            Assert.Equal((double)results.Counts[check.Id], row[9]);

            // The share is the score's line: of the pages or the URLs the analysis counted. A check reported once has none.
            var line = score.Lines[number - 1];
            Assert.Equal(check.Id, line.CheckId);
            if (check.ReportedOnce)
            {
                Assert.Equal((null, "Checked once, for the whole site", null), (row[10], row[11], row[12]));
            }
            else
            {
                Assert.Equal(((double)line.Of, line.Unit == ScoreUnit.Pages ? "pages" : "addresses"), ((double)row[10]!, (string)row[11]!));
                Assert.Equal(Math.Min(1d, (double)line.Count / line.Of), (double)row[12]!, 12);
            }

            Assert.Equal((double)line.Points, row[13]);
        }

        Assert.Contains(order, id => IssueCatalog.Find(id)!.ReportedOnce);

        foreach (var row in checks.Skip(order.Count))
        {
            var id = (string)row[7]!;
            var plain = PlainLimits.Fill(IssueCatalog.Find(id)!.Plain!, settings);
            Assert.Null(row[0]);
            Assert.Null(row[1]);

            // Named by what the check looks for, where it says: "Nothing wrong" beside a title that states a fault would misread.
            Assert.Equal(plain.LooksFor ?? plain.Title, row[5]);
            if (results.Counts.ContainsKey(id))
            {
                Assert.Equal("Nothing wrong", row[2]);
                Assert.Equal(0d, row[9]);
            }
            else
            {
                Assert.Equal("Couldn’t run", row[2]);
                Assert.Null(row[9]);
                Assert.False(string.IsNullOrEmpty((string?)row[14]));
            }
        }
    }

    private sealed class Heard(Action<ExportProgress> heard) : IProgress<ExportProgress>
    {
        public void Report(ExportProgress value) => heard(value);
    }
}
