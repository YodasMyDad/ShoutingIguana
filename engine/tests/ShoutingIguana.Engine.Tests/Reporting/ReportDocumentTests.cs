using System.Security.Cryptography;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Time.Testing;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Export;
using ShoutingIguana.Engine.Export.Models;
using ShoutingIguana.Engine.Reporting;
using ShoutingIguana.Engine.Reporting.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Tests.Support;
using static ShoutingIguana.Engine.Tests.Support.GridQueries;

namespace ShoutingIguana.Engine.Tests.Reporting;

/// <summary>
/// The report, read back as a browser parses it: its outline and its numbers against the analysis, each state the design
/// names, both cuts, and that nothing a site sends becomes markup, a link to anything but http or https, or a request.
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
        var order = ReportSources.FixFirst(results);
        var document = Parse(ReportExport.Build(crawl.Path, settings: SETTINGS));

        Assert.Equal("en-GB", document.DocumentElement.GetAttribute("lang"));
        Assert.Equal("127.0.0.1", document.QuerySelector("h1")!.TextContent);
        Assert.StartsWith("Website check: 127.0.0.1, ", document.Title, StringComparison.Ordinal);
        var meta = document.QuerySelector("p.meta")!.TextContent;
        Assert.Contains($"{results.Analysis.PagesAnalyzed:N0} pages read", meta, StringComparison.Ordinal);
        Assert.Contains($"{TestCrawls.Count(crawl.Path, "SELECT COUNT(*) FROM responses"):N0} addresses checked", meta, StringComparison.Ordinal);
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

        // Every check that found something has its section, numbered in the order to fix them, under its technical name.
        var problems = document.QuerySelectorAll("article.problem").ToList();
        Assert.Equal(order.Count, problems.Count);
        for (var index = 0; index < order.Count; index++)
        {
            var check = IssueCatalog.Find(order[index])!;
            Assert.StartsWith($"{index + 1}. ", problems[index].QuerySelector("h3 > span")!.TextContent, StringComparison.Ordinal);
            Assert.Equal($"{check.Title} · {check.Severity}", problems[index].QuerySelector(".problem__tech")!.TextContent);
            var listed = problems[index].QuerySelectorAll(".examples tbody tr").Length;
            var most = check.Severity == IssueSeverity.Notice ? ReportOptions.DEFAULT_LOOK_EXAMPLES : ReportOptions.DEFAULT_EXAMPLES;
            Assert.Equal((int)Math.Min(most, results.Counts[check.Id]), listed);
            if (results.Counts[check.Id] > most)
            {
                Assert.Equal($"And {results.Counts[check.Id] - most:N0} more: the full list is on sheet {index + 1} of the spreadsheet.", problems[index].QuerySelector(".more")!.TextContent);
            }
        }

        // The first list is the first five problems to fix, under the same numbers.
        var first = document.QuerySelectorAll("ol.first li").ToList();
        Assert.Equal(5, first.Count);
        Assert.Equal(["1", "2", "3", "4", "5"], first.Select(item => item.QuerySelector(".first__n")!.TextContent));

        // Every check that passed has a line, and every one that couldn't run its reason.
        Assert.Equal(results.Counts.Count(count => count.Value == 0), document.QuerySelectorAll(".passed li").Length);
        Assert.Equal(IssueCatalog.All.Count - results.Counts.Count, document.QuerySelectorAll(".plain-list li").Length);
        Assert.Contains("Made with Shouting Iguana", document.QuerySelector("footer")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("on 1 November 2026", document.QuerySelector("footer")!.TextContent, StringComparison.Ordinal);
    }

    /// <summary>The score is the reference's (score.mjs), on the design's own worked examples.</summary>
    [Theory]
    [InlineData(new[] { "internal-client-error:9", "broken-internal-links:5", "image-too-large:61", "description-duplicate:8", "title-duplicate:6", "slow-response:3", "noindex-page:40" }, 265, 4521, 85, 98)]
    [InlineData(new[] { "image-missing-alt:3", "h1-missing:6" }, 48, 612, 98, 100)]
    [InlineData(new[]
    {
        "broken-internal-links:1240", "mixed-content:620", "internal-server-error:420", "title-missing:187", "internal-client-error:212", "image-broken:96",
        "redirect-loop:14", "image-missing-alt:1022", "viewport-missing:1240", "duplicate-site-version:1", "title-duplicate:540", "slow-response:310",
        "broken-external-links:44"
    }, 1240, 3870, 36, 74)]
    [InlineData(new string[0], 1208, 9412, 100, null)]
    [InlineData(new[] { "title-missing:1" }, 5, 61, 89, 100)]
    [InlineData(new[] { "no-https:1" }, 100, 500, 40, 100)]
    [InlineData(new[] { "no-https:1", "robots-txt-unreachable:1" }, 100, 500, 0, 100)]
    [InlineData(new[] { "internal-client-error:1" }, 100, 500, 89, 100)]
    public void TheStandInScoreIsTheDesignsVersionOne(string[] found, int pages, int urls, int score, int? onceFixed)
    {
        var counts = found.Select(entry => entry.Split(':')).ToDictionary(parts => parts[0], parts => long.Parse(parts[1]), StringComparer.Ordinal);
        var worked = StandInScore.Of(counts, StandInScore.FixFirst(counts), new ScoreCovers(pages, urls, 86, 0, Complete: true, 0))!;
        Assert.Equal(score, worked.Value);
        Assert.Equal(onceFixed, worked.FixedValue);
        Assert.Equal(Math.Max(0, 100 - worked.Lines.Sum(line => line.Points)), worked.BeforeCeiling);
        Assert.Null(StandInScore.Of(counts, StandInScore.FixFirst(counts), new ScoreCovers(0, urls, 86, 0, Complete: true, 0)));
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
    public void TheSummaryCutIsTheFullReportsFirstPage()
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
        Assert.Contains("from page 2 under the same number", full.QuerySelector(".first__foot")!.TextContent, StringComparison.Ordinal);

        // A check that couldn't run is listed near the full report's end, which the summary can only point to.
        var skipped = crawl.Copy();
        Change(skipped, "DELETE FROM issue_types WHERE id IN ('title-missing', 'h1-missing')");
        Assert.Contains(" 2 checks couldn’t run: they are listed near the end.", Parse(ReportExport.Build(skipped, settings: SETTINGS)).QuerySelector(".summary__covers")!.TextContent,
            StringComparison.Ordinal);
        Assert.Contains(" 2 checks couldn’t run: the full report lists them.",
            Parse(ReportExport.Build(skipped, new ReportOptions { Cut = ReportCut.Summary }, SETTINGS)).QuerySelector(".summary__covers")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void TheExamplesAreAsManyAsAskedFor()
    {
        var document = Parse(ReportExport.Build(crawl.Path, new ReportOptions { Examples = 2, LookExamples = 0 }, SETTINGS));
        var results = IssueReader.ReadResults(crawl.Path)!;
        var order = ReportSources.FixFirst(results);
        var problems = document.QuerySelectorAll("article.problem").ToList();
        for (var index = 0; index < order.Count; index++)
        {
            var notice = IssueCatalog.Find(order[index])!.Severity == IssueSeverity.Notice;
            Assert.Equal(notice ? 0 : (int)Math.Min(2, results.Counts[order[index]]), problems[index].QuerySelectorAll(".examples tbody tr").Length);
            if (notice)
            {
                Assert.Equal($"The full list is on sheet {index + 1} of the spreadsheet.", problems[index].QuerySelector(".more")!.TextContent);
            }
        }

        foreach (var options in new[] { new ReportOptions { Examples = 51 }, new ReportOptions { LookExamples = -1 } })
        {
            var failure = Assert.Throws<ExportException>(() => ReportExport.Build(crawl.Path, options, SETTINGS));
            Assert.Equal(ExportFailure.InvalidRequest, failure.Failure);
            Assert.Equal(options.Examples == 51 ? "examples" : "lookExamples", failure.Field);
        }
    }

    [Fact]
    public void ACrawlStoppedEarlySaysSoAndItsScoreIsSoFar()
    {
        var stopped = crawl.Copy();
        Change(stopped, $"UPDATE crawl SET status = {(int)CrawlStatus.Stopped}");
        Change(stopped, "UPDATE urls SET state = 0 WHERE id IN (SELECT id FROM urls WHERE id NOT IN (SELECT url_id FROM responses) LIMIT 7)");
        var queued = TestCrawls.Count(stopped, "SELECT COUNT(*) FROM urls WHERE state = 0");
        var document = Parse(ReportExport.Build(stopped, settings: SETTINGS));

        var note = document.QuerySelector("header .note")!.TextContent;
        Assert.StartsWith("This check was stopped early: ", note, StringComparison.Ordinal);
        Assert.Contains($"{queued:N0} addresses still to check", note, StringComparison.Ordinal);
        Assert.EndsWith("This report covers only what was read.", note, StringComparison.Ordinal);
        Assert.EndsWith(", so far", document.QuerySelector(".summary__word")!.TextContent, StringComparison.Ordinal);
        Assert.EndsWith(", so far", document.QuerySelector("svg.ring")!.GetAttribute("aria-label"), StringComparison.Ordinal);
        Assert.Contains("read before the check was stopped", document.QuerySelector(".summary__covers")!.TextContent, StringComparison.Ordinal);
        var finished = document.QuerySelectorAll(".facts dt").Single(term => term.TextContent == "Finished?").NextElementSibling!.TextContent;
        Assert.StartsWith("No. This check was stopped early", finished, StringComparison.Ordinal);
        Assert.EndsWith("Continue the check to read the rest.", finished, StringComparison.Ordinal);
    }

    [Fact]
    public void APerfectSiteHasNothingToFixAndAScoreOfAHundred()
    {
        var perfect = crawl.Copy();
        Change(perfect, "DELETE FROM issues");
        Change(perfect, "UPDATE issue_types SET url_count = 0");
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
    }

    [Fact]
    public void ACrawlThatReadNoPageHasNoScoreAndSaysWhy()
    {
        var none = crawl.Copy();
        Change(none, "UPDATE analysis SET pages = 0");
        var document = Parse(ReportExport.Build(none, settings: SETTINGS));

        Assert.Equal("No score", document.QuerySelector("svg.ring")!.GetAttribute("aria-label"));
        Assert.Equal("No score", document.QuerySelector(".summary__word")!.TextContent);
        Assert.Equal("No page could be read, so there is nothing to score.", document.QuerySelector(".summary__sentence")!.TextContent);
        Assert.DoesNotContain("How the score is worked out", document.QuerySelectorAll("h2").Select(heading => heading.TextContent));
        Assert.DoesNotContain("Fix these first", document.QuerySelectorAll("h2").Select(heading => heading.TextContent));
    }

    [Fact]
    public void ACheckThatCouldNotRunIsListedWithWhy()
    {
        var skipped = crawl.Copy();
        Change(skipped, "DELETE FROM issue_types WHERE id = 'title-missing'");
        Change(skipped, "INSERT INTO skipped_checks (check_id, reason, text) VALUES ('title-missing', 'crawl-unfinished', 'The crawl was stopped before it finished.')");
        var document = Parse(ReportExport.Build(skipped, settings: SETTINGS));
        Assert.Contains(document.QuerySelectorAll(".plain-list li"), item => item.TextContent == "Missing title. The crawl was stopped before it finished.");
        Assert.Contains(" 1 check couldn’t run: it is listed near the end.", document.QuerySelector(".summary__covers")!.TextContent, StringComparison.Ordinal);
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

    /// <summary>
    /// Nothing in the document runs or asks for anything: no script, no handler, no style attribute, no frame, image or
    /// form, and no link but to http or https, each opened apart and telling the site nothing.
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
