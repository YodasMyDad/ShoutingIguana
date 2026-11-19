using System.Globalization;
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

        // The sheets in the report's order, by its labels: each job's, then those of the checks folded under it ("2a").
        var listed = GridListing.Listed(order);
        for (var number = 1; number <= listed.Count; number++)
        {
            var check = IssueCatalog.Find(listed[number - 1].Id)!;
            var sheet = file.Sheets[number];
            Assert.True(sheet.Name.Length <= 31, sheet.Name);
            Assert.StartsWith($"{listed[number - 1].Sheet} ", sheet.Name, StringComparison.Ordinal);
            Assert.Equal(1, sheet.FrozenRows);
            Assert.Equal(Titles(check.Id), sheet.Values[0]);
            Assert.All(sheet.Rows[0], cell => Assert.Equal(1u, cell.Style));
            AssertHoldsTheChecksRows(check.Id, sheet.Values);
        }

        AssertSummary(file.Sheets[0].Values, results, order, [.. file.Sheets.Select(sheet => sheet.Name)]);
    }

    /// <summary>Discovery decision 2: a check the user hid is left out of the workbook's sheets and its summary, which says so.</summary>
    /// <summary>
    /// The "can I fix it from this?" pass: a sheet holds no column that's empty on every row, no "Other address" beside the
    /// column naming the same URLs, no final URL that's only ever the address; and a check of the site's files names them.
    /// </summary>
    [Fact]
    public void ASheetHoldsOnlyColumnsThatSaySomething()
    {
        var path = Path.Combine(_folder, "shape.xlsx");
        CrawlExporter.Export(_crawl.Path, new ExportRequest { Kind = ExportKind.Workbook, Format = ExportFormat.Xlsx }, path);
        var order = IssueReader.ReadResults(_crawl.Path)!.FixFirst.ToList();
        var sheets = XlsxFile.Read(path).Sheets;
        var listed = GridListing.Listed(order).Select(each => each.Id).ToList();
        object?[] Header(string checkId) => sheets[listed.IndexOf(checkId) + 1].Values[0];

        Assert.Equal(["Address", "Page title"], Header("canonical-missing"));
        Assert.Equal(["Address", "Page title", "What was found", "Canonical"], Header("canonical-broken"));
        Assert.DoesNotContain("Final URL", Header("sitemap-non-indexable"));
        Assert.Equal(["File", "What was found"], Header("sitemap-error"));
        Assert.Equal($"{_crawl.Server.BaseUri.GetLeftPart(UriPartial.Authority)}/sitemaps/missing.xml", sheets[listed.IndexOf("sitemap-error") + 1].Values[1][0]);
    }

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
            Assert.Equal(Titles(check.Id), rows[0]);
            AssertHoldsTheChecksRows(check.Id, rows, guarded: true);
            return;
        }

        var sheet = Assert.Single(XlsxFile.Read(path).Sheets);
        Assert.StartsWith("3 ", sheet.Name, StringComparison.Ordinal);
        Assert.Equal(Titles(check.Id), sheet.Values[0]);
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
        var order = IssueReader.ReadResults(_crawl.Path)!.FixFirst.ToList();
        string[] hidden = [order[0], "a-check-from-another-version"];
        var check = order[4];

        var workbook = Path.Combine(_folder, "hidden-for-one.xlsx");
        CrawlExporter.Export(_crawl.Path, new ExportRequest { Kind = ExportKind.Workbook, Format = ExportFormat.Xlsx, Report = new() { HiddenChecks = hidden } }, workbook);
        var listed = GridListing.Listed([.. order.Where(id => !hidden.Contains(id))]);
        var place = listed.FindIndex(each => each.Id == check);
        var inTheWorkbook = XlsxFile.Read(workbook).Sheets[place + 1].Name;
        Assert.StartsWith($"{listed[place].Sheet} ", inTheWorkbook, StringComparison.Ordinal);

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
        Assert.Equal([Titles(clean.Id)], Assert.Single(XlsxFile.Read(path).Sheets).Values);
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

    /// <summary>A sheet's rows are the check's rows, as its grid reads them when no order is asked for: by importance.</summary>
    private void AssertHoldsTheChecksRows(string checkId, List<object?[]> sheet, bool guarded = false)
    {
        var shape = Shape(checkId);
        var rows = GridRows(checkId, SHEET_COLUMNS);
        Assert.Equal(rows.Count, sheet.Count - 1);

        // A check of the site's files names them, one a line, where the row would name the home page.
        if (shape.Files is { } files)
        {
            Assert.Equal([string.Join("\n", files), rows[0][3] as string], sheet[1]);
            return;
        }

        for (var row = 0; row < rows.Count; row++)
        {
            for (var column = 0; column < shape.Columns.Length; column++)
            {
                var expected = rows[row][Array.IndexOf(SHEET_COLUMNS, shape.Columns[column]) + 1] as string;
                expected = string.IsNullOrEmpty(expected) ? null : guarded && "=+-@\t\r".Contains(expected[0], StringComparison.Ordinal) ? "'" + expected : expected;
                Assert.True(Equals(expected, sheet[row + 1][column]), $"{checkId}, row {row + 1}, {shape.Titles[column]}: the grid has '{expected}', the sheet '{sheet[row + 1][column]}'.");
            }
        }

        AssertSaysWhereToFix(checkId, [.. rows.Select(row => Convert.ToInt32(row[0], CultureInfo.InvariantCulture))], sheet);
    }

    /// <summary>A check's rows as its grid gives them, the id first, then <paramref name="columns"/>.</summary>
    private List<IReadOnlyList<object?>> GridRows(string checkId, string[] columns)
    {
        List<IReadOnlyList<object?>> rows = [];
        for (var offset = 0; ; offset += 1_000)
        {
            var page = IssueGroupReader.ReadRows(_crawl.Path, checkId, new UrlQuery { Columns = columns, Offset = offset, Limit = 1_000 })!.Page!;
            Assert.Equal(["id", .. columns], page.Columns);
            rows.AddRange(page.Rows.Select(row => row.Values));
            if (rows.Count >= page.Matching)
            {
                break;
            }
        }

        return rows;
    }

    /// <summary>
    /// What a check's sheet holds, worked out from its grid rows: the address, then the page's title, what was found and the
    /// other address only where some row has one (the other address not where where-to-fix names the same URLs), then its
    /// evidence but a final URL or canonical that's always the address itself. A check of the site's files: the files.
    /// </summary>
    private (string[] Columns, string[] Titles, string[] Evidence, IReadOnlyList<string>? Files) Shape(string checkId)
    {
        var check = IssueCatalog.Find(checkId)!;
        if (check.About is not null && IssueFileReader.Read(_crawl.Path)!.Checks.GetValueOrDefault(checkId) is { } files)
        {
            return (["url", "detail"], ["File", "What was found"], [], [.. files.Select(file => file.Url)]);
        }

        var rows = GridRows(checkId, [.. SHEET_COLUMNS, "finalUrl", "canonicalUrl"]);
        bool Any(int column) => rows.Any(row => row[column] is string { Length: > 0 });
        bool Elsewhere(int column) => rows.Any(row => row[column] is string { Length: > 0 } other && other != row[1] as string);
        bool[] keep = [true, Any(2), Any(3), Any(4) && check.References != IssueReferences.Targets];
        string[] evidence =
        [
            .. check.Evidence.Where(column => column != "title" && column switch { "finalUrl" => Elsewhere(5), "canonicalUrl" => Elsewhere(6), _ => true })
        ];
        return ([.. SHEET_COLUMNS.Where((_, index) => keep[index])], [.. SHEET_TITLES.Where((_, index) => keep[index])], evidence, null);
    }

    /// <summary>
    /// A check's file's header: its sheet's, its evidence (but the page's title, already there), then where to fix each URL
    /// where the check's URLs are referred to, named by what a page's targets are.
    /// </summary>
    private object?[] Titles(string checkId)
    {
        var check = IssueCatalog.Find(checkId)!;
        var shape = Shape(checkId);
        if (shape.Files is not null)
        {
            return [.. shape.Titles];
        }

        object?[] where = check.References switch
        {
            IssueReferences.Sources => ["Linked from (pages)", "Linked from", "Listed in sitemaps"],
            IssueReferences.Targets when check.Category == IssueCategory.Canonicals => ["Canonical"],
            IssueReferences.Targets when check == IssueCatalog.IMAGE_TOO_LARGE => ["Images (addresses)", "Images", "Largest image (bytes)"],
            IssueReferences.Targets when check == IssueCatalog.PROTOCOL_RELATIVE_LINKS => ["URLs without a scheme (addresses)", "URLs without a scheme"],
            IssueReferences.Targets => check.Category switch
            {
                IssueCategory.Images => ["Images (addresses)", "Images"],
                IssueCategory.Hreflang => ["Alternates (addresses)", "Alternates"],
                IssueCategory.Security => ["HTTP resources (addresses)", "HTTP resources"],
                _ => ["Links to (addresses)", "Links to"]
            },
            _ => []
        };
        return [.. shape.Titles, .. EvidenceTitles(shape.Evidence), .. where];
    }

    /// <summary>The titles of a check's evidence columns on its sheet, its unit in brackets: "Title length", "Response time (ms)".</summary>
    private static string[] EvidenceTitles(string[] evidence) =>
        [.. evidence.Select(column => GridExport.Title(UrlGridReader.IssueRowColumns.Single(info => info.Id == column)))];

    /// <summary>
    /// Each row's added cells say where to fix it, as the found-on reader reads it: how many pages link to the URL and the
    /// first of them, or how many URLs the page's issue names and the first of them.
    /// </summary>
    private void AssertSaysWhereToFix(string checkId, IReadOnlyList<int> ids, List<object?[]> sheet)
    {
        var check = IssueCatalog.Find(checkId)!;
        var references = check.References;
        var shape = Shape(checkId);
        var first = shape.Columns.Length + shape.Evidence.Length;
        if (references == IssueReferences.None)
        {
            Assert.All(sheet, row => Assert.True(row.Length <= first, $"{checkId}: {row.Length} cells"));
            return;
        }

        foreach (var (id, index) in ids.Select((id, index) => (id, index)).Take(FoundOnReader.MAX_URLS))
        {
            var row = sheet[index + 1];
            string? Cell(int column) => row.Length > first + column && row[first + column] is { } value ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;
            if (references == IssueReferences.Sources)
            {
                var found = FoundOnReader.ReadFoundOn(_crawl.Path, [id], WhereFoundColumns.LISTED).Single();
                Assert.Equal(found.Pages.ToString(CultureInfo.InvariantCulture), Cell(0));
                if (found.Sources.Count > 0)
                {
                    Assert.StartsWith(found.Sources[0].Url, Cell(1)!.TrimStart('\''),StringComparison.Ordinal);
                }

                Assert.Equal(found.Sitemaps.Total > 0, Cell(2) is not null);
            }
            else
            {
                // A canonical check's one cell is the canonical; any other's, how many, then the first of them with what each is.
                var named = FoundOnReader.ReadTargets(_crawl.Path, checkId, [id], WhereFoundColumns.LISTED)!.Single();
                var listed = check.Category == IssueCategory.Canonicals ? Cell(0) : Cell(1);
                if (check.Category != IssueCategory.Canonicals)
                {
                    Assert.Equal(named.Total.ToString(CultureInfo.InvariantCulture), Cell(0));
                }

                if (named.Targets.Count > 0)
                {
                    Assert.StartsWith(WhereFoundColumns.Target(check, named.Targets[0]), listed!, StringComparison.Ordinal);
                }
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

        // In the report's order: a check folded under its job has no number of its own, and its sheet's is its job's with a letter.
        var listed = GridListing.Listed(order);
        for (var number = 1; number <= listed.Count; number++)
        {
            var row = checks[number - 1];
            var check = IssueCatalog.Find(listed[number - 1].Id)!;
            Assert.Equal(listed[number - 1].Folded ? null : (double)listed[number - 1].Number, row[0]);
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
            var line = score.Lines.Single(each => each.CheckId == check.Id);
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
