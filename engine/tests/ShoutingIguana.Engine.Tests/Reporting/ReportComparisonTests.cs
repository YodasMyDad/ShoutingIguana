using System.Globalization;
using AngleSharp.Html.Parser;
using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Export;
using ShoutingIguana.Engine.Export.Models;
using ShoutingIguana.Engine.Reporting;
using ShoutingIguana.Engine.Reporting.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;

namespace ShoutingIguana.Engine.Tests.Reporting;

/// <summary>
/// The report and the workbook of a crawl compared with the site's last check (the fix loop): revision 1 of the synthetic
/// site "after the fixes" against revision 0. The report gains "Since your last check" after its summary (a line of it in
/// the summary cut) and says what it was compared with; the workbook gains each check's count then and its change, and a
/// sheet of every row the comparison says was fixed, gone or not checked.
/// </summary>
[Collection(ComparisonCrawlsCollection.NAME)]
public sealed class ReportComparisonTests(ComparisonCrawls crawls) : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("si-report-compare-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void TheReportSaysWhatChangedSinceTheLastCheck()
    {
        var comparison = CrawlComparisonReader.ReadSummary(crawls.Now, crawls.Then)!;
        var startedThen = CrawlSummaryReader.Read(crawls.Then).StartedAt;
        var on = ReportWords.Date(startedThen, TimeZoneInfo.Local);
        var settings = IssueReader.ReadAnalysis(crawls.Now)!.Settings!;
        var document = new HtmlParser().ParseDocument(ReportExport.Build(crawls.Now, new ReportOptions { CompareWith = crawls.Then }));

        // After the summary, before Fix these first.
        var headings = document.QuerySelectorAll("h2").Select(heading => heading.TextContent).ToList();
        Assert.Equal($"Since your last check on {on}", headings[0]);
        Assert.Equal("Fix these first", headings[1]);
        var score = comparison.Score!;
        Assert.StartsWith(score.Change switch
        {
            0 => $"The score is {score.Now.Value}, as it was on {on}.",
            > 0 => $"The score was {score.Then.Value} on {on}, and is {score.Now.Value} now: up {score.Change}.",
            _ => $"The score was {score.Then.Value} on {on}, and is {score.Now.Value} now: down {-score.Change}."
        }, document.QuerySelector("h2 + .lead")!.TextContent, StringComparison.Ordinal);

        // Each group lists its checks by their words, in the comparison's order.
        var groups = document.QuerySelectorAll("h3.group-title").ToList();
        foreach (var (state, name) in new[] { (CheckChangeState.Fixed, "Fixed"), (CheckChangeState.New, "New"), (CheckChangeState.Fewer, "Got better"), (CheckChangeState.More, "Got worse") })
        {
            List<string> titles = [.. comparison.Checks.Where(check => check.State == state).Select(check => ReportSources.Words(IssueCatalog.Find(check.CheckId)!, settings).Title + ".")];
            var group = groups.SingleOrDefault(title => title.FirstChild?.TextContent.Trim() == name);
            if (titles.Count == 0)
            {
                Assert.Null(group);
                continue;
            }

            Assert.Equal($"{name} {titles.Count}", group!.TextContent);
            Assert.Equal(titles, group.NextElementSibling!.QuerySelectorAll("li b").Select(title => title.TextContent));
        }

        Assert.Contains(comparison.Checks, check => check.State == CheckChangeState.Fixed);
        Assert.Contains(comparison.Checks, check => check is { CheckId: "security-headers-missing", State: CheckChangeState.New });

        // About this check names the check it was compared with.
        var facts = Facts(document);
        Assert.Equal($"The check of {on}, {ReportWords.Time(startedThen, TimeZoneInfo.Local)}", facts["Compared with"]);
        Assert.False(facts.ContainsKey("Rechecked"));

        // The summary cut has it in a line: the score and the counts.
        var summary = new HtmlParser().ParseDocument(ReportExport.Build(crawls.Now, new ReportOptions { Cut = ReportCut.Summary, CompareWith = crawls.Then }));
        Assert.DoesNotContain(summary.QuerySelectorAll("h2"), heading => heading.TextContent.StartsWith("Since", StringComparison.Ordinal));
        var line = summary.QuerySelectorAll("p.note").Single(note => note.TextContent.StartsWith("Since your last check", StringComparison.Ordinal)).TextContent;
        var fixedCount = comparison.Checks.Count(check => check.State == CheckChangeState.Fixed);
        Assert.StartsWith($"Since your last check on {on}: The score ", line, StringComparison.Ordinal);
        Assert.Contains($"{fixedCount} problems fixed", line, StringComparison.Ordinal);

        // In English (the UX audit's S-11, A-12): "2 problems fixed, 1 new, 3 got better, 2 got worse.", never "2 on more."
        int Of(CheckChangeState state) => comparison.Checks.Count(check => check.State == state);
        Assert.True(Of(CheckChangeState.More) > 0 && Of(CheckChangeState.Fewer) > 0, "The comparison crawls have a problem on more pages and one on fewer.");
        Assert.Contains($", {Of(CheckChangeState.Fewer)} got better", line, StringComparison.Ordinal);
        Assert.Contains($", {Of(CheckChangeState.More)} got worse.", line, StringComparison.Ordinal);
        Assert.DoesNotContain(" on more", line, StringComparison.Ordinal);
        Assert.DoesNotContain(" on fewer", line, StringComparison.Ordinal);
    }

    /// <summary>The counts of the summary's line, the first with its noun: a line of one count reads alone ("2 problems got worse.").</summary>
    [Theory]
    [InlineData(2, 0, 0, 2, "2 problems fixed, 2 got worse.")]
    [InlineData(1, 1, 1, 1, "1 problem fixed, 1 new, 1 got better, 1 got worse.")]
    [InlineData(0, 2, 0, 0, "2 new problems.")]
    [InlineData(0, 0, 3, 1, "3 problems got better, 1 got worse.")]
    [InlineData(0, 0, 0, 1, "1 problem got worse.")]
    [InlineData(0, 0, 0, 0, "No problem was fixed, and none is new.")]
    public void TheSummaryLineCountsWhatChangedInEnglish(int fixedCount, int added, int fewer, int more, string said) =>
        Assert.Equal(said, ReportWords.ChangeCounts(fixedCount, added, fewer, more));

    /// <summary>Without a comparison, nothing of it; one that can't be made is said under About this check, and the report is still made.</summary>
    [Fact]
    public void AReportNotComparedSaysNothingOrWhyNot()
    {
        var plain = new HtmlParser().ParseDocument(ReportExport.Build(crawls.Now));
        Assert.DoesNotContain(plain.QuerySelectorAll("h2"), heading => heading.TextContent.StartsWith("Since", StringComparison.Ordinal));
        Assert.False(Facts(plain).ContainsKey("Compared with"));

        var named = new HtmlParser().ParseDocument(ReportExport.Build(crawls.Now, new ReportOptions { NotCompared = "Not compared: that crawl is of another site." }));
        Assert.Equal("Not compared: that crawl is of another site.", Facts(named)["Compared with"]);

        var unanalysed = crawls.Copy(crawls.Then);
        Execute(unanalysed, "DROP TABLE analysis");
        var never = new HtmlParser().ParseDocument(ReportExport.Build(crawls.Now, new ReportOptions { CompareWith = unanalysed }));
        Assert.Equal(ReportReader.NOT_COMPARED_UNANALYSED, Facts(never)["Compared with"]);
        Assert.DoesNotContain(never.QuerySelectorAll("h2"), heading => heading.TextContent.StartsWith("Since", StringComparison.Ordinal));

        // Analysed by other rules: only the counts, said in one sentence.
        var older = crawls.Copy(crawls.Then);
        Execute(older, $"UPDATE analysis SET analysis_version = {AnalysisFreshness.VERSION - 1}");
        var counted = new HtmlParser().ParseDocument(ReportExport.Build(crawls.Now, new ReportOptions { CompareWith = older }));
        Assert.Contains("only the problems’ counts are compared until they are worked out again", counted.QuerySelector("h2 + .lead")!.TextContent, StringComparison.Ordinal);
    }

    /// <summary>The workbook's summary gains each check's count then and its change, and a sheet lists every row the comparison cleared.</summary>
    [Fact]
    public void TheWorkbookGainsTheCountThenAndASheetOfWhatWasFixed()
    {
        var path = Path.Combine(_folder, "compared.xlsx");
        var result = CrawlExporter.Export(crawls.Now, new ExportRequest { Kind = ExportKind.Workbook, Format = ExportFormat.Xlsx, Report = new ReportOptions { CompareWith = crawls.Then } },
            path);
        var comparison = CrawlComparisonReader.ReadSummary(crawls.Now, crawls.Then)!;
        var results = IssueReader.ReadResults(crawls.Now)!;
        var file = XlsxFile.Read(path);
        var day = TimeZoneInfo.ConvertTime(CrawlSummaryReader.Read(crawls.Then).StartedAt, TimeZoneInfo.Local).ToString("d MMM", CultureInfo.InvariantCulture);

        var summary = file.Sheets[0].Values;
        var header = summary.FindIndex(row => (row[0] as string) == "Number");
        Assert.Equal(new object?[] { $"Found on {day}", "Change" }, summary[header][15..17]);
        Assert.Contains(summary, row => row[0] as string == "Compared with");
        foreach (var check in comparison.Checks.Where(check => IssueCatalog.Find(check.CheckId) is not null))
        {
            var row = summary.Skip(header + 1).Single(row => row[7] as string == check.CheckId);
            Assert.Equal(check.Then is { } then ? (double)then : null, row[15]);
            Assert.Equal(check.State switch
            {
                CheckChangeState.NewCheck => "Not looked for then",
                CheckChangeState.NotCheckedNow => "Not checked now",
                var state => state.ToString()
            }, row[16]);
        }

        // Every row the comparison cleared, check by check, in its order.
        List<string> expected = [];
        foreach (var check in comparison.Checks.Where(check => check.Then > 0 && IssueCatalog.Find(check.CheckId) is not null))
        {
            var cleared = CrawlComparisonReader.ReadCleared(crawls.Now, crawls.Then, check.CheckId, new ClearedQuery(Limit: UrlGridReader.MAX_LIMIT))!;
            expected.AddRange(cleared.Rows.Select(row => $"{check.CheckId} {row.Url} {row.DetailThen}"));
        }

        var sheet = file.Sheets[^1];
        Assert.Equal($"Fixed since {day}", sheet.Name);
        Assert.Equal(new object?[] { "Problem", "Check ID", "Address", "Now", "Why not checked", "What was found then", "Other address then" }, sheet.Values[0]);
        Assert.Equal(expected, sheet.Values.Skip(1).Select(row => $"{row[1]} {row[2]} {row[5]}"));
        Assert.Contains(sheet.Values, row => row[3] as string == "No longer linked");
        Assert.Equal(results.FixFirst.Sum(id => results.Counts[id]) + expected.Count, result.Rows);
    }

    private static Dictionary<string, string> Facts(AngleSharp.Dom.IDocument document) =>
        document.QuerySelectorAll(".facts dt").ToDictionary(term => term.TextContent, term => term.NextElementSibling!.TextContent);

    private static void Execute(string path, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
