using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Export;
using ShoutingIguana.Engine.Export.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Reporting;
using ShoutingIguana.Engine.Reporting.Models;
using ShoutingIguana.Engine.Serialization;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using static ShoutingIguana.Engine.Tests.Support.GridQueries;

namespace ShoutingIguana.Engine.Tests.Reporting;

/// <summary>
/// The report, read back as a browser parses it: its outline and its numbers against the analysis, the score and the
/// words as the analysis and the catalog have them, each state the design names, both cuts, and that nothing a site
/// sends becomes markup, a link to anything but http or https, or a request.
/// </summary>
[Collection(GridCrawlCollection.NAME)]
public sealed class ReportDocumentTests(GridCrawl crawl) : IDisposable
{
    private static readonly ExportSettings SETTINGS = new()
    {
        Clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 1, 17, 42, 0, TimeSpan.Zero))
    };

    private readonly string _folder = Directory.CreateTempSubdirectory("si-report-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void TheReportSaysWhatTheAnalysisFound()
    {
        var results = IssueReader.ReadResults(crawl.Path)!;
        var score = results.Score!;
        var settings = results.Analysis.Settings!;

        // The order to fix them in is the engine's, as the analysis gives it: the report has none of its own.
        var order = results.FixFirst;
        Assert.Equal(FixFirstOrder.Of(results.Counts), order);
        var document = Parse(ReportExport.Build(crawl.Path, settings: SETTINGS));

        Assert.Equal("en-GB", document.DocumentElement.GetAttribute("lang"));
        Assert.Equal("127.0.0.1", document.QuerySelector("h1")!.TextContent);
        Assert.StartsWith("Website check: 127.0.0.1, ", document.Title, StringComparison.Ordinal);
        var meta = document.QuerySelector("p.meta")!.TextContent;
        Assert.Contains($"{ReportWords.Number(results.Analysis.PagesAnalyzed)} pages read", meta, StringComparison.Ordinal);

        // The addresses are the ones the analysis counted, which its shares are of.
        Assert.Equal(results.Analysis.CrawledUrlCount, score.Covers.Urls);
        Assert.Contains($"{ReportWords.Number(score.Covers.Urls)} addresses checked", meta, StringComparison.Ordinal);
        Assert.Contains($"{results.Counts.Count} of {IssueCatalog.All.Count} checks run", meta, StringComparison.Ordinal);

        // The outline, in order: what couldn't be checked is there when something couldn't.
        var skipped = IssueCatalog.All.Count - results.Counts.Count;
        string[] outline = ["Fix these first", "How the score is worked out", "What to fix", "What couldn’t be checked", "What was checked and found nothing wrong", "About this check"];
        Assert.Equal(outline.Where(heading => skipped > 0 || heading != "What couldn’t be checked"), document.QuerySelectorAll("h2").Select(heading => heading.TextContent));

        // The five counts.
        int Of(IssueSeverity severity) => order.Count(id => IssueCatalog.Find(id)!.Severity == severity);
        Assert.Equal(
            [Of(IssueSeverity.Error), Of(IssueSeverity.Warning), Of(IssueSeverity.Notice), results.Counts.Count(count => count.Value == 0), IssueCatalog.All.Count - results.Counts.Count],
            document.QuerySelectorAll(".tally b").Select(count => int.Parse(count.TextContent)));

        // The problems a fix of another of their severity clears entirely, as the analysis has them, are the two GridListing
        // folds under the 4xx: they're one job with it, and the report lists them so.
        var causes = IssueCauseReader.Read(crawl.Path)!.Checks;
        Assert.Equal(
            GridListing.FOLDED.Select(id => (GridListing.JOB, id)),
            order.SelectMany(id => causes[id].Clears.Where(cleared => cleared.All && order.Contains(cleared.CheckId)
                    && IssueCatalog.Find(cleared.CheckId)!.Severity == IssueCatalog.Find(id)!.Severity).Select(cleared => (id, cleared.CheckId))));
        var inReport = GridListing.Listed(order);

        // Every check that found something has its section, the jobs numbered in the order to fix them, each followed by
        // what's folded under it: its plain words, with its technical name beside them, how much of the site it is on and
        // what it costs the score.
        var problems = document.QuerySelectorAll("article.problem").ToList();
        Assert.Equal(order.Count, problems.Count);
        Assert.Contains(order, id => IssueCatalog.Find(id)!.ReportedOnce);
        for (var index = 0; index < inReport.Count; index++)
        {
            var check = IssueCatalog.Find(inReport[index].Id)!;
            var plain = PlainLimits.Fill(check.Plain!, settings);
            var line = score.Lines.Single(each => each.CheckId == check.Id);
            Assert.Equal(inReport[index].Folded ? plain.Title : $"{inReport[index].Number}. {plain.Title}", problems[index].QuerySelector("h3 > span")!.TextContent);

            // In plain words (the default) the check's technical name isn't beside the title, in a client's way: it is listed
            // once under About this check. Its section says which check it is, for the "Fix these first" list and the app.
            Assert.Null(problems[index].QuerySelector(".problem__tech"));
            Assert.Equal(($"problem-{check.Id}", check.Id), (problems[index].Id, problems[index].GetAttribute("data-check")));

            // A check reported once for the whole site says so: "1 of 240 pages" would say the fault is one page's.
            var share = check.ReportedOnce
                ? "Checked once, for the whole site"
                : $"{ReportWords.Number(line.Count)} of {ReportWords.Number(line.Of)} {(line.Unit == ScoreUnit.Pages ? (line.Of == 1 ? "page" : "pages") : (line.Of == 1 ? "address" : "addresses"))}";
            var points = line.Points switch { 0 => "doesn’t change the score", 1 => "takes 1 point off the score", _ => $"takes {line.Points} points off the score" };
            var who = plain.Fixer switch { IssueFixer.You => "you, in your site’s editor", IssueFixer.Developer => "your developer or hosting company", _ => "you or your developer" };
            Assert.Equal($"{share} · {points} · usually fixed by {who}", problems[index].QuerySelector(".problem__meta")!.TextContent);
            Assert.Equal(string.Join(' ', plain.What), problems[index].QuerySelector("h4 + p")!.TextContent);
            foreach (var step in plain.Fix)
            {
                Assert.Contains(step, problems[index].TextContent, StringComparison.Ordinal);
            }

            var listed = problems[index].QuerySelectorAll(".examples tbody tr").Length;
            var most = check.Severity == IssueSeverity.Notice ? ReportOptions.DEFAULT_LOOK_EXAMPLES : ReportOptions.DEFAULT_EXAMPLES;
            Assert.Equal((int)Math.Min(most, results.Counts[check.Id]), listed);

            // They are the check's first rows, in the order its list has them (by importance), each linked to its own address;
            // a check of the site's files (reported once, on the home page) names its first file instead.
            // A missing address leads with the page that holds the link to it (the page to edit), and keeps one page's links
            // together: its own address is the second cell's, and the rows are the list's first, whatever their order.
            var files = check.About is null ? null : IssueFileReader.Read(crawl.Path)!.Checks.GetValueOrDefault(check.Id);
            var linkFirst = check == IssueCatalog.INTERNAL_CLIENT_ERROR;
            var addresses = problems[index].QuerySelectorAll(".examples tbody tr").Select(row => row.QuerySelector(linkFirst ? "td:nth-child(2) .mono a" : "td .mono a")!.GetAttribute("href")!).ToList();
            IEnumerable<string> expected = files is { Count: > 0 } ? [files[0].Url] : FirstRows(check.Id, most).Select(row => (string)row.Values[1]!);
            Assert.Equal(linkFirst ? expected.Order(StringComparer.Ordinal) : expected, linkFirst ? addresses.Order(StringComparer.Ordinal) : addresses);
            if (results.Counts[check.Id] > most)
            {
                Assert.Equal($"And {results.Counts[check.Id] - most:N0} more: the full list is on sheet {inReport[index].Sheet} of the spreadsheet.", problems[index].QuerySelector(".more")!.TextContent);
            }
        }

        // The technical names, once: each problem's plain title, linked to its section, with what a developer calls it.
        Assert.Equal(
            inReport.Select(listed => (PlainLimits.Fill(IssueCatalog.Find(listed.Id)!.Plain!, settings).Title, $"#problem-{listed.Id}", IssueCatalog.Find(listed.Id)!.Title, IssueCatalog.Find(listed.Id)!.Severity.ToString())),
            document.QuerySelectorAll("table.names tbody tr").Select(row => (row.QuerySelector("a")!.TextContent, row.QuerySelector("a")!.GetAttribute("href")!, row.Children[1].TextContent, row.Children[2].TextContent)));

        // The first list is the first five problems to fix, under the same numbers: the ones the score says it would be without.
        var first = document.QuerySelectorAll("ol.first li").ToList();
        Assert.Equal(5, first.Count);
        Assert.Equal(["1", "2", "3", "4", "5"], first.Select(item => item.QuerySelector(".first__n")!.TextContent));
        Assert.Equal(score.IfFixed.CheckIds, order.Take(5));
        Assert.Equal(order.Take(5).Select(id => PlainLimits.Fill(IssueCatalog.Find(id)!.Plain!, settings).Title), first.Select(item => item.QuerySelector(".first__title")!.TextContent));
        Assert.StartsWith(
            score.IfFixed.Value == score.Value ? $"Fixing these five leaves the score at {score.Value} " : $"Fixing these five takes the score from {score.Value} to {score.IfFixed.Value}. ",
            document.QuerySelector(".first__foot")!.TextContent, StringComparison.Ordinal);

        // Every check that passed has its good news, under its category's plain title; every one that couldn't run has its reason.
        var passed = IssueCatalog.All.Where(check => results.Counts.GetValueOrDefault(check.Id, -1) == 0).ToList();
        Assert.Equal(passed.Select(check => PlainLimits.Fill(check.Plain!, settings).Passed), document.QuerySelectorAll(".passed li").Select(item => item.TextContent));
        Assert.Equal(passed.Select(check => PlainWords.CategoryTitle(check.Category)).Distinct(), document.QuerySelectorAll(".passed h4").Select(title => title.TextContent));
        Assert.Equal(IssueCatalog.All.Count - results.Counts.Count, document.QuerySelectorAll(".plain-list li").Length);
        Assert.Contains($"Score version {ScoreCalculator.VERSION} · engine ", document.QuerySelector("footer")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("Made with Shouting Iguana", document.QuerySelector("footer")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("on 1 November 2026", document.QuerySelector("footer")!.TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    /// Gallery 7's view 20: Simple's Summary and What to fix fold a problem that a fix of another clears entirely (the
    /// analysis's clears, all) under that one, in the same severity, as one job, numbering only the jobs. So does the
    /// report: in "Fix these first" and in What to fix the folded one has no number and follows its job, "Fixed along
    /// with the problem above", the jobs are numbered 1 to 4 and "Fixing these four" counts them. Its full list is on its
    /// job's sheet's number with a letter, "2a", in the workbook as in the report.
    /// </summary>
    [Fact]
    public void AProblemAFixOfAnotherClearsEntirelyIsOneJobWithIt()
    {
        // Every page linking to a broken address links only to the site's 4xx ones: restoring those clears every one.
        var copy = crawl.Copy();
        Change(copy, """
            DELETE FROM issues WHERE issue = 'broken-internal-links' AND url_id IN (
                SELECT t.url_id FROM issue_targets t WHERE t.issue = 'broken-internal-links'
                AND NOT EXISTS (SELECT 1 FROM issues i WHERE i.issue = 'internal-client-error' AND i.url_id = t.related_url_id));
            DELETE FROM issue_targets WHERE issue = 'broken-internal-links' AND url_id NOT IN (SELECT url_id FROM issues WHERE issue = 'broken-internal-links');
            UPDATE issue_types SET url_count = (SELECT COUNT(*) FROM issues WHERE issue = 'broken-internal-links') WHERE id = 'broken-internal-links';
            """);
        StoreTheScoreOfItsCounts(copy);
        var results = IssueReader.ReadResults(copy)!;
        var score = results.Score!;
        var order = results.FixFirst;
        // What restoring the 4xx clears entirely, among the serious problems: the broken links, and others of the synthetic site.
        var folded = IssueCauseReader.Read(copy)!.Checks["internal-client-error"].Clears
            .Where(cleared => cleared.All && order.Contains(cleared.CheckId) && IssueCatalog.Find(cleared.CheckId)!.Severity == IssueSeverity.Error)
            .Select(cleared => cleared.CheckId).ToList();
        Assert.Equal(["broken-internal-links", "canonical-broken", "hreflang-broken-target"], folded);
        Assert.Equal(["no-https", "internal-client-error", "broken-internal-links", "broken-external-links", "redirect-loop"], order.Take(5));
        Assert.Equal(score.IfFixed.CheckIds, order.Take(5));
        Assert.DoesNotContain(order, id => id != "internal-client-error" && IssueCatalog.Find(id)!.Clears.Count > 0
            && IssueCauseReader.Read(copy)!.Checks[id].Clears.Any(cleared => cleared.All && order.Contains(cleared.CheckId) && IssueCatalog.Find(cleared.CheckId)!.Severity == IssueCatalog.Find(id)!.Severity));

        var document = Parse(ReportExport.Build(copy, settings: SETTINGS));
        string Title(string id) => PlainLimits.Fill(IssueCatalog.Find(id)!.Plain!, results.Analysis.Settings!).Title;

        // Fix these first: the engine's five as four jobs numbered 1 to 4, the broken links under the 4xx, unnumbered.
        var first = document.QuerySelectorAll("ol.first > li").ToList();
        Assert.Equal(order.Take(5).Select(Title), first.Select(item => item.QuerySelector(".first__title")!.TextContent));
        Assert.Equal(["1", "2", "", "3", "4"], first.Select(item => item.QuerySelector(".first__n")!.TextContent));
        Assert.Equal([false, false, true, false, false], first.Select(item => item.ClassList.Contains("first__also")));
        Assert.Equal("Fixed along with the problem above", first[2].QuerySelector(".first__along")!.TextContent);
        Assert.Empty(first[2].QuerySelectorAll("svg"));
        Assert.StartsWith(
            score.IfFixed.Value == score.Value ? $"Fixing these four leaves the score at {score.Value} " : $"Fixing these four takes the score from {score.Value} to {score.IfFixed.Value}. ",
            document.QuerySelector(".first__foot")!.TextContent, StringComparison.Ordinal);

        // What to fix: the jobs numbered from 1 in the order to fix them, each followed by what's folded under it, unnumbered.
        List<string> listed = [.. order.Where(id => !folded.Contains(id))];
        listed.InsertRange(listed.IndexOf("internal-client-error") + 1, folded);
        var problems = document.QuerySelectorAll("article.problem").ToList();
        Assert.Equal(listed.Count, problems.Count);
        var number = 0;
        List<string> sheets = [];
        for (var index = 0; index < listed.Count; index++)
        {
            var isFolded = folded.Contains(listed[index]);
            number += isFolded ? 0 : 1;
            var sheet = isFolded ? $"2{(char)('a' + folded.IndexOf(listed[index]))}" : $"{number}";
            sheets.Add(sheet);
            Assert.Equal(isFolded ? Title(listed[index]) : $"{number}. {Title(listed[index])}", problems[index].QuerySelector("h3 > span")!.TextContent);
            Assert.Equal(isFolded, problems[index].ClassList.Contains("problem--along"));
            Assert.Equal(isFolded ? "Fixed along with the problem above." : null, problems[index].QuerySelector(".problem__along")?.TextContent);
            if (problems[index].QuerySelector(".more") is { } more)
            {
                Assert.EndsWith($" on sheet {sheet} of the spreadsheet.", more.TextContent, StringComparison.Ordinal);
            }
        }

        Assert.Equal(["1", "2", "2a", "2b", "2c", "3"], sheets.Take(6));

        // The workbook's sheets are the report's, in its order and by its labels; the summary numbers the jobs only.
        var path = Path.Combine(_folder, "jobs.xlsx");
        CrawlExporter.Export(copy, new ExportRequest { Kind = ExportKind.Workbook, Format = ExportFormat.Xlsx }, path, SETTINGS);
        var file = XlsxFile.Read(path);
        Assert.Equal(listed.Count + 1, file.Sheets.Count);
        for (var index = 0; index < listed.Count; index++)
        {
            Assert.StartsWith($"{sheets[index]} ", file.Sheets[index + 1].Name, StringComparison.Ordinal);
        }

        var summary = file.Sheets[0].Values;
        var header = summary.FindIndex(row => (row[0] as string) == "Number");
        Assert.Equal(
            listed.Select((id, index) => folded.Contains(id) ? null : (object?)double.Parse(sheets[index], System.Globalization.CultureInfo.InvariantCulture)),
            summary.Skip(header + 1).Take(listed.Count).Select(row => row[0]));
        Assert.Equal(file.Sheets.Skip(1).Select(sheet => (object?)sheet.Name), summary.Skip(header + 1).Take(listed.Count).Select(row => row[1]));

        // One check's file of its own is named as its sheet in the workbook.
        var one = Path.Combine(_folder, "one.xlsx");
        CrawlExporter.Export(copy, new ExportRequest { Kind = ExportKind.Check, CheckId = "canonical-broken", Format = ExportFormat.Xlsx }, one, SETTINGS);
        Assert.Equal(file.Sheets[listed.IndexOf("canonical-broken") + 1].Name, Assert.Single(XlsxFile.Read(one).Sheets).Name);
    }

    /// <summary>
    /// The score is the one the analysis stored, line for line: the report works none out. A file whose stored score
    /// says another number has a report that says that number.
    /// </summary>
    /// <summary>Discovery decision 2: the checks the user hid are left out of the report's lists and counts; the score stays.</summary>
    [Fact]
    public void TheReportLeavesOutTheChecksTheUserHidAndSaysSo()
    {
        var results = IssueReader.ReadResults(crawl.Path)!;
        var order = results.FixFirst;
        var clean = IssueCatalog.All.First(check => results.Counts.GetValueOrDefault(check.Id, -1) == 0).Id;
        string[] hidden = [order[1], order[2], clean];
        var whole = Parse(ReportExport.Build(crawl.Path, settings: SETTINGS));
        var document = Parse(ReportExport.Build(crawl.Path, new ReportOptions { HiddenChecks = hidden }, SETTINGS));

        var titles = document.QuerySelectorAll("article.problem").Select(problem => IssueCatalog.Find(problem.GetAttribute("data-check")!)!.Title + " ·").ToList();
        Assert.Equal(order.Count - 2, titles.Count);
        Assert.DoesNotContain(titles, title => title.StartsWith(IssueCatalog.Find(order[1])!.Title + " ·", StringComparison.Ordinal));
        Assert.Equal(whole.QuerySelectorAll(".passed li").Length - 1, document.QuerySelectorAll(".passed li").Length);
        Assert.Equal(whole.QuerySelector(".summary__word")?.TextContent, document.QuerySelector(".summary__word")?.TextContent);
        Assert.Contains(document.QuerySelectorAll(".summary__covers"),
            note => note.TextContent == "2 checks that found something are hidden, so they’re left out of this report. The score still counts them.");
        Assert.DoesNotContain(whole.QuerySelectorAll(".summary__covers"), note => note.TextContent.Contains("hidden", StringComparison.Ordinal));
    }

    /// <summary>
    /// Round 5's review (R2-5): with checks hidden, the score table dropped their rows but its sum still took off their
    /// points ("100 − 126" under rows adding to 66). The score is the analysis's whole, so what the hidden checks (or
    /// the checks a report of some checks leaves out) took off is one row of its own, and the rows add up to the sum.
    /// </summary>
    [Fact]
    public void TheScoreTableAddsUpWhenChecksAreLeftOut()
    {
        var score = IssueReader.ReadResults(crawl.Path)!.Score!;
        var costly = score.Lines.Where(line => line.Points > 0).ToList();
        var siteWide = costly.First(line => IssueCatalog.Find(line.CheckId)!.SiteWide);
        var other = costly.First(line => line != siteWide);
        string[] leftOut = [siteWide.CheckId, other.CheckId];
        var points = siteWide.Points + other.Points;

        foreach (var (options, says, howMuch) in new[]
        {
            (new ReportOptions { HiddenChecks = leftOut }, "2 hidden checks", "Left out of this report; the score still counts them"),
            (new ReportOptions { Checks = [.. costly.Select(line => line.CheckId).Except(leftOut)] }, "2 checks not in this report", "The score still counts them")
        })
        {
            var document = Parse(ReportExport.Build(crawl.Path, options, SETTINGS));
            var rows = document.QuerySelectorAll("table.score-table tbody tr").ToList();
            var taken = rows.Select(row => row.QuerySelector("td.num")!.TextContent.Replace("−", string.Empty, StringComparison.Ordinal)).Sum(int.Parse);
            Assert.Equal(score.Lost, taken);
            Assert.Equal(["Score", $"100 − {score.Lost}", $"{score.Value}"], document.QuerySelectorAll("table.score-table tfoot td").Select(cell => cell.TextContent));
            var row = Assert.Single(rows, row => row.QuerySelector("td")!.TextContent == says);
            Assert.Equal([says, howMuch, $"−{points}"], row.QuerySelectorAll("td").Select(cell => cell.TextContent));
        }

        // Nothing left out: no such row.
        var whole = Parse(ReportExport.Build(crawl.Path, settings: SETTINGS));
        Assert.DoesNotContain(whole.QuerySelectorAll("table.score-table tbody td"),
            cell => cell.TextContent.Contains("in this report", StringComparison.Ordinal) || cell.TextContent.Contains("hidden", StringComparison.Ordinal));

        // Held at 50 by a serious problem (a copy says so: this crawl's problems take it to 0): with every serious one
        // hidden, the note says the one holding it is left out, rather than citing a row that isn't there.
        var held = crawl.Copy();
        Change(held, "UPDATE analysis SET score_json = replace(replace(score_json, '\"heldBySerious\":false', '\"heldBySerious\":true'), '\"beforeCeiling\":0,', '\"beforeCeiling\":70,')");
        Assert.True(IssueReader.ReadResults(held)!.Score!.HeldBySerious);
        Assert.StartsWith($"Held at {ScoreCalculator.SERIOUS_CEILING} while a serious problem stands (70 otherwise). ",
            Parse(ReportExport.Build(held, settings: SETTINGS)).QuerySelector(".score-note")!.TextContent, StringComparison.Ordinal);
        var noSerious = Parse(ReportExport.Build(held,
            new ReportOptions { HiddenChecks = [.. score.Lines.Where(line => line.Severity == IssueSeverity.Error).Select(line => line.CheckId)] }, SETTINGS));
        Assert.StartsWith($"Held at {ScoreCalculator.SERIOUS_CEILING} while a serious problem stands, among the checks left out of this report (70 otherwise). ",
            noSerious.QuerySelector(".score-note")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void TheScoreIsTheOneTheAnalysisStored()
    {
        var score = IssueReader.ReadResults(crawl.Path)!.Score!;
        var document = Parse(ReportExport.Build(crawl.Path, settings: SETTINGS));

        Assert.Equal($"Score {score.Value} out of 100", document.QuerySelector("svg.ring")!.GetAttribute("aria-label"));
        Assert.Equal(ReportWords.Band(score.Band), document.QuerySelector(".summary__word")!.TextContent);

        // What took points off: every error and warning of the score, in its order, with its points; the notices in one line.
        var costly = score.Lines.Where(line => line.Severity != IssueSeverity.Notice).ToList();
        var rows = document.QuerySelectorAll("table.score-table tbody tr").ToList();
        Assert.Equal(costly.Count + (score.Lines.Count > costly.Count ? 1 : 0), rows.Count);
        Assert.Equal(costly.Select(line => $"−{line.Points}"), rows.Take(costly.Count).Select(row => row.QuerySelector("td.num")!.TextContent));
        Assert.Equal(["Score", $"100 − {score.Lost}", $"{score.Value}"], document.QuerySelectorAll("table.score-table tfoot td").Select(cell => cell.TextContent));

        // A fault of the whole site is said as one; a check reported once that isn't one says what it counted as.
        for (var index = 0; index < costly.Count; index++)
        {
            var check = IssueCatalog.Find(costly[index].CheckId)!;
            var howMuch = rows[index].QuerySelectorAll("td")[1].TextContent;
            if (check.SiteWide)
            {
                Assert.Equal("The whole site", howMuch);
            }
            else if (check.ReportedOnce)
            {
                Assert.StartsWith("Checked once, for the whole site (counts as ", howMuch, StringComparison.Ordinal);
            }
            else
            {
                Assert.StartsWith($"{ReportWords.Number(costly[index].Count)} of {ReportWords.Number(costly[index].Of)} ", howMuch, StringComparison.Ordinal);
            }
        }

        Assert.Contains(costly, line => IssueCatalog.Find(line.CheckId)!.SiteWide);
        Assert.Equal(score.HeldBySerious, document.QuerySelector(".score-note")!.TextContent.StartsWith($"Held at {ScoreCalculator.SERIOUS_CEILING} ", StringComparison.Ordinal));

        // Another number stored: that is the number, in the report and in the workbook, whatever the counts would give.
        var other = crawl.Copy();
        var said = score.Value == 12 ? 13 : 12;
        Change(other, $"UPDATE analysis SET score = {said}, score_json = replace(score_json, '\"value\":{score.Value},', '\"value\":{said},')");
        Assert.Equal(said, IssueReader.ReadResults(other)!.Score!.Value);
        var changed = Parse(ReportExport.Build(other, settings: SETTINGS));
        Assert.Equal($"Score {said} out of 100", changed.QuerySelector("svg.ring")!.GetAttribute("aria-label"));
        Assert.Equal($"{said}", changed.QuerySelector("table.score-table tfoot td.num")!.TextContent);

        // A score above 0 has its arc on the ring's track. A score of 0 has the track alone: an arc of no length with
        // round ends is drawn as a dot.
        Assert.Equal(2, changed.QuerySelectorAll("svg.ring circle").Length);
        var nought = crawl.Copy();
        Change(nought, $"UPDATE analysis SET score = 0, score_json = replace(score_json, '\"value\":{score.Value},', '\"value\":0,')");
        var none = Parse(ReportExport.Build(nought, settings: SETTINGS));
        Assert.Equal("Score 0 out of 100", none.QuerySelector("svg.ring")!.GetAttribute("aria-label"));
        Assert.Single(none.QuerySelectorAll("svg.ring circle"));
        Assert.Empty(none.QuerySelectorAll("svg.ring path"));

        // The sum is printed once, under the last line: a table's foot is printed again at the bottom of every page a
        // long table runs to, which would put the whole sum under part of the list.
        Assert.Contains(".score-table tfoot { display: table-row-group; }", document.QuerySelector("style")!.TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    /// A limit in a check's words is the one the analysis ran with: a crawl analysed with a title limit of 70 has a report
    /// that says 70, and no <c>{limit:…}</c> reaches the report, the workbook or a check's CSV.
    /// </summary>
    [Fact]
    public async Task ALimitInACheckSWordsIsTheOneTheAnalysisRanWith()
    {
        var copy = crawl.Copy();
        var thresholds = new AnalysisSettings { TitleMaxPixels = 385, TitleMinPixels = 160, DescriptionMaxPixels = 812, DescriptionMinPixels = 366, MinWordCount = 173, MaxUrlLength = 67 };
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(copy, thresholds);
        var results = IssueReader.ReadResults(copy)!;
        Assert.Equal(thresholds, results.Analysis.Settings);

        // The checks whose words name a limit, and that this analysis found something for: the test is of them.
        static bool Names(IssuePlainText plain) => plain.What.Concat(plain.Fix).Append(plain.Title).Append(plain.Passed).Any(text => text.Contains("{limit:", StringComparison.Ordinal));
        var naming = IssueCatalog.All.Where(check => Names(check.Plain!)).ToList();
        Assert.Equal(18, naming.Count);
        var found = naming.Where(check => results.Counts.GetValueOrDefault(check.Id) > 0).ToList();
        Assert.Contains(found, check => check.Id == "title-too-long");

        var html = ReportExport.Build(copy, settings: SETTINGS);
        Assert.DoesNotContain("{limit:", html, StringComparison.Ordinal);
        var text = Parse(html).Body!.TextContent;
        Assert.Contains("These pages have a title too long to show in full in search results, where there's room for about 40 characters.", text, StringComparison.Ordinal);
        foreach (var check in found)
        {
            Assert.Contains(string.Join(' ', PlainLimits.Fill(check.Plain!, thresholds).What), text, StringComparison.Ordinal);
        }

        // With the default thresholds the same sentence says the default: the number is the analysis's, not the document's.
        Assert.Contains("These pages have a title too long to show in full in search results, where there's room for about 60 characters.",
            Parse(ReportExport.Build(crawl.Path, settings: SETTINGS)).Body!.TextContent, StringComparison.Ordinal);

        // The workbook, whose summary names every check, and one check's sheet as CSV.
        var workbook = Path.Combine(_folder, "limits.xlsx");
        CrawlExporter.Export(copy, new ExportRequest { Kind = ExportKind.Workbook, Format = ExportFormat.Xlsx }, workbook, SETTINGS);
        var cells = XlsxFile.Read(workbook).Sheets.SelectMany(sheet => sheet.Values.SelectMany(row => row).OfType<string>().Append(sheet.Name)).ToList();
        Assert.NotEmpty(cells);
        Assert.DoesNotContain(cells, cell => cell.Contains("{limit:", StringComparison.Ordinal));

        var csv = Path.Combine(_folder, "limits.csv");
        CrawlExporter.Export(copy, new ExportRequest { Kind = ExportKind.Check, Format = ExportFormat.Csv, CheckId = "title-too-long" }, csv, SETTINGS);
        Assert.DoesNotContain("{limit:", File.ReadAllText(csv), StringComparison.Ordinal);
    }

    /// <summary>
    /// What makes the document safe to open and to mail on: no script, no style but its own sheet (which its policy names
    /// by hash), nothing loaded from anywhere, and no link to anything but http or https.
    /// </summary>
    [Fact]
    public void TheDocumentIsSelfContainedAndCarriesAPolicyThatAllowsNothingElse()
    {
        var html = ReportExport.Build(crawl.Path, settings: SETTINGS);
        var document = Parse(html);
        AssertInert(document);

        var policy = document.QuerySelector("meta[http-equiv='Content-Security-Policy']")!.GetAttribute("content")!;
        Assert.Equal(ReportExport.ContentSecurityPolicy, policy);
        Assert.StartsWith("default-src 'none'; style-src 'sha256-", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe", policy, StringComparison.Ordinal);

        // The hash a browser takes of the sheet: of the style element's text, its line endings as line feeds.
        var sheet = Assert.Single(document.QuerySelectorAll("style")).TextContent;
        Assert.DoesNotContain('\r', sheet);
        Assert.Contains($"'sha256-{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(sheet)))}'", policy, StringComparison.Ordinal);

        // The sheet asks for nothing either: no web font, no image, no import.
        Assert.DoesNotContain("url(", sheet, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@import", sheet, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@font-face", sheet, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@page", sheet, StringComparison.Ordinal);

        // The policy comes before anything it governs.
        Assert.True(html.IndexOf("Content-Security-Policy", StringComparison.Ordinal) < html.IndexOf("<style>", StringComparison.Ordinal));
    }

    /// <summary>A site's own text, as hostile as a site can make it, in every place the report prints one.</summary>
    [Fact]
    public void WhatASiteSendsIsNeverMarkup()
    {
        const string MARKUP = "\"><script>alert(1)</script><img src=x onerror=alert(2)><svg onload=alert(3)>'&lt;";
        const string STYLE = "</style><style>*{display:none}</style><link rel=stylesheet href=https://evil.example/x.css>";
        var hostile = crawl.Copy();
        var results = IssueReader.ReadResults(hostile)!;
        var order = ReportSources.FixFirst(results);

        // Every issue's detail; a javascript: URL for the first URLs flagged, a URL with a mark that reverses its text for the next,
        // a data: URL where another URL is named, an https URL full of quotes for every other; and the start URL.
        const string FIRST = "SELECT DISTINCT url_id FROM issues ORDER BY url_id LIMIT 3";
        const string NEXT = "SELECT DISTINCT url_id FROM issues ORDER BY url_id LIMIT 20 OFFSET 3";
        Change(hostile, "UPDATE issues SET detail = $detail", ("$detail", MARKUP + STYLE));
        Change(hostile, $"""
            UPDATE urls SET url = 'data:text/html,<script>alert(4)</script>' || id WHERE id IN (
                SELECT related_url_id FROM issues WHERE related_url_id IS NOT NULL AND related_url_id NOT IN ({FIRST}) AND related_url_id NOT IN ({NEXT}) ORDER BY url_id LIMIT 3)
            """);
        Change(hostile, $"UPDATE urls SET url = 'javascript:alert(document.domain)//' || id WHERE id IN ({FIRST})");
        Change(hostile, $"UPDATE urls SET url = 'https://evil.example/\u202Egnp.exe?' || id WHERE id IN ({NEXT})");
        Change(hostile, """
            UPDATE urls SET url = 'https://evil.example/"onmouseover="alert(5)" x="' || id
            WHERE id IN (SELECT url_id FROM issues) AND url NOT LIKE 'javascript:%' AND url NOT LIKE 'data:%' AND url NOT LIKE '%gnp.exe%'
            """);
        Change(hostile, "UPDATE crawl SET start_url = $start", ("$start", "https://evil.example/\"><script>alert(6)</script>"));

        var html = ReportExport.Build(hostile, new ReportOptions { Examples = 50, LookExamples = 50 }, SETTINGS);
        var document = Parse(html);
        AssertInert(document);

        // The text is all there, as text.
        var text = document.Body!.TextContent;
        Assert.Contains("<script>alert(1)</script>", text, StringComparison.Ordinal);
        Assert.Contains("</style><style>*{display:none}</style>", text, StringComparison.Ordinal);
        Assert.Contains("javascript:alert(document.domain)//", text, StringComparison.Ordinal);
        Assert.Contains("data:text/html,<script>alert(4)</script>", text, StringComparison.Ordinal);
        Assert.Contains("\"onmouseover=\"alert(5)\"", text, StringComparison.Ordinal);
        Assert.Contains("<script>alert(6)</script>", text, StringComparison.Ordinal);

        // A javascript: or data: URL is shown and never linked; an https one is linked as it is, quotes and all.
        var links = document.QuerySelectorAll("a[href]").ToList();
        Assert.NotEmpty(links);
        Assert.Contains(links, link => link.GetAttribute("href")!.StartsWith("https://evil.example/\"onmouseover=\"alert(5)\" x=\"", StringComparison.Ordinal));
        Assert.DoesNotContain(links, link => link.TextContent.Contains("javascript:", StringComparison.Ordinal) || link.TextContent.Contains("data:", StringComparison.Ordinal));

        // A mark that reverses the text after it is left out, and a URL that held one isn't linked.
        Assert.DoesNotContain('\u202E', html);
        Assert.Contains("/gnp.exe?", text, StringComparison.Ordinal);
        Assert.DoesNotContain(links, link => link.TextContent.Contains("gnp.exe", StringComparison.Ordinal));
        Assert.True(order.Count > 0);
    }

    [Fact]
    public async Task TheSummaryCutIsTheFullReportsFirstPage()
    {
        var full = Parse(ReportExport.Build(crawl.Path, settings: SETTINGS));
        var summary = Parse(ReportExport.Build(crawl.Path, new ReportOptions { Cut = ReportCut.Summary }, SETTINGS));
        AssertInert(summary);

        Assert.Equal(["Fix these first", "How the score is worked out"], summary.QuerySelectorAll("h2").Select(heading => heading.TextContent));
        Assert.Empty(summary.QuerySelectorAll("article.problem"));
        Assert.Empty(summary.QuerySelectorAll(".facts"));

        // The parts it has are the full report's own, but for where each points the reader on.
        foreach (var part in new[] { "h1", "p.meta", ".summary__word", ".summary__sentence", ".tally", "ol.first", "table.score-table", ".score-note", "footer" })
        {
            Assert.Equal(full.QuerySelector(part)!.TextContent, summary.QuerySelector(part)!.TextContent);
        }

        Assert.Contains("in the full report under the same number", summary.QuerySelector(".first__foot")!.TextContent, StringComparison.Ordinal);
        // Where each is explained is said by its section's name: which page that starts on depends on the paper and the score's length.
        Assert.EndsWith("Each is explained under the same number in “What to fix”, with the first pages it’s on.", full.QuerySelector(".first__foot")!.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("page 2", full.QuerySelector(".first__foot")!.TextContent, StringComparison.Ordinal);

        // Checks that couldn't run are listed near the full report's end, which the summary can only point to.
        var stopped = await EndedEarlyAsync(CrawlStatus.Stopped);
        var skipped = IssueReader.ReadResults(stopped)!.Score!.Covers.ChecksSkipped;
        Assert.True(skipped > 1, $"{skipped} checks skipped");
        Assert.Contains($" {skipped} checks couldn’t run: they are listed near the end.", Parse(ReportExport.Build(stopped, settings: SETTINGS)).QuerySelector(".summary__covers")!.TextContent,
            StringComparison.Ordinal);
        Assert.Contains($" {skipped} checks couldn’t run: the full report lists them.",
            Parse(ReportExport.Build(stopped, new ReportOptions { Cut = ReportCut.Summary }, SETTINGS)).QuerySelector(".summary__covers")!.TextContent, StringComparison.Ordinal);

    }

    [Fact]
    public void TheExamplesAreAsManyAsAskedFor()
    {
        var document = Parse(ReportExport.Build(crawl.Path, new ReportOptions { Examples = 2, LookExamples = 0 }, SETTINGS));
        var results = IssueReader.ReadResults(crawl.Path)!;
        var listed = GridListing.Listed(ReportSources.FixFirst(results));
        var problems = document.QuerySelectorAll("article.problem").ToList();
        for (var index = 0; index < listed.Count; index++)
        {
            var notice = IssueCatalog.Find(listed[index].Id)!.Severity == IssueSeverity.Notice;
            Assert.Equal(notice ? 0 : (int)Math.Min(2, results.Counts[listed[index].Id]), problems[index].QuerySelectorAll(".examples tbody tr").Length);
            if (notice)
            {
                Assert.Equal($"The full list is on sheet {listed[index].Sheet} of the spreadsheet.", problems[index].QuerySelector(".more")!.TextContent);
            }
        }

        foreach (var options in new[] { new ReportOptions { Examples = 51 }, new ReportOptions { LookExamples = -1 } })
        {
            var failure = Assert.Throws<ExportException>(() => ReportExport.Build(crawl.Path, options, SETTINGS));
            Assert.Equal(ExportFailure.InvalidRequest, failure.Failure);
            Assert.Equal(options.Examples == 51 ? "examples" : "lookExamples", failure.Field);
        }
    }

    /// <summary>
    /// A title or description too long or too short is said in the report as Simple says it: where Google will likely cut
    /// it, or how many characters it has. Never in pixels, which plain words don't use.
    /// </summary>
    [Fact]
    public void TitleAndDescriptionExamplesSayWhereGoogleWillLikelyCutThem()
    {
        var document = Parse(ReportExport.Build(crawl.Path, new ReportOptions { LookExamples = 50 }, SETTINGS));
        var order = ReportSources.FixFirst(IssueReader.ReadResults(crawl.Path)!).ToList();
        string Examples(string id) => document.QuerySelectorAll("article.problem")[order.IndexOf(id)].QuerySelector(".examples")!.TextContent;

        var titles = Examples("title-too-long");
        Assert.Contains("Google will likely cut this title off after “…runs on far past what search”", titles, StringComparison.Ordinal);
        Assert.Contains("Google will likely cut this title off after “…HEATERS: MODERN LANTERNS AND”", titles, StringComparison.Ordinal);
        Assert.Contains("Only 10 characters long", Examples("title-too-short"), StringComparison.Ordinal);
        Assert.Contains("Google will likely cut this description off after “…", Examples("description-too-long"), StringComparison.Ordinal);
        Assert.Contains("Only 10 characters long", Examples("description-too-short"), StringComparison.Ordinal);
        foreach (var id in new[] { "title-too-long", "title-too-short", "description-too-long", "description-too-short" })
        {
            Assert.DoesNotContain(" px", Examples(id), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The quote is the last whole words shown, up to 30 characters, with "…" only when more comes before them, and never
    /// opens on a separator left from the words before ("…| Phone").
    /// </summary>
    [Theory]
    [InlineData("title", "Garden Sofa Set…", "Google will likely cut this title off after “Garden Sofa Set”")]
    [InlineData("title", "Brisbane Wooden Garden Storage Tool Shed With Sloped…", "Google will likely cut this title off after “…Storage Tool Shed With Sloped”")]
    [InlineData("description", "Supercalifragilisticexpialidocious-and-then-some-more…",
        "Google will likely cut this description off after “…ialidocious-and-then-some-more”")]
    [InlineData("title", "Contact Us - GardenFurnitureSets | Phone, Email or Contract…", "Google will likely cut this title off after “…Phone, Email or Contract”")]
    [InlineData("title", "Garden Sofa Set", null)]
    [InlineData("title", null, null)]
    public void WhereGoogleWillLikelyCutIsQuotedByItsLastWords(string what, string? inResults, string? said) =>
        Assert.Equal(said, ReportWords.CutOff(what, inResults));

    /// <summary>
    /// A crawl stopped or failed part-way, analysed as it stands: the report says so, its score is "so far" and says why,
    /// and the checks that need the whole site are listed as not run, each with why and what would let it, in plain words.
    /// </summary>
    [Theory]
    [InlineData(CrawlStatus.Stopped, "This check was stopped early: ", "read before the check was stopped")]
    [InlineData(CrawlStatus.Failed, "This check failed before it finished: ", "read before the check failed")]
    public async Task ACrawlThatEndedEarlySaysSoAndItsScoreIsSoFar(CrawlStatus status, string says, string covers)
    {
        var stopped = await EndedEarlyAsync(status);
        var results = IssueReader.ReadResults(stopped)!;
        var score = results.Score!;
        Assert.Equal((true, (ScoreIncompleteReason?)(status == CrawlStatus.Failed ? ScoreIncompleteReason.Failed : ScoreIncompleteReason.Stopped)), (score.Partial, score.Covers.Reason));
        var queued = TestCrawls.Count(stopped, "SELECT COUNT(*) FROM urls WHERE state = 0");
        var document = Parse(ReportExport.Build(stopped, settings: SETTINGS));
        AssertInert(document);

        var note = document.QuerySelector("header .note")!.TextContent;
        Assert.StartsWith(says, note, StringComparison.Ordinal);
        Assert.Contains($"{queued:N0} addresses still to check", note, StringComparison.Ordinal);
        Assert.EndsWith("This report covers only what was read.", note, StringComparison.Ordinal);
        Assert.Equal($"{ReportWords.Band(score.Band)}, so far", document.QuerySelector(".summary__word")!.TextContent);
        Assert.Equal($"Score {score.Value} out of 100, so far", document.QuerySelector("svg.ring")!.GetAttribute("aria-label"));
        Assert.Equal("so far", document.QuerySelectorAll("svg.ring text").Last().TextContent);
        Assert.Equal("Score, so far", document.QuerySelector("table.score-table tfoot td")!.TextContent);
        var sentence = document.QuerySelector(".summary__covers")!.TextContent;
        Assert.StartsWith($"The score covers the {ReportWords.Number(score.Covers.Pages)} pages {covers}, and {score.Covers.ChecksRun} of {IssueCatalog.All.Count} checks.", sentence,
            StringComparison.Ordinal);
        var finished = document.QuerySelectorAll(".facts dt").Single(term => term.TextContent == "Finished?").NextElementSibling!.TextContent;
        Assert.StartsWith("No. " + says, finished, StringComparison.Ordinal);
        Assert.EndsWith("Continue the check to read the rest.", finished, StringComparison.Ordinal);

        // What couldn't be checked: every check the analysis skipped, named by what it looks for, with the reason's plain words.
        Assert.NotEmpty(results.Skipped);
        Assert.Equal(score.Covers.ChecksSkipped, results.Skipped.Count);
        Assert.Contains("What couldn’t be checked", document.QuerySelectorAll("h2").Select(heading => heading.TextContent));
        var reasons = results.Skipped.ToDictionary(skip => skip.CheckId, StringComparer.Ordinal);
        Assert.Equal(
            IssueCatalog.All.Where(check => reasons.ContainsKey(check.Id)).Select(check =>
            {
                var words = PlainWords.SkipReason(reasons[check.Id].Reason)!;
                return $"{check.Plain!.LooksFor ?? check.Plain.Title}. {words.Text} {words.Action}";
            }),
            document.QuerySelectorAll(".plain-list li").Select(item => item.TextContent));
        Assert.Contains(reasons.Values, skip => skip.Reason == SkippedCheckReasons.CRAWL_UNFINISHED);
        Assert.Equal($"{results.Skipped.Count}", document.QuerySelectorAll(".tally b").Last().TextContent);
    }

    /// <summary>
    /// A crawl that finished at a limit read part of the site: the report says which limit, and the score is of what was
    /// read. Real crawls of the synthetic site, each with a limit that keeps some of it out.
    /// </summary>
    [Fact]
    public async Task ACrawlThatReachedALimitSaysWhichAndItsScoreIsSoFar()
    {
        var limited = await CrawlAsync("limited", options => options with { MaxUrls = 40 });
        var byUrls = IssueReader.ReadResults(limited)!.Score!;
        Assert.Equal((true, (ScoreIncompleteReason?)ScoreIncompleteReason.UrlLimit), (byUrls.Partial, byUrls.Covers.Reason));
        var document = Parse(ReportExport.Build(limited, settings: SETTINGS));
        var past = TestCrawls.Count(limited, $"SELECT COUNT(*) FROM urls WHERE skip_reason = {(int)SkipReason.UrlLimit}");
        Assert.Equal(
            $"This check stopped at its limit of 40 addresses: {ReportWords.Plural(byUrls.Covers.Pages, "page", "pages")} read, {ReportWords.Plural(past, "address", "addresses")} past the limit. This report covers only what was read.",
            document.QuerySelector("header .note")!.TextContent);
        Assert.EndsWith(", so far", document.QuerySelector(".summary__word")!.TextContent, StringComparison.Ordinal);
        Assert.Contains(" read before the check reached its limit, and ", document.QuerySelector(".summary__covers")!.TextContent, StringComparison.Ordinal);
        var finished = document.QuerySelectorAll(".facts dt").Single(term => term.TextContent == "Finished?").NextElementSibling!.TextContent;
        Assert.EndsWith("Check the site again with a higher limit to read the rest.", finished, StringComparison.Ordinal);

        var shallow = await CrawlAsync("shallow", options => options with { MaxDepth = 1, DiscoverSitemaps = false });
        var byDepth = IssueReader.ReadResults(shallow)!.Score!;
        Assert.Equal((true, (ScoreIncompleteReason?)ScoreIncompleteReason.DepthLimit), (byDepth.Partial, byDepth.Covers.Reason));
        document = Parse(ReportExport.Build(shallow, settings: SETTINGS));
        var deeper = TestCrawls.Count(shallow, $"SELECT COUNT(*) FROM urls WHERE skip_reason = {(int)SkipReason.DepthLimit}");
        Assert.Equal(
            $"This check stopped at its limit of 1 click from the start: {ReportWords.Plural(byDepth.Covers.Pages, "page", "pages")} read, {ReportWords.Plural(deeper, "address", "addresses")} further in than that. This report covers only what was read.",
            document.QuerySelector("header .note")!.TextContent);
        Assert.EndsWith(", so far", document.QuerySelector(".summary__word")!.TextContent, StringComparison.Ordinal);
        Assert.Contains(" within the check’s limit of clicks from the start, and ", document.QuerySelector(".summary__covers")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void APerfectSiteHasNothingToFixAndAScoreOfAHundred()
    {
        var perfect = crawl.Copy();
        Change(perfect, "DELETE FROM issues");
        Change(perfect, "UPDATE issue_types SET url_count = 0");
        StoreTheScoreOfItsCounts(perfect);
        var document = Parse(ReportExport.Build(perfect, settings: SETTINGS));
        AssertInert(document);

        Assert.Equal("Score 100 out of 100", document.QuerySelector("svg.ring")!.GetAttribute("aria-label"));
        Assert.Equal("Very good", document.QuerySelector(".summary__word")!.TextContent);
        Assert.Equal("This site is in very good shape. Nothing was found to fix.", document.QuerySelector(".summary__sentence")!.TextContent);
        var skipped = IssueCatalog.All.Count - IssueReader.ReadCounts(perfect).Count;
        string[] outline = ["Nothing to fix", "How the score is worked out", "What couldn’t be checked", "What was checked and found nothing wrong", "About this check"];
        Assert.Equal(outline.Where(heading => skipped > 0 || heading != "What couldn’t be checked"), document.QuerySelectorAll("h2").Select(heading => heading.TextContent));
        Assert.Empty(document.QuerySelectorAll("article.problem"));
        Assert.Equal("100", document.QuerySelector("table.score-table tfoot td.num")!.TextContent);
        Assert.Equal(["Nothing took points off", string.Empty, "0"], document.QuerySelectorAll("table.score-table tbody td").Select(cell => cell.TextContent));
        Assert.Equal("No serious problem was found, and nothing worth fixing.", document.QuerySelector("h2 + p.lead")!.TextContent);
        Assert.Equal(["0", "0", "0"], document.QuerySelectorAll(".tally b").Take(3).Select(count => count.TextContent));

        // A site with nothing but things worth a look is as good: they take nothing off, and the report says where they are.
        var looks = crawl.Copy();
        var costly = string.Join(", ", IssueCatalog.All.Where(check => check.Severity != IssueSeverity.Notice).Select(check => $"'{check.Id}'"));
        Change(looks, $"DELETE FROM issues WHERE issue IN ({costly})");
        Change(looks, $"UPDATE issue_types SET url_count = 0 WHERE id IN ({costly})");
        StoreTheScoreOfItsCounts(looks);
        document = Parse(ReportExport.Build(looks, settings: SETTINGS));
        var notices = IssueReader.ReadResults(looks)!.FixFirst.Count;
        Assert.True(notices > 1, $"{notices} notices");
        Assert.Equal("Score 100 out of 100", document.QuerySelector("svg.ring")!.GetAttribute("aria-label"));
        Assert.Equal($"This site is in very good shape. Nothing needs fixing, and there are {notices} things worth a look.", document.QuerySelector(".summary__sentence")!.TextContent);
        Assert.Equal(
            $"No serious problem was found, and nothing worth fixing. {notices} things are worth a look: they are explained under “What to fix”. Often they’re fine as they are, and they don’t change the score.",
            document.QuerySelector("h2 + p.lead")!.TextContent);
        Assert.Equal(notices, document.QuerySelectorAll("article.problem").Length);
    }

    /// <summary>A crawl that read no page has no score, and the report says why: a real crawl whose start URL answers 404.</summary>
    [Fact]
    public async Task ACrawlThatReadNoPageHasNoScoreAndSaysWhy()
    {
        var none = await CrawlAsync("none", options => options with { StartUrl = new Uri(crawl.Server.BaseUri, "/broken/1").AbsoluteUri, DiscoverSitemaps = false });
        var results = IssueReader.ReadResults(none)!;
        Assert.Equal(((SiteScore?)null, (NoScoreReason?)NoScoreReason.NoPages, 0L), (results.Score, results.NoScoreReason, results.Analysis.PagesAnalyzed));
        var document = Parse(ReportExport.Build(none, settings: SETTINGS));
        AssertInert(document);

        Assert.Equal("No score", document.QuerySelector("svg.ring")!.GetAttribute("aria-label"));
        Assert.Equal("No score", document.QuerySelector(".summary__word")!.TextContent);
        Assert.Equal("No page could be read, so there is nothing to score.", document.QuerySelector(".summary__sentence")!.TextContent);
        Assert.Contains("and got no page it could read. Check that the address is right and that the site is up, then check it again.", document.QuerySelector(".summary__covers")!.TextContent,
            StringComparison.Ordinal);
        var headings = document.QuerySelectorAll("h2").Select(heading => heading.TextContent).ToList();
        Assert.DoesNotContain("How the score is worked out", headings);
        Assert.DoesNotContain("Nothing to fix", headings);
        Assert.Empty(document.QuerySelectorAll("table.score-table"));
        Assert.Contains("0 pages read", document.QuerySelector("p.meta")!.TextContent, StringComparison.Ordinal);

        // What it did find is still said, in the engine's order, with no share of a score there isn't: the start URL answers 404.
        Assert.NotEmpty(results.FixFirst);
        Assert.Equal(results.FixFirst.Count, document.QuerySelectorAll("article.problem").Length);
        Assert.Equal(headings.Contains("Fix these first"), results.FixFirst.Any(id => IssueCatalog.Find(id)!.Severity != IssueSeverity.Notice));
        Assert.All(document.QuerySelectorAll(".problem__meta"), line => Assert.DoesNotContain("the score", line.TextContent, StringComparison.Ordinal));

        // The workbook says the same.
        Assert.Equal("No score: no page could be read", SummaryFact(none, "Score"));

        // A start URL the site's robots.txt refuses: no page either, and the report says what kept it out, counting the
        // site's own addresses as the analysis did (one here: "wasn’t", not "weren’t").
        var refused = await CrawlAsync("refused", options => options with { StartUrl = new Uri(crawl.Server.BaseUri, "/private/1").AbsoluteUri, DiscoverSitemaps = false });
        Assert.Equal((NoScoreReason?)NoScoreReason.NoPages, IssueReader.ReadResults(refused)!.NoScoreReason);
        Assert.Equal(1, IssueReader.ReadResults(refused)!.Counts["blocked-by-robots"]);
        Assert.EndsWith(
            "and got no page it could read. The site’s rules file (robots.txt) keeps search engines out of 1 address, which wasn’t read.",
            Parse(ReportExport.Build(refused, settings: SETTINGS)).QuerySelector(".summary__covers")!.TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    /// What robots.txt kept the check out of is the site's own addresses, as the analysis counted them for the score:
    /// an address on another website that its robots.txt refuses is no part of "the site’s rules file".
    /// </summary>
    [Fact]
    public void WhatTheSitesRobotsTxtKeptOutIsTheSitesOwnAddresses()
    {
        var covers = IssueReader.ReadResults(crawl.Path)!.Score!.Covers;
        Assert.True(covers.BlockedByRobots > 1);
        // The partner site's robots.txt already keeps the crawler out of one of its pages; three more of its URLs are made so here.
        var others = CrawlStatsReader.Read(crawl.Path).Outcomes[FetchOutcome.RobotsDisallowed] - covers.BlockedByRobots;
        Assert.Equal(1, others);
        var copy = crawl.Copy();
        Change(copy, $"UPDATE responses SET outcome = {(int)FetchOutcome.RobotsDisallowed} WHERE url_id IN (SELECT url_id FROM responses JOIN urls ON urls.id = url_id WHERE is_internal = 0 AND outcome = 0 LIMIT 3)");
        Assert.Equal(covers.BlockedByRobots + others + 3, CrawlStatsReader.Read(copy).Outcomes[FetchOutcome.RobotsDisallowed]);

        var document = Parse(ReportExport.Build(copy, settings: SETTINGS));
        var notRead = document.QuerySelectorAll(".facts dt").Single(term => term.TextContent == "Not read").NextElementSibling!.TextContent;
        Assert.Contains($"{covers.BlockedByRobots} addresses the site’s rules file (robots.txt) keeps search engines out of", notRead, StringComparison.Ordinal);
        Assert.EndsWith($"robots.txt keeps search engines out of {covers.BlockedByRobots} addresses, which weren’t read.", document.QuerySelector(".summary__covers")!.TextContent,
            StringComparison.Ordinal);

        // One address "wasn’t" read.
        Change(copy, $"UPDATE analysis SET score_json = replace(score_json, '\"blockedByRobots\":{covers.BlockedByRobots}', '\"blockedByRobots\":1')");
        Assert.Equal(1, IssueReader.ReadResults(copy)!.Score!.Covers.BlockedByRobots);
        document = Parse(ReportExport.Build(copy, settings: SETTINGS));
        Assert.EndsWith("robots.txt keeps search engines out of 1 address, which wasn’t read.", document.QuerySelector(".summary__covers")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("1 address the site’s rules file (robots.txt) keeps search engines out of", document.QuerySelector(".facts")!.TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    /// A crawl that went on after its results were worked out (stopped, analysed, continued): the report is of the
    /// results, so it says they are out of date whoever asks, and leaves out what it could only read from the crawl as it
    /// now stands. "Pages that can appear in search: 4,064 of 3,113" set a figure of now beside one of then.
    /// </summary>
    [Fact]
    public async Task ACrawlThatWentOnAfterItsResultsLeavesOutWhatIsOfTheCrawlNow()
    {
        var stopped = await EndedEarlyAsync(CrawlStatus.Stopped);
        var before = Parse(ReportExport.Build(stopped, settings: SETTINGS));
        string[] ofNow = ["Pages that can appear in search", "Not read"];
        Assert.Contains("Pages that can appear in search", before.QuerySelectorAll(".facts dt").Select(term => term.TextContent));
        Assert.Contains("still to check", before.QuerySelector("header .note")!.TextContent, StringComparison.Ordinal);

        // It goes on: more is found and queued, and the crawl's row says it changed. Nobody says how to judge the results.
        Change(stopped, "UPDATE urls SET state = 0 WHERE id IN (SELECT id FROM urls WHERE id NOT IN (SELECT url_id FROM responses) LIMIT 40)");
        Stamp(stopped);
        var document = Parse(ReportExport.Build(stopped, settings: SETTINGS));
        var note = Assert.Single(document.QuerySelectorAll("header .note")).TextContent;
        Assert.StartsWith("These results were worked out on ", note, StringComparison.Ordinal);
        Assert.Contains("the site check has gone on since", note, StringComparison.Ordinal);
        Assert.DoesNotContain("still to check", document.Body!.TextContent, StringComparison.Ordinal);
        var facts = document.QuerySelectorAll(".facts dt").Select(term => term.TextContent).ToList();
        Assert.DoesNotContain(facts, fact => ofNow.Contains(fact));
        Assert.Equal(
            "Not known from these results: the check has gone on since they were worked out.",
            document.QuerySelectorAll(".facts dt").Single(term => term.TextContent == "Finished?").NextElementSibling!.TextContent);

        // The score is still the results': so far, as they were of a check stopped early.
        Assert.EndsWith(", so far", document.QuerySelector(".summary__word")!.TextContent, StringComparison.Ordinal);
        Assert.Equal("The check has gone on since these results were worked out", SummaryFact(stopped, "Finished?"));
    }

    /// <summary>
    /// "One fix" speaks of what the check flags: pages for a check of pages, addresses for a check of addresses (a chain
    /// of forwards that all end at one address), which lead to the address they share rather than name it.
    /// </summary>
    [Fact]
    public void OneFixCountsAddressesForACheckOfAddresses()
    {
        var copy = crawl.Copy();
        var results = IssueReader.ReadResults(copy)!;
        Assert.Equal(IssueSubject.Url, IssueCatalog.Find("redirect-chain")!.Subject);
        Assert.True(results.Counts["redirect-chain"] > 0);

        // Twelve more addresses flagged, all forwarding on to the start URL: most of the check's rows share one end.
        Change(copy, """
            UPDATE issues SET issue = 'redirect-chain', related_url_id = 1, detail = NULL
            WHERE (issue, url_id) IN (SELECT MIN(issue), url_id FROM issues WHERE url_id NOT IN (SELECT url_id FROM issues WHERE issue = 'redirect-chain') GROUP BY url_id LIMIT 12)
            """);
        Change(copy, "UPDATE issue_types SET url_count = (SELECT COUNT(*) FROM issues WHERE issue = issue_types.id) WHERE url_count > 0");
        Change(copy, "UPDATE issues SET related_url_id = 1 WHERE issue = 'redirect-chain'");
        Change(copy, "DELETE FROM issue_targets WHERE issue = 'redirect-chain'");
        Change(copy, "INSERT INTO issue_targets (issue, url_id, related_url_id) SELECT issue, url_id, 1 FROM issues WHERE issue = 'redirect-chain'");
        var count = IssueReader.ReadResults(copy)!.Counts["redirect-chain"];
        Assert.True(count >= 12, $"{count} flagged.");

        var document = Parse(ReportExport.Build(copy, settings: SETTINGS));
        var problem = document.QuerySelector("article.problem[data-check=redirect-chain]")!;
        var oneFix = problem.QuerySelector(".onefix")!.TextContent;
        Assert.True(oneFix.StartsWith($"One fix clears {count} of these {count} addresses. They all lead to the same address, /, so ", StringComparison.Ordinal), oneFix);

        // A check of pages still counts pages, which name the address they share.
        var pages = document.QuerySelectorAll(".onefix").Select(fix => fix.TextContent).Where(text => text != oneFix).ToList();
        Assert.All(pages, text => Assert.Matches("^One fix clears [0-9,]+ of these [0-9,]+ pages?\\. ", text));
    }

    /// <summary>
    /// The "can I fix it from this?" pass: a check of the site's sitemaps or robots.txt names the file, not the home page it's
    /// reported on; and a check of the canonicals or hreflang a page names says what its column holds, the URL it names,
    /// as the fix is the page's own tag, not a change to that URL. The page is "Page to edit" in a site owner's words.
    /// </summary>
    [Fact]
    public void AFileCheckNamesItsFileAndACanonicalsColumnSaysWhatItNames()
    {
        foreach (var wording in new[] { ReportWording.Plain, ReportWording.Technical })
        {
            var document = Parse(ReportExport.Build(crawl.Path, new ReportOptions { Examples = 10, LookExamples = 10, Wording = wording }, SETTINGS));
            IElement Problem(string checkId) => document.QuerySelector($"article.problem[data-check={checkId}]")!;
            var page = wording == ReportWording.Plain ? "Page to edit" : "Page";

            var error = Problem("sitemap-error");
            Assert.Equal(["File", "What was found"], error.QuerySelectorAll(".examples thead th").Select(cell => cell.TextContent));
            Assert.Equal("/sitemaps/missing.xml", Assert.Single(error.QuerySelectorAll(".examples tbody tr")).QuerySelector("td .mono")!.TextContent);
            Assert.Equal("/sitemaps/empty.xml", Assert.Single(Problem("sitemap-empty").QuerySelectorAll(".examples tbody tr")).QuerySelector("td .mono")!.TextContent);

            Assert.Equal([page, "What was found", "Main address it names"], Problem("canonical-broken").QuerySelectorAll(".examples thead th").Select(cell => cell.TextContent));
            Assert.Equal([page, "What was found", "Language versions it names"], Problem("hreflang-broken-target").QuerySelectorAll(".examples thead th").Select(cell => cell.TextContent));
        }
    }

    /// <summary>
    /// The owner's complaint, in the report (the UX audit's S-02, S-11): "people are going to be more interested as to
    /// where the broken link is". A missing address leads its row with the page that holds the link to it, as a link, then
    /// the link's words and where on the page it is; then where the link leads, and what's wrong there. One page's links are
    /// together. In a site owner's words in the plain report, a working SEO's in the technical one.
    /// </summary>
    [Fact]
    public void AMissingAddressLeadsWithThePageToEditAndTheLinkOnIt()
    {
        var origin = crawl.Server.BaseUri.GetLeftPart(UriPartial.Authority);
        var ids = FirstRows("internal-client-error", 10).ToDictionary(row => (string)row.Values[1]!, row => row.Id);
        var found = FoundOnReader.ReadFoundOn(crawl.Path, [.. ids.Values], ReportReader.LISTED_SOURCES).ToDictionary(url => url.UrlId);
        Assert.Contains(found.Values, url => url.Pages > 0 && !string.IsNullOrWhiteSpace(url.Sources[0].Text));

        foreach (var wording in new[] { ReportWording.Plain, ReportWording.Technical })
        {
            var plain = wording == ReportWording.Plain;
            var document = Parse(ReportExport.Build(crawl.Path, new ReportOptions { Examples = 10, LookExamples = 10, Wording = wording }, SETTINGS));
            var broken = document.QuerySelector("article.problem[data-check=internal-client-error]")!;
            Assert.Equal(plain ? ["Page to edit", "It leads to", "What’s wrong"] : ["Where to fix it", "Address", "What was found"],
                broken.QuerySelectorAll(".examples thead th").Select(cell => cell.TextContent));
            var rows = broken.QuerySelectorAll(".examples tbody tr").ToList();
            Assert.Equal(ids.Count, rows.Count);
            List<string?> firstPages = [];
            foreach (var row in rows)
            {
                var cells = row.QuerySelectorAll("td");
                var url = found[ids[cells[1].QuerySelector("a")!.GetAttribute("href")!]];
                var lines = cells[0].QuerySelectorAll(".where").ToList();
                firstPages.Add(url.Pages > 0 ? url.Sources[0].Url : null);

                // What's wrong, in the row's last cell: how the address answered.
                Assert.Matches(plain ? "^It (is missing|can’t be opened) \\(4[0-9]{2}" : "^4[0-9]{2} ", cells[2].TextContent);
                if (url.Pages == 0)
                {
                    Assert.Equal(url.Sitemaps.Total > 0, cells[0].TextContent.Contains("Listed in", StringComparison.Ordinal));
                    Assert.False(string.IsNullOrWhiteSpace(cells[0].TextContent));
                    continue;
                }

                // Each page that holds a link to it is a link itself, the first of them first, and under it the link's words and place.
                var pages = url.Sources.DistinctBy(source => source.Url).ToList();
                Assert.Equal(pages.Select(source => source.Url), lines.Take(pages.Count).Select(line => line.QuerySelector("a")!.GetAttribute("href")));
                Assert.Equal(pages[0].Url[origin.Length..], lines[0].QuerySelector("a")!.TextContent);
                var said = plain ? ReportPlainWords.LinkWords(pages[0].Kind, pages[0].Text, pages[0].Position, pages[0].Flags) : WhereFoundColumns.Place(pages[0]);
                Assert.Equal(said, lines[0].QuerySelector(".where__what")?.TextContent);
                Assert.Equal(url.Pages > pages.Count, cells[0].TextContent.Contains($"{url.Pages - pages.Count} more page", StringComparison.Ordinal));
                if (plain)
                {
                    Assert.DoesNotContain("(content", cells[0].TextContent, StringComparison.Ordinal);
                }
            }

            // One page's links are together: a page, once left, doesn't come back.
            var runs = firstPages.Where((page, index) => index == 0 || page != firstPages[index - 1]).ToList();
            Assert.Equal(runs.Distinct().Count(), runs.Count);
        }

        // In a site owner's words: "The link that says “…”, in the main content", never "content, “…”".
        var words = Parse(ReportExport.Build(crawl.Path, settings: SETTINGS)).QuerySelector("article.problem[data-check=internal-client-error] .examples")!.TextContent;
        Assert.Matches("The link that says “[^”]+”, in the (main content|menu|header|footer|sidebar)", words);
    }

    /// <summary>
    /// A check of pages holding the bad links says what to change on each (the first few, each on a line of its own, then
    /// how many more): in plain words the link by what it says and where on the page it is, then where it leads and how
    /// that answered; in technical ones the URL with what it is and how the page links to it. Any other check of addresses
    /// (a server error, a chain of forwards) leads with the address, the pages that link to it after.
    /// </summary>
    [Fact]
    public void APageHoldingBadLinksSaysWhatToChangeOnIt()
    {
        var check = IssueCatalog.BROKEN_INTERNAL_LINKS;
        var named = FoundOnReader.ReadTargets(crawl.Path, check.Id, [.. FirstRows(check.Id, 10).Select(row => row.Id)], ReportReader.LISTED_TARGETS)!;

        var technical = Parse(ReportExport.Build(crawl.Path, new ReportOptions { Examples = 10, LookExamples = 10, Wording = ReportWording.Technical }, SETTINGS));
        var pages = technical.QuerySelector($"article.problem[data-check={check.Id}]")!;
        Assert.Equal(["Page", "What was found", "Where to fix it"], pages.QuerySelectorAll(".examples thead th").Select(cell => cell.TextContent));
        foreach (var (row, page) in pages.QuerySelectorAll(".examples tbody tr").Zip(named))
        {
            var cells = row.QuerySelectorAll("td");
            Assert.Matches("^[0-9,]+ broken URLs?", cells[1].TextContent);
            var lines = cells[2].QuerySelectorAll(".where").ToList();
            var more = page.Total - page.Targets.Count;
            Assert.Equal(page.Targets.Count + (more > 0 ? 1 : 0), lines.Count);
            Assert.Equal($"{lines[0].QuerySelector("a")!.TextContent} ({WhereFoundColumns.TargetFacts(check, page.Targets[0])})", lines[0].TextContent);
            Assert.Equal(more > 0, lines[^1].TextContent == $"and {more} {(more == 1 ? "other" : "others")}");
        }

        var plain = Parse(ReportExport.Build(crawl.Path, new ReportOptions { Examples = 10, LookExamples = 10 }, SETTINGS));
        pages = plain.QuerySelector($"article.problem[data-check={check.Id}]")!;
        Assert.Equal(["Page to edit", "What was found", "What to change on it"], pages.QuerySelectorAll(".examples thead th").Select(cell => cell.TextContent));
        foreach (var (row, page) in pages.QuerySelectorAll(".examples tbody tr").Zip(named))
        {
            var cells = row.QuerySelectorAll("td");
            Assert.Matches("^[0-9,]+ broken links?", cells[1].TextContent);
            var lines = cells[2].QuerySelectorAll(".where").ToList();
            var first = page.Targets[0];
            var element = ReportPlainWords.LinkWords(first.Kind, first.Text, first.Position, first.Flags) ?? "A link";
            Assert.StartsWith(element + "It leads to ", lines[0].TextContent, StringComparison.Ordinal);
            Assert.Equal(first.Url, lines[0].QuerySelector(".where__what a")!.GetAttribute("href"));
            Assert.Contains($", which {ReportPlainWords.Status(first.Facts?.Final?.StatusCode ?? first.StatusCode, first.Facts?.Final?.Outcome ?? first.Outcome)}", lines[0].TextContent, StringComparison.Ordinal);
            var more = page.Total - page.Targets.Count;
            Assert.Equal(more > 0, lines[^1].TextContent == ReportPlainWords.Sentence(ReportPlainWords.Others(check.Id, more)));
        }

        // A page naming more than the report lists says how many more, by what they are. A thing worth a look, which is
        // often on every page and sets its list beside its text, names two to a page, where a problem to fix names five.
        Assert.Equal((5, 2), (ReportReader.LISTED_TARGETS, ReportReader.LISTED_LOOK_TARGETS));
        Assert.Equal(IssueSeverity.Notice, IssueCatalog.Find("links-to-redirects")!.Severity);
        var redirects = FoundOnReader.ReadTargets(crawl.Path, "links-to-redirects", [.. FirstRows("links-to-redirects", 10).Select(row => row.Id)], ReportReader.LISTED_LOOK_TARGETS)!;
        var most = redirects.First(page => page.Total > page.Targets.Count);
        var listed = plain.QuerySelectorAll("article.problem[data-check=links-to-redirects] tbody tr").First(row => row.TextContent.Contains(" other link", StringComparison.Ordinal)).QuerySelectorAll(".where").ToList();
        Assert.Equal(ReportReader.LISTED_LOOK_TARGETS + 1, listed.Count);
        Assert.Equal(ReportPlainWords.Sentence(ReportPlainWords.Others("links-to-redirects", most.Total - most.Targets.Count)), listed[^1].TextContent);

        // The technical words a client can't read are nowhere in the plain report's rows (the audit's S-11 list).
        var rows = string.Join('\n', plain.QuerySelectorAll(".examples").Select(table => table.TextContent));
        foreach (var jargon in new[] { "not indexable", "Inlinks:", "Linking pages:", "Type: Page", " broken URL", "(200; ", "; content", "Response time:", "Redirect kind:" })
        {
            Assert.DoesNotContain(jargon, rows, StringComparison.Ordinal);
        }

        Assert.DoesNotMatch("H[1-6] → H[1-6]", rows);
        Assert.DoesNotMatch("(Empty|Missing) og:", rows);

        // A check of addresses that isn't fixed at the link leads with the address; where it's linked from comes after.
        foreach (var (document, where) in new[] { (plain, "Where it’s linked from"), (technical, "Where to fix it") })
        {
            Assert.Equal(["Address", "What was found", where], document.QuerySelector("article.problem[data-check=redirect-chain]")!.QuerySelectorAll(".examples thead th").Select(cell => cell.TextContent));
        }
    }

    /// <summary>
    /// The full report is many screens long (the audit's A-12: 17,000 px with nothing to steer by): it lists its parts under
    /// its head, each a link to its part and there only when the part is; "Fix these first" and the score's table lead to
    /// each problem's section. The summary page, which has no sections, has none of it.
    /// </summary>
    [Fact]
    public void TheFullReportListsItsPartsAndItsFirstFixesLeadToTheirSections()
    {
        var results = IssueReader.ReadResults(crawl.Path)!;
        var document = Parse(ReportExport.Build(crawl.Path, settings: SETTINGS));
        AssertInert(document);
        var contents = document.QuerySelectorAll("nav.contents a").ToList();
        int Of(IssueSeverity severity) => results.FixFirst.Count(id => IssueCatalog.Find(id)!.Severity == severity);
        string[] parts =
        [
            "Summary", "Fix these first", "How the score is worked out", "What to fix", $"Serious ({Of(IssueSeverity.Error)})", $"Worth fixing ({Of(IssueSeverity.Warning)})",
            $"Worth a look ({Of(IssueSeverity.Notice)})", "What couldn’t be checked", "What was checked and found nothing wrong", "About this check"
        ];
        var skipped = IssueCatalog.All.Count - results.Counts.Count;
        Assert.Equal(parts.Where(part => skipped > 0 || part != "What couldn’t be checked"), contents.Select(link => link.TextContent));

        // Every link of the document that stays inside it leads to something that is there, once.
        var inside = document.QuerySelectorAll("a[href^='#']").ToList();
        Assert.All(inside, link => Assert.Single(document.QuerySelectorAll($"[id='{link.GetAttribute("href")![1..]}']")));
        Assert.All(inside, link => Assert.Null(link.GetAttribute("target")));

        // "Fix these first" and the score's rows lead to their problems' sections, which carry the same titles.
        var first = document.QuerySelectorAll("ol.first a.first__title").ToList();
        Assert.Equal(results.FixFirst.Take(5).Select(id => $"#problem-{id}"), first.Select(link => link.GetAttribute("href")));
        Assert.All(first, link => Assert.EndsWith(link.TextContent, document.QuerySelector($"[id='{link.GetAttribute("href")![1..]}'] h3 > span")!.TextContent, StringComparison.Ordinal));
        Assert.Equal(
            results.FixFirst.Where(id => IssueCatalog.Find(id)!.Severity != IssueSeverity.Notice).Select(id => $"#problem-{id}"),
            document.QuerySelectorAll("table.score-table tbody a").Select(link => link.GetAttribute("href")));

        // Where the app's preview puts its own way into each problem: a comment, so a saved file has no control that leads nowhere.
        var html = ReportExport.Build(crawl.Path, settings: SETTINGS);
        Assert.All(results.FixFirst, id => Assert.Contains($"</span><!--si:open {id}-->", html, StringComparison.Ordinal));

        // On paper a link is plain text; on a screen it looks like one.
        var sheet = document.QuerySelector("style")!.TextContent;
        Assert.Contains("a { color: inherit; text-decoration: none; }", sheet, StringComparison.Ordinal);
        Assert.Contains("@media screen {", sheet, StringComparison.Ordinal);
        Assert.Contains("a { color: var(--link); text-decoration: underline;", sheet, StringComparison.Ordinal);

        var summary = Parse(ReportExport.Build(crawl.Path, new ReportOptions { Cut = ReportCut.Summary }, SETTINGS));
        Assert.Null(summary.QuerySelector("nav.contents"));
        Assert.Empty(summary.QuerySelectorAll("a[href^='#']"));
        Assert.Equal(5, summary.QuerySelectorAll("ol.first span.first__title").Length);
    }

    /// <summary>
    /// A problem lists every one of its pages while they fit (the audit's A-12: "The first 5 of 8 … And 3 more: the full
    /// list is on sheet 1" sent a client to a second file for three rows): up to 25 for a problem to fix and 10 for a
    /// thing worth a look, unless asked otherwise.
    /// </summary>
    [Fact]
    public void AProblemListsEveryPageWhileTheyFit()
    {
        Assert.Equal((25, 10), (ReportOptions.DEFAULT_EXAMPLES, ReportOptions.DEFAULT_LOOK_EXAMPLES));
        var counts = IssueReader.ReadResults(crawl.Path)!.Counts;
        var document = Parse(ReportExport.Build(crawl.Path, settings: SETTINGS));
        var between = counts.Where(count => count.Value is > 5 and <= 25 && IssueCatalog.Find(count.Key)!.Severity != IssueSeverity.Notice).Select(count => count.Key).ToList();
        Assert.NotEmpty(between);
        foreach (var id in between)
        {
            var problem = document.QuerySelector($"article.problem[data-check={id}]")!;
            Assert.Equal($"All {counts[id]}", problem.QuerySelector(".examples caption")!.TextContent);
            Assert.Equal(counts[id], problem.QuerySelectorAll(".examples tbody tr").Length);
            Assert.Null(problem.QuerySelector(".more"));
        }
    }

    /// <summary>The technical report carries each check's own name and severity beside its title, and its rows as the engine says them.</summary>
    [Fact]
    public void TheTechnicalReportSpeaksAsTheChecksDo()
    {
        var results = IssueReader.ReadResults(crawl.Path)!;
        var document = Parse(ReportExport.Build(crawl.Path, new ReportOptions { Wording = ReportWording.Technical, LookExamples = 50 }, SETTINGS));
        AssertInert(document);
        Assert.Equal(
            GridListing.Listed(results.FixFirst).Select(listed => $"{IssueCatalog.Find(listed.Id)!.Title} · {IssueCatalog.Find(listed.Id)!.Severity}"),
            document.QuerySelectorAll("article.problem .problem__tech").Select(tech => tech.TextContent));
        Assert.Null(document.QuerySelector("table.names"));
        string Examples(string id) => document.QuerySelector($"article.problem[data-check={id}] .examples")!.TextContent;
        Assert.Matches("[0-9,]+ px, over [0-9,]+ \\([0-9]+ characters\\)", Examples("title-too-long"));
        Assert.Matches("H1 → H[3-6]", Examples("heading-levels-skipped"));
        Assert.Matches("4[0-9]{2} [A-Z][a-z]+", Examples("internal-client-error"));
        Assert.Contains("Type: ", Examples("internal-client-error"), StringComparison.Ordinal);
    }

    /// <summary>A line of evidence that only repeats the plain detail is left out of a plain row; one that adds a text or a number stays.</summary>
    [Theory]
    [InlineData("5 clicks from the home page; at most 4 is advised", "5 clicks from the home page", true)]
    [InlineData("5 clicks from the home page; at most 4 is advised", "1 link to it", false)]
    [InlineData("The main heading is a picture: “Synthetic Store logo”", "Main heading: “Synthetic Store logo”", true)]
    [InlineData("1,026 links to your own pages, more than 1,000", "1,026 links on the page", true)]
    [InlineData("12 pages link here", "1 link to it", false)]
    [InlineData("It couldn’t be reached", "It couldn’t be reached", true)]
    [InlineData("Pages use it as a script, but the server says it’s plain text", "The server says it’s plain text", true)]
    [InlineData("Only 16 characters long", "“Garden Furniture”", false)]
    [InlineData("Another page has the same title", "“Login”", false)]
    [InlineData("It links to none of your other pages", "No links on the page", false)]
    public void EvidenceThatOnlyRepeatsThePlainDetailIsLeftOut(string detail, string evidence, bool repeats) =>
        Assert.Equal(repeats, ReportRows.SaidByTheDetail(detail, evidence));

    /// <summary>
    /// What a site wrote can be one unbroken run of any length (a detail, a fault's text): it wraps inside its cell, where
    /// it would otherwise widen its table past the edge of the page and be cut off in print.
    /// </summary>
    [Fact]
    public void ASitesUnbrokenTextWrapsWhereItIsPrinted()
    {
        var sheet = Parse(ReportExport.Build(crawl.Path, settings: SETTINGS)).QuerySelector("style")!.TextContent;
        Assert.Contains(".examples td, .examples--one .found, .problem .onefix { overflow-wrap: anywhere; }", sheet, StringComparison.Ordinal);
    }

    /// <summary>
    /// Results an engine from before the score worked out have no score: the report says so, and to work them out again,
    /// and still lists what they found, in the engine's order.
    /// </summary>
    [Fact]
    public void ResultsFromBeforeTheScoreHaveNoScoreAndSaySo()
    {
        var earlier = crawl.Copy();
        Change(earlier, "UPDATE analysis SET score_version = NULL, score = NULL, score_partial = NULL, score_json = NULL, crawled_urls = NULL");
        var results = IssueReader.ReadResults(earlier)!;
        Assert.Equal(((SiteScore?)null, (NoScoreReason?)NoScoreReason.NotScored), (results.Score, results.NoScoreReason));
        var document = Parse(ReportExport.Build(earlier, settings: SETTINGS));
        AssertInert(document);

        Assert.Equal("No score", document.QuerySelector("svg.ring")!.GetAttribute("aria-label"));
        Assert.Equal("These results were worked out before the score was.", document.QuerySelector(".summary__sentence")!.TextContent);
        Assert.EndsWith("Work out the results again to score the site.", document.QuerySelector(".summary__covers")!.TextContent, StringComparison.Ordinal);
        string[] outline = ["Fix these first", "What to fix", "What couldn’t be checked", "What was checked and found nothing wrong", "About this check"];
        var skipped = IssueCatalog.All.Count - results.Counts.Count;
        Assert.Equal(outline.Where(heading => skipped > 0 || heading != "What couldn’t be checked"), document.QuerySelectorAll("h2").Select(heading => heading.TextContent));

        // The list follows the engine's order, and each problem says how many it's on: there is no total stored to give a share of.
        var order = results.FixFirst;
        Assert.Equal(order.Take(5).Select(id => IssueCatalog.Find(id)!.Plain!.Title), document.QuerySelectorAll("ol.first .first__title").Select(title => title.TextContent));
        Assert.Equal("Each is explained under the same number in “What to fix”, with the first pages it’s on.", document.QuerySelector(".first__foot")!.TextContent);
        var lines = document.QuerySelectorAll(".problem__meta b").Select(line => line.TextContent).ToList();
        var listed = GridListing.Listed(order);
        for (var index = 0; index < listed.Count; index++)
        {
            var check = IssueCatalog.Find(listed[index].Id)!;
            Assert.Equal(
                check.ReportedOnce ? "Checked once, for the whole site" : ReportWords.Plural(results.Counts[check.Id], check.Subject == IssueSubject.Page ? "page" : "address", check.Subject == IssueSubject.Page ? "pages" : "addresses"),
                lines[index]);
        }

        Assert.StartsWith("No score: these results were worked out before the score was.", (string)SummaryFact(earlier, "Score")!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A check that couldn't run is named by what it looks for, where it says: its title, a sentence about the site, would
    /// read as a finding. Its reason is in plain words with what would let it run; a reason this engine has no words for
    /// (a later engine's) is said as the analysis stored it.
    /// </summary>
    [Fact]
    public void ACheckThatCouldNotRunIsListedWithWhy()
    {
        var skipped = crawl.Copy();
        Change(skipped, "DELETE FROM skipped_checks");
        Change(skipped, "DELETE FROM issues WHERE issue IN ('title-missing', 'sitemap-missing', 'h1-missing')");
        Change(skipped, "DELETE FROM issue_types WHERE id IN ('title-missing', 'sitemap-missing', 'h1-missing')");
        Change(skipped, """
            INSERT INTO skipped_checks (check_id, reason, text) VALUES
                ('title-missing', 'crawl-unfinished', 'The crawl was stopped before it finished.'),
                ('sitemap-missing', 'sitemaps-off', 'The crawl was set not to read sitemaps.'),
                ('h1-missing', 'a-later-engines-reason', 'What a later engine said of it.')
            """);
        var document = Parse(ReportExport.Build(skipped, settings: SETTINGS));

        var unfinished = PlainWords.SkipReason(SkippedCheckReasons.CRAWL_UNFINISHED)!;
        var sitemapsOff = PlainWords.SkipReason(SkippedCheckReasons.SITEMAPS_OFF)!;
        Assert.Equal("sitemaps-off", SkippedCheckReasons.SITEMAPS_OFF);
        Assert.Null(IssueCatalog.Find("title-missing")!.Plain!.LooksFor);
        var listed = document.QuerySelectorAll(".plain-list li").Select(item => item.TextContent).ToList();
        Assert.Contains($"{IssueCatalog.Find("title-missing")!.Plain!.Title}. {unfinished.Text} {unfinished.Action}", listed);
        Assert.Contains($"A missing sitemap. {sitemapsOff.Text} {sitemapsOff.Action}", listed);
        Assert.Contains($"{IssueCatalog.Find("h1-missing")!.Plain!.Title}. What a later engine said of it.", listed);
        Assert.DoesNotContain(listed, item => item.StartsWith(IssueCatalog.Find("sitemap-missing")!.Plain!.Title, StringComparison.Ordinal));
    }

    [Fact]
    public void ACrawlNeverAnalysedHasNoReport()
    {
        var never = crawl.Copy();
        Change(never, "DELETE FROM analysis");
        var failure = Assert.Throws<ExportException>(() => ReportExport.Build(never, settings: SETTINGS));
        Assert.Equal(ExportFailure.NotAnalyzed, failure.Failure);

        var path = Path.Combine(_folder, "never.html");
        Assert.Equal(ExportFailure.NotAnalyzed,
            Assert.Throws<ExportException>(() => CrawlExporter.Export(never, new ExportRequest { Kind = ExportKind.Report, Format = ExportFormat.Html }, path, SETTINGS)).Failure);
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public void ResultsThatNoLongerStandAreSaidToBeOutOfDate()
    {
        var settings = SETTINGS with { OutOfDateBy = new AnalysisSettings() };
        Assert.Empty(Parse(ReportExport.Build(crawl.Path, settings: settings, outOfDateBy: settings.OutOfDateBy)).QuerySelectorAll("header .note"));

        var note = Parse(ReportExport.Build(crawl.Changed, settings: settings, outOfDateBy: settings.OutOfDateBy)).QuerySelector("header .note")!.TextContent;
        Assert.StartsWith("These results were worked out on ", note, StringComparison.Ordinal);
        Assert.Contains("the site check has gone on since", note, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReportIsSavedAsTheDocumentItIs()
    {
        var path = Path.Combine(_folder, "report.html");
        var options = new ReportOptions { Cut = ReportCut.Summary };
        var result = CrawlExporter.Export(crawl.Path, new ExportRequest { Kind = ExportKind.Report, Format = ExportFormat.Html, Report = options }, path, SETTINGS);
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(bytes.Length, result.Bytes);
        Assert.Equal(0, result.Rows);

        // UTF-8 with no byte-order mark: the document says its own encoding.
        Assert.Equal("<!doctype html>"u8.ToArray(), bytes[..15]);
        Assert.Equal(ReportExport.Build(crawl.Path, options, SETTINGS), Encoding.UTF8.GetString(bytes));
        Assert.Equal([path], Directory.GetFiles(_folder));
    }

    private static AngleSharp.Html.Dom.IHtmlDocument Parse(string html) => new HtmlParser().ParseDocument(html);

    /// <summary>A check's first rows as its list gives them with no order asked for: the most important first.</summary>
    private IReadOnlyList<UrlRow> FirstRows(string checkId, int count) =>
        IssueGroupReader.ReadRows(crawl.Path, checkId, new UrlQuery { Columns = ["url"], Limit = count })!.Page!.Rows;

    /// <summary>A copy of the crawl as one stopped or failed part-way leaves it, with addresses still to check, analysed as it then stands.</summary>
    private async Task<string> EndedEarlyAsync(CrawlStatus status)
    {
        var copy = crawl.Copy();
        Change(copy, $"UPDATE crawl SET status = {(int)status}");
        Change(copy, "UPDATE urls SET state = 0 WHERE id IN (SELECT id FROM urls WHERE id NOT IN (SELECT url_id FROM responses) LIMIT 7)");
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(copy);
        return copy;
    }

    /// <summary>A crawl of the synthetic site with other options than the shared one's, analysed.</summary>
    private async Task<string> CrawlAsync(string name, Func<CrawlOptions, CrawlOptions> change)
    {
        var path = Path.Combine(_folder, name + ".sicrawl");
        var summary = await TestCrawls.RunAsync(change(new CrawlOptions { StartUrl = crawl.Server.BaseUri.AbsoluteUri, Concurrency = 8 }), path);
        Assert.Equal(CrawlStatus.Completed, summary.Status);
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        return path;
    }

    /// <summary>
    /// Stores the score the analysis would have stored for the counts the file now holds, as its writer does. A test that
    /// changes what was found by hand needs it: the report says the stored score, and works none out.
    /// </summary>
    private static void StoreTheScoreOfItsCounts(string path)
    {
        var results = IssueReader.ReadResults(path)!;
        var covers = results.Score!.Covers;
        List<IssueCount> checks = [.. IssueCatalog.All.Where(check => results.Counts.ContainsKey(check.Id)).Select(check => new IssueCount(check, results.Counts[check.Id]))];
        var score = ScoreCalculator.Calculate(checks, IssueCatalog.All.Count - checks.Count, covers.Pages, covers.Urls, covers.Reason, covers.BlockedByRobots)!;
        Change(path, "UPDATE analysis SET score = $score, score_partial = $partial, score_json = $json",
            ("$score", score.Value), ("$partial", score.Partial ? 1 : 0), ("$json", JsonSerializer.Serialize(score, EngineJsonContext.Default.SiteScore)));
        Assert.Equal(score.Value, IssueReader.ReadResults(path)!.Score!.Value);
    }

    /// <summary>A fact of the workbook's summary sheet, by its label.</summary>
    private object? SummaryFact(string crawlPath, string label)
    {
        var path = Path.Combine(_folder, $"{Guid.NewGuid():N}.xlsx");
        CrawlExporter.Export(crawlPath, new ExportRequest { Kind = ExportKind.Workbook, Format = ExportFormat.Xlsx }, path, SETTINGS);
        var fact = XlsxFile.Read(path).Sheets[0].Values.Single(row => Equals(row[0], label))[1];
        File.Delete(path);
        return fact;
    }

    /// <summary>
    /// Nothing in the document runs or asks for anything: no script, no handler, no style attribute, no frame, image or
    /// form, and no link but to http or https, each opened apart and telling the site nothing, or to a part of the
    /// document itself: one of its own ids, which no text a site sends can be.
    /// </summary>
    private static void AssertInert(IDocument document)
    {
        Assert.Empty(document.QuerySelectorAll("script, iframe, frame, object, embed, img, video, audio, source, link, base, form, input, button, textarea, select, applet"));
        Assert.Single(document.QuerySelectorAll("style"));
        foreach (var element in document.All)
        {
            foreach (var attribute in element.Attributes)
            {
                Assert.False(attribute.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase), $"<{element.LocalName} {attribute.Name}>");
                Assert.False(attribute.Name is "style" or "src" or "srcset" or "action" or "formaction" or "xlink:href" or "background", $"<{element.LocalName} {attribute.Name}>");
                if (attribute.Name == "href")
                {
                    Assert.Equal("a", element.LocalName);
                    if (attribute.Value.StartsWith('#'))
                    {
                        Assert.Matches("^#[a-z][a-z0-9-]*$", attribute.Value);
                        Assert.Single(document.All, target => target.Id == attribute.Value[1..]);
                        Assert.Null(element.GetAttribute("target"));
                        continue;
                    }

                    Assert.True(attribute.Value.StartsWith("http://", StringComparison.Ordinal) || attribute.Value.StartsWith("https://", StringComparison.Ordinal), attribute.Value);
                    Assert.Equal("_blank", element.GetAttribute("target"));
                    Assert.Equal("noopener noreferrer nofollow", element.GetAttribute("rel"));
                }
            }
        }

        // Only what the document writes itself is inside an SVG: no foreign content, no use of anything outside.
        Assert.Empty(document.QuerySelectorAll("svg foreignObject, svg use, svg image, svg a, svg script, svg style"));
        Assert.All(document.QuerySelectorAll("meta[http-equiv]"), meta => Assert.Equal("Content-Security-Policy", meta.GetAttribute("http-equiv")));
    }
}
