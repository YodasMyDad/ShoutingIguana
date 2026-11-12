using System.Globalization;
using ShoutingIguana.Cli.Output;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Cli.Tests.Output;

public sealed class OutputTests
{
    [Theory]
    [InlineData(0, 5, 7, "05:07")]
    [InlineData(1, 2, 3, "1:02:03")]
    [InlineData(49, 12, 3, "49:12:03")]
    public void DurationsKeepEveryHour(int hours, int minutes, int seconds, string expected) =>
        Assert.Equal(expected, Format.Duration(new TimeSpan(hours, minutes, seconds)));

    [Fact]
    public void CrawledTextCantControlTheTerminal()
    {
        var rightToLeftOverride = (char)0x202E;
        Assert.Equal(@"title\u001B]0;owned\u0007 and \" + "u202Eevil", Format.Printable($"title\u001b]0;owned\u0007 and {rightToLeftOverride}evil"));
        Assert.Equal(@"line\u000Abreak", Format.Printable("line\nbreak"));

        // Every bidirectional control: the isolates and marks reorder text as much as the overrides do.
        foreach (var control in new[] { 0x061C, 0x200E, 0x200F, 0x202A, 0x202E, 0x2066, 0x2067, 0x2068, 0x2069 })
        {
            Assert.Equal($@"a\u{control:X4}b", Format.Printable($"a{(char)control}b"));
        }

        const string plain = "https://example.com/caf%C3%A9 Café 東京";
        Assert.Same(plain, Format.Printable(plain));
        Assert.Equal(string.Empty, Format.Printable(null));
    }

    /// <summary>The score's breakdown: what it covers and why it is partial, a line for each check that cost points, the sum, and the ceiling.</summary>
    [Fact]
    public void TheScoresBreakdownSaysWhatItCoversAndWhatEachCheckCost()
    {
        var score = ScoreCalculator.Calculate(
            [Check("broken-internal-links", 3), Check("internal-client-error", 30), Check("title-duplicate", 0), Check("description-missing", 40)],
            checksSkipped: 2, pages: 312, urls: 2_104, ScoreIncompleteReason.Stopped, blockedByRobots: 1_080)!;

        Assert.Equal(
            """
            Score 89 of 100 so far: Good
              Covers 312 pages and 2,104 URLs. 4 of 6 checks ran; 2 couldn't.
              The crawl was stopped before it finished, so the score is of what it read so far.
              robots.txt kept the crawler out of 1,080 URLs, which weren't read.

              Error    Internal URL returns 4xx                       30 of 2,104 URLs: some             -4   internal-client-error
              Error    Links to broken internal URLs                  3 of 312 pages: a few              -2   broken-internal-links
              Notices take nothing off: 1 notice check found something.

              100 - 6 = 94
              Held at 89 while an error check has issues (94 otherwise).
              Fix first: internal-client-error, broken-internal-links. Without them the score would be 100.
              Score version 1.

            """.ReplaceLineEndings("\n"), Printed(score));
    }

    /// <summary>A fault of the whole site isn't a share of anything, and a score can't go below 0 however much is lost.</summary>
    [Fact]
    public void TheScoresBreakdownSaysTheWholeSiteAndStopsAtNought()
    {
        var score = ScoreCalculator.Calculate([Check("no-https", 1), Check("robots-txt-unreachable", 1)], checksSkipped: 0, pages: 100, urls: 500, null, 0)!;

        Assert.Equal(
            """
            Score 0 of 100: Poor
              Covers 100 pages and 500 URLs. 2 of 2 checks ran.

              Error    robots.txt unavailable                         the whole site                    -60   robots-txt-unreachable
              Error    Site not on HTTPS                              the whole site                    -60   no-https

              100 - 120 = 0 (it can't go below 0)
              Fix first: robots-txt-unreachable, no-https. Without them the score would be 100.
              Score version 1.

            """.ReplaceLineEndings("\n"), Printed(score));
    }

    /// <summary>No score is said with its reason, in the breakdown and in the line si issues leads with.</summary>
    [Fact]
    public void NoScoreSaysWhy()
    {
        var noPages = new AnalysisInfo(DateTimeOffset.UnixEpoch, "2.0.0", PagesAnalyzed: 0, TimeSpan.Zero) { ScoreVersion = ScoreCalculator.VERSION };
        var earlier = new AnalysisInfo(DateTimeOffset.UnixEpoch, "2.0.0", PagesAnalyzed: 265, TimeSpan.Zero);
        var scored = earlier with { ScoreVersion = ScoreCalculator.VERSION, Score = new SiteScoreSummary(85, ScoreBand.Good, Partial: false) };

        Assert.Equal("No score: the crawl read no page, so there is nothing to score.", ScorePrinter.Headline(noPages));
        Assert.Equal("No score: this analysis was made before the score. Analyse the crawl again (si analyze) to score it.", ScorePrinter.Headline(earlier));
        Assert.Equal("Score 85 of 100: Good", ScorePrinter.Headline(scored));
        Assert.Equal("Score 62 of 100 so far: Needs work", ScorePrinter.Headline(scored with { Score = new SiteScoreSummary(62, ScoreBand.NeedsWork, Partial: true) }));
        Assert.Equal("Score 90 of 100: Very good", ScorePrinter.Headline(scored with { Score = new SiteScoreSummary(90, ScoreBand.VeryGood, Partial: false) }));

        using var output = new StringWriter();
        ScorePrinter.Print(new AnalysisResults(noPages, DateTimeOffset.UnixEpoch, new Dictionary<string, long>(), [], []), output);
        ScorePrinter.Print(new AnalysisResults(earlier, DateTimeOffset.UnixEpoch, new Dictionary<string, long>(), [], []), output);
        Assert.Equal(
            $"No score: the crawl read no page, so there is nothing to score.{Environment.NewLine}" +
            $"No score: this analysis was made before the score. Analyse the crawl again (si analyze) to score it.{Environment.NewLine}", output.ToString());
    }

    private static IssueCount Check(string id, long count) => new(IssueCatalog.Find(id)!, count);

    /// <summary>The breakdown as si score prints it, for an analysis that stored this score.</summary>
    private static string Printed(SiteScore score)
    {
        var analysis = new AnalysisInfo(DateTimeOffset.UnixEpoch, "2.0.0", score.Covers.Pages, TimeSpan.Zero)
        {
            ScoreVersion = score.Version, Score = new SiteScoreSummary(score.Value, score.Band, score.Partial)
        };
        using var output = new StringWriter();

        // The commands group thousands as the machine does; the expected text here groups them with commas.
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            ScorePrinter.Print(new AnalysisResults(analysis, DateTimeOffset.UnixEpoch, new Dictionary<string, long>(), [], []) { Score = score }, output);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }

        return output.ToString().ReplaceLineEndings("\n");
    }

    [Fact]
    public void CsvQuotesFieldsAndDefusesFormulas()
    {
        using var text = new StringWriter();
        var csv = new CsvWriter(text);

        csv.WriteRow("plain", "a,b", "say \"hi\"", "=HYPERLINK(\"http://x\")", "-1", null);

        Assert.Equal("plain,\"a,b\",\"say \"\"hi\"\"\",\"'=HYPERLINK(\"\"http://x\"\")\",'-1,\r\n", text.ToString());
    }
}
