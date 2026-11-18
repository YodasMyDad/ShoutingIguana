using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>
/// A recheck is all or nothing. One made to fail at each stage of its run (its robots.txt stored again, a sitemap's old
/// entries deleted, part-way through its fetches, part-way through storing a page) ends failed with its reason, and
/// leaves the crawl file as it was: every table's rows, the analysis current, no recheck recorded, and a comparison with
/// the file as it was finds nothing changed. A real one left a file half-written, then analysed it: two checks "fixed".
/// </summary>
[Collection(RecheckCrawlsCollection.NAME)]
public sealed class RecheckAllOrNothingTests(RecheckCrawls crawls)
{
    private const string FAULT = "The disk went away (a test's fault)";

    [Theory]
    [InlineData(nameof(RecheckStage.RobotsStored))]
    [InlineData(nameof(RecheckStage.SitemapCleared))]
    [InlineData(nameof(RecheckStage.Fetching))]
    [InlineData(nameof(RecheckStage.PageStoring))]
    public async Task ARecheckThatFailsLeavesTheFileAsItWas(string failsAt)
    {
        var stage = Enum.Parse<RecheckStage>(failsAt);
        var path = crawls.Copy(crawls.Then);
        var before = crawls.Copy(path);
        var plan = RecheckPlanReader.Plan(path, new RecheckAsk("not-in-sitemap"), new AnalysisSettings());
        plan = plan with { Kinds = [RecheckKind.Urls, RecheckKind.RobotsTxt, RecheckKind.Sitemaps] };
        var pagesStoring = 0;
        var failedAt = 0;
        var request = new RecheckRequest(path, plan, CrawlDatabase.ReadOptions(path) with { MaxRequestsPerSecond = 20, Concurrency = 1 })
        {
            AtStage = at =>
            {
                // Storing a page fails on the second, so one is already in.
                if (at == stage && (at != RecheckStage.PageStoring || ++pagesStoring == 2) && Interlocked.Exchange(ref failedAt, 1) == 0)
                {
                    throw new InvalidOperationException(FAULT);
                }
            }
        };

        CrawlSummary run;
        await using (var job = new CrawlEngine(NullLoggerFactory.Instance).Recheck(request))
        {
            run = await job.Completion.WaitAsync(TestCrawls.TIMEOUT);
        }

        Assert.Equal(1, failedAt);
        Assert.Equal(CrawlStatus.Failed, run.Status);
        Assert.Contains(FAULT, run.Error);
        AssertSameRows(before, path);
        Assert.Empty(RecheckReader.ReadAll(path));
        Assert.Empty(CrawlSummaryReader.Read(path).OutOfDate(new AnalysisSettings()));

        var comparison = CrawlComparisonReader.ReadSummary(path, before)!;
        Assert.Equal(0, comparison.Score?.Change ?? 0);
        Assert.All(comparison.Checks, check => Assert.Equal(CheckChangeState.Same, check.State));
    }

    /// <summary>A recheck that ends well commits as a whole: its rows and the URLs it read again, at once.</summary>
    [Fact]
    public async Task ARecheckThatEndsWellIsStoredWhole()
    {
        var path = crawls.Copy(crawls.Then);

        var (run, recheck) = await RecheckCrawls.RecheckAsync(path, new RecheckAsk("not-in-sitemap"));

        Assert.Equal((CrawlStatus.Completed, RecheckStatus.Completed), (run.Status, recheck.Status));
        Assert.Equal(recheck.Judged, TestCrawls.Count(path, $"SELECT COUNT(*) FROM recheck_urls WHERE role = {(int)RecheckRole.Judged} AND answer IS NOT NULL"));
    }

    /// <summary>Every table of <paramref name="before"/> has the same rows in <paramref name="after"/>; a table only the recheck's upgrade made is empty.</summary>
    private static void AssertSameRows(string before, string after)
    {
        var tables = Tables(before);
        foreach (var table in tables)
        {
            Assert.True(TestCrawls.Rows(before, $"SELECT * FROM \"{table}\"").SequenceEqual(TestCrawls.Rows(after, $"SELECT * FROM \"{table}\"")), $"{table} changed");
        }

        foreach (var table in Tables(after).Except(tables))
        {
            Assert.Equal(0, TestCrawls.Count(after, $"SELECT COUNT(*) FROM \"{table}\""));
        }
    }

    private static List<string> Tables(string path) =>
        TestCrawls.Rows(path, "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'");
}
