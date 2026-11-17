using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>
/// How a recheck runs (the fix loop): what it lets go, what it fetches one step and no further, what it leaves of the
/// crawl (its queue, its status, its time), how its result is settled once and kept, and when it can't run.
/// </summary>
[Collection(RecheckCrawlsCollection.NAME)]
public sealed class RecheckRunTests(RecheckCrawls crawls) : IDisposable
{
    private readonly CrawlFiles _files = new();

    public void Dispose() => _files.Dispose();

    /// <summary>
    /// A small site whose owner then removes the home page's link to a pair of pages that link only to each other, and
    /// changes a page's link to a missing URL into one to a new page. Rechecking the two pages that changed lets the pair
    /// and the missing URL go (nothing leads to them now), fetches the new page one step, and records what it links to
    /// without fetching it. With a link from a page the recheck didn't fetch still leading into the pair, the pair stays;
    /// and a page that now fails lets nothing go.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ARecheckLetsGoWhatNothingLeadsToAndFetchesNewUrlsOneStep(bool pairStillLinked)
    {
        var revision = 0;
        ConcurrentQueue<string> asked = new();
        await using var site = await TestWebHost.StartAsync((_, _) => (path, external) =>
        {
            if (!external)
            {
                asked.Enqueue(path);
            }

            return (path, Volatile.Read(ref revision)) switch
            {
                ("/", 0) => Page("Home", "/a", "/b", "/c", "/e"),
                ("/", _) => Page("Home", "/a", "/b", "/e"),
                ("/a", 0) => Page("A", "/gone"),
                ("/a", _) => Page("A", "/new"),
                ("/b", _) => pairStillLinked ? Page("B", "/d") : Page("B"),
                ("/c", _) => Page("C", "/d"),
                ("/d", _) => Page("D", "/c"),
                ("/e", 0) => Page("E", "/f"),
                ("/e", _) => new SyntheticResponse(500, "text/html; charset=utf-8", "<html><body>Server error</body></html>", null, null),
                ("/f", _) => Page("F"),
                ("/new", _) => Page("New", "/deeper"),
                ("/deeper", _) => Page("Deeper"),
                _ => new SyntheticResponse(404, "text/html; charset=utf-8", "<html><body>Not found</body></html>", null, null)
            };
        });

        var path = _files.NewPath();
        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(new CrawlOptions { StartUrl = site.BaseUri.AbsoluteUri, Concurrency = 2 }, path)).Status);
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        Volatile.Write(ref revision, 1);
        asked.Clear();

        var ids = new[] { "", "a", "e" }.Select(page => (int)TestCrawls.Count(path, $"SELECT id FROM urls WHERE url = '{site.BaseUri}{page}'")).ToList();
        var (run, recheck) = await RecheckCrawls.RecheckAsync(path, new RecheckAsk(null, UrlIds: ids));

        Assert.Equal((CrawlStatus.Completed, RecheckStatus.Completed), (run.Status, recheck.Status));
        var letGo = pairStillLinked ? new[] { "gone" } : ["c", "d", "gone"];
        Assert.Equal(letGo.Select(page => $"{site.BaseUri}{page}").Order(StringComparer.Ordinal),
            TestCrawls.Rows(path, $"SELECT url FROM urls WHERE state = {(int)UrlState.Skipped} AND skip_reason = {(int)SkipReason.NoLongerLinked}"));
        Assert.Equal(0, TestCrawls.Count(path, $"SELECT COUNT(*) FROM responses WHERE url_id IN (SELECT id FROM urls WHERE skip_reason = {(int)SkipReason.NoLongerLinked})"));
        Assert.Equal(0, TestCrawls.Count(path, $"SELECT COUNT(*) FROM links WHERE from_id IN (SELECT id FROM urls WHERE skip_reason = {(int)SkipReason.NoLongerLinked})"));

        // The new page was fetched one step; what it links to was recorded, as found by the recheck, and never asked for.
        Assert.Equal(200, TestCrawls.Count(path, $"SELECT r.status_code FROM responses r JOIN urls u ON u.id = r.url_id WHERE u.url = '{site.BaseUri}new'"));
        Assert.Equal($"{(int)UrlState.Skipped} | {(int)SkipReason.Recheck} | 0",
            Assert.Single(TestCrawls.Rows(path, $"SELECT u.state, u.skip_reason, (SELECT COUNT(*) FROM responses r WHERE r.url_id = u.id) FROM urls u WHERE u.url = '{site.BaseUri}deeper'")));
        Assert.DoesNotContain("/deeper", asked);
        Assert.DoesNotContain("/b", asked);

        // The stats leave what was let go out of the URLs found, and the grid says why each has no answer.
        var stats = CrawlStatsReader.Read(path);
        Assert.Equal(TestCrawls.Count(path, "SELECT COUNT(*) FROM urls") - letGo.Length, stats.UrlCount);
        Assert.Equal((letGo.Length, 1L), (stats.SkipReasons.GetValueOrDefault(SkipReason.NoLongerLinked), stats.SkipReasons.GetValueOrDefault(SkipReason.Recheck)));
        Assert.Equal(letGo.Length, UrlGridReader.Query(path, new UrlQuery { Filters = [GridQueries.Names("skipReason", FilterOperator.In, "noLongerLinked")], Limit = 1 }).Matching);
        Assert.Equal(1, UrlGridReader.Query(path, new UrlQuery { Filters = [GridQueries.Names("skipReason", FilterOperator.In, "recheck")], Limit = 1 }).Matching);

        // Its record: the two judged, the new page, and what it let go.
        var rows = RecheckReader.ReadRows(path, recheck.Id, null, null, 0, 100)!.Rows;
        (int, RecheckRole)[] expected = [.. ids.Select(id => (id, RecheckRole.Judged)), (Id("new"), RecheckRole.New), .. letGo.Select(page => (Id(page), RecheckRole.LetGo))];
        Assert.Equal(expected.OrderBy(row => row.Item2).ThenBy(row => row.Item1), rows.Select(row => (row.UrlId, row.Role)).OrderBy(row => row.Role).ThenBy(row => row.UrlId));
        Assert.Equal(3, recheck.Judged);

        // A page that failed when it was asked again says nothing of where it links: what it linked to stays.
        Assert.Equal(200, TestCrawls.Count(path, $"SELECT r.status_code FROM responses r JOIN urls u ON u.id = r.url_id WHERE u.url = '{site.BaseUri}f'"));

        int Id(string page) => (int)TestCrawls.Count(path, $"SELECT id FROM urls WHERE url = '{site.BaseUri}{page}'");
    }

    /// <summary>
    /// A recheck of a crawl that was stopped part-way leaves it as it was: stopped, its queue the same URLs in the same
    /// states, for its continue, which then completes it.
    /// </summary>
    [Fact]
    public async Task ARecheckLeavesAStoppedCrawlsQueueAndStatusAsTheyWere()
    {
        var path = crawls.NewPath();
        await using (var job = TestCrawls.Start(crawls.Options with { MaxRequestsPerSecond = 100 }, path))
        {
            await TestCrawls.WaitUntilAsync(path, "SELECT COUNT(*) >= 40 FROM responses");
            Assert.Equal(CrawlStatus.Stopped, (await job.StopAsync()).Status);
        }

        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        const string QUEUE = "SELECT id, url, state, skip_reason, fetch_mode, depth FROM urls WHERE state IN (0, 1)";
        var queue = TestCrawls.Rows(path, QUEUE);
        Assert.NotEmpty(queue);
        var pages = TestCrawls.Rows(path, $"SELECT u.id FROM urls u JOIN responses r ON r.url_id = u.id WHERE {CrawlSchema.PAGE} ORDER BY u.id LIMIT 3")
            .Select(int.Parse).ToList();

        var (run, recheck) = await RecheckCrawls.RecheckAsync(path, new RecheckAsk(null, UrlIds: pages));

        Assert.Equal((CrawlStatus.Completed, RecheckStatus.Completed), (run.Status, recheck.Status));
        Assert.Equal(CrawlStatus.Stopped, CrawlSummaryReader.Read(path).Status);
        Assert.Equal(queue, TestCrawls.Rows(path, QUEUE));

        // It continues as any stopped crawl does, and completes.
        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(CrawlDatabase.ReadOptions(path), path, resume: true)).Status);
        Assert.Equal(0, TestCrawls.Count(path, "SELECT COUNT(*) FROM urls WHERE state IN (0, 1)"));
    }

    /// <summary>
    /// The recheck stamps the crawl, so its analysis is out of date until the one after it, which settles the recheck. A
    /// later analysis leaves the result as it was: it records what that recheck found.
    /// </summary>
    [Fact]
    public async Task ARecheckIsSettledOnceByTheAnalysisAfterIt()
    {
        var path = crawls.Copy(crawls.Then);
        var plan = RecheckPlanReader.Plan(path, new RecheckAsk("title-too-long"), new AnalysisSettings());
        Assert.False(plan.AnalysisFirst);
        await using (var job = new CrawlEngine(NullLoggerFactory.Instance).Recheck(new RecheckRequest(path, plan, CrawlDatabase.ReadOptions(path))))
        {
            await job.Completion.WaitAsync(TestCrawls.TIMEOUT);
        }

        Assert.Equal([OutOfDateReason.CrawlChanged], CrawlSummaryReader.Read(path).OutOfDate(new AnalysisSettings()));
        var unsettled = Assert.Single(RecheckReader.ReadAll(path));
        Assert.Equal((RecheckStatus.Completed, (DateTimeOffset?)null, (RecheckResult?)null), (unsettled.Status, unsettled.SettledAt, unsettled.Result));
        Assert.True(RecheckPlanReader.Plan(path, new RecheckAsk("title-too-long"), new AnalysisSettings()).AnalysisFirst);

        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        Assert.Empty(CrawlSummaryReader.Read(path).OutOfDate(new AnalysisSettings()));
        var settled = Assert.Single(RecheckReader.ReadAll(path));
        Assert.NotNull(settled.SettledAt);
        Assert.Equal((1, 1, 1), (settled.Result!.Fixed, settled.Result.Still, settled.Result.NotChecked));

        // Analysed again with other limits, the crawl's issues change; the recheck's result doesn't.
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path, new AnalysisSettings { TitleMaxPixels = 1_000 });
        var again = Assert.Single(RecheckReader.ReadAll(path));
        Assert.Equal((settled.SettledAt, settled.Result, settled.Status), (again.SettledAt, again.Result, again.Status));
        Assert.NotEqual(IssueReader.ReadCounts(path)["title-too-long"], settled.Result.Still);
    }

    /// <summary>
    /// A recheck whose process ended part-way leaves its row running and the crawl stamped: the next analysis settles it as
    /// interrupted, with what it had fetched, the rest not rechecked.
    /// </summary>
    [Fact]
    public async Task ARecheckCutShortIsSettledAsInterrupted()
    {
        var path = crawls.Copy(crawls.Then);
        var plan = RecheckPlanReader.Plan(path, new RecheckAsk("title-too-long"), new AnalysisSettings());
        await using (var job = new CrawlEngine(NullLoggerFactory.Instance).Recheck(new RecheckRequest(path, plan, CrawlDatabase.ReadOptions(path))))
        {
            await job.Completion.WaitAsync(TestCrawls.TIMEOUT);
        }

        // As a process that died after fetching one of the three would leave it.
        Execute(path, $"""
            UPDATE rechecks SET status = {(int)RecheckStatus.Running}, ended_at = NULL;
            UPDATE responses SET fetched_at = 0 WHERE url_id IN (SELECT url_id FROM recheck_urls WHERE role = {(int)RecheckRole.Judged} ORDER BY url_id LIMIT 2);
            """);

        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);

        var recheck = Assert.Single(RecheckReader.ReadAll(path));
        Assert.Equal(RecheckStatus.Interrupted, recheck.Status);
        Assert.Equal(2, recheck.Result!.NotRechecked);
        Assert.Equal(1, recheck.Result.Fixed + recheck.Result.Still + recheck.Result.NotChecked);
    }

    /// <summary>Stopped part-way, a recheck keeps what it fetched, is settled as stopped, and what it didn't reach isn't rechecked.</summary>
    [Fact]
    public async Task AStoppedRecheckKeepsWhatItFetched()
    {
        var path = crawls.Copy(crawls.Then);
        var plan = RecheckPlanReader.Plan(path, new RecheckAsk("title-duplicate"), new AnalysisSettings());
        var options = CrawlDatabase.ReadOptions(path) with { MaxRequestsPerSecond = 2, Concurrency = 1 };
        CrawlSummary run;
        await using (var job = new CrawlEngine(NullLoggerFactory.Instance).Recheck(new RecheckRequest(path, plan, options)))
        {
            var deadline = DateTime.UtcNow + TestCrawls.TIMEOUT;
            while ((job.Progress.Recheck?.Done ?? 0) < 1 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(5);
            }

            run = await job.StopAsync();
        }

        Assert.Equal(CrawlStatus.Stopped, run.Status);
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        var recheck = RecheckReader.Read(path, run.RecheckId!.Value)!;
        Assert.Equal(RecheckStatus.Stopped, recheck.Status);
        Assert.True(recheck.Fetched >= 1 && recheck.Result!.NotRechecked >= 1 && recheck.Result.NotRechecked < recheck.Judged, $"{recheck}");
        Assert.Equal(recheck.Judged, recheck.Result.Fixed + recheck.Result.Still + recheck.Result.NotChecked + recheck.Result.NotRechecked);
    }

    /// <summary>
    /// A duplicate is settled against the other pages as the crawl stored them: the page renamed is fixed, and so is the
    /// page that kept the old title, which a page the recheck didn't fetch took meanwhile. Only a full crawl sees that.
    /// </summary>
    [Fact]
    public async Task DuplicatesAreSettledAgainstTheOtherPagesAsStored()
    {
        var path = crawls.Copy(crawls.Then);
        var (_, recheck) = await RecheckCrawls.RecheckAsync(path, new RecheckAsk("title-duplicate"));

        var outcomes = RecheckReader.ReadRows(path, recheck.Id, null, [RecheckRole.Judged], 0, 100)!.Rows.ToDictionary(row => row.Url[(crawls.Server.BaseUri.AbsoluteUri.Length - 1)..], row => row.Outcome);
        Assert.Equal((RecheckOutcome?)RecheckOutcome.Fixed, outcomes["/p/184"]);
        Assert.Equal((RecheckOutcome?)RecheckOutcome.Fixed, outcomes["/p/185"]);
        Assert.Equal((RecheckOutcome?)RecheckOutcome.Still, outcomes["/p/111"]);
        Assert.Contains($"{crawls.Server.BaseUri}p/185", crawls.Server.Site.GetExpectations().IssueUrls["title-duplicate"]);
    }

    /// <summary>
    /// Past its cap, a recheck takes the check's most important URLs, as the check's rows come, and leaves the rest as the
    /// crawl found them, not rechecked.
    /// </summary>
    [Fact]
    public async Task PastItsCapARecheckTakesTheMostImportantFirst()
    {
        var path = crawls.Copy(crawls.Then);
        var plan = RecheckPlanReader.Plan(path, new RecheckAsk("title-duplicate"), new AnalysisSettings(), limit: 3);
        var first = IssueGroupReader.ReadRows(path, "title-duplicate", new UrlQuery { Columns = ["url"], Limit = 3 })!.Page!.Rows.Select(row => row.Id);

        Assert.Equal((10, 3, true, 7), (plan.Judged, plan.Phase1, plan.Capped, plan.NotRechecked));
        Assert.Equal(first, plan.Scope.Select(url => url.Id));

        var (_, recheck) = await RecheckCrawls.RecheckAsync(path, new RecheckAsk("title-duplicate"), limit: 3);
        Assert.Equal((10, 7, 3), (recheck.Judged, recheck.Result!.NotRechecked, recheck.Fetched));
    }

    /// <summary>A recheck is paced as the crawl's options say: never faster than its rate, as polite as the crawl was.</summary>
    [Fact]
    public async Task ARecheckKeepsToTheCrawlsPace()
    {
        var path = crawls.Copy(crawls.Then);
        var time = Stopwatch.StartNew();
        var (_, recheck) = await RecheckCrawls.RecheckAsync(path, new RecheckAsk("title-duplicate"), options => options with { MaxRequestsPerSecond = 4 });

        // Ten fetches at four a second take at least two and a quarter seconds between the first and the last.
        Assert.Equal(10, recheck.Fetched);
        Assert.True(time.Elapsed >= TimeSpan.FromSeconds(2.25), $"Took {time.Elapsed}");
    }

    /// <summary>The crawl's time spent crawling leaves the recheck's run out; the run is listed, marked as one, and the recheck has its own time.</summary>
    [Fact]
    public async Task ARechecksTimeIsItsOwnAndNotTheCrawls()
    {
        var path = crawls.Copy(crawls.Then);
        var before = CrawlRunReader.Read(path)!;

        var (_, recheck) = await RecheckCrawls.RecheckAsync(path, new RecheckAsk("title-too-long"));

        var after = CrawlRunReader.Read(path)!;
        Assert.Equal(before.TimeCrawling, after.TimeCrawling);
        Assert.Equal(before.Runs!.Count + 1, after.Runs!.Count);
        Assert.True(after.Runs[^1].IsRecheck && !after.Runs[0].IsRecheck);
        Assert.True(recheck.CrawlingMs > 0);
        Assert.Equal(before.TimeCrawling, CrawlSummaryReader.Read(path).TimeCrawling);
    }

    /// <summary>
    /// When a recheck can't run, the plan says why and the engine refuses it: a check a recheck can't settle, a crawl that
    /// was interrupted or never analysed, and a crawl open elsewhere.
    /// </summary>
    [Fact]
    public async Task ARecheckIsRefusedWhenItCantRun()
    {
        var path = crawls.Copy(crawls.Then);
        var settings = new AnalysisSettings();
        Assert.Equal(RecheckRefusalCode.Unsupported, RecheckPlanReader.Plan(path, new RecheckAsk("orphan-page"), settings).Refused?.Code);
        Assert.Equal(RecheckRefusalCode.Unsupported, RecheckPlanReader.Plan(path, new RecheckAsk("no-https"), settings).Refused?.Code);
        Assert.Throws<ArgumentException>(() => RecheckPlanReader.Plan(path, new RecheckAsk("no-such-check"), settings));
        Assert.Throws<ArgumentException>(() => RecheckPlanReader.Plan(path, new RecheckAsk(null), settings));

        var refused = RecheckPlanReader.Plan(path, new RecheckAsk("orphan-page"), settings);
        Assert.Throws<InvalidOperationException>(() => new CrawlEngine(NullLoggerFactory.Instance).Recheck(new RecheckRequest(path, refused, CrawlDatabase.ReadOptions(path))));

        using (CrawlFileLock.Acquire(path))
        {
            Assert.Equal(RecheckRefusalCode.CrawlBusy, RecheckPlanReader.Plan(path, new RecheckAsk("title-too-long"), settings).Refused?.Code);
        }

        Execute(path, $"UPDATE crawl SET status = {(int)CrawlStatus.Running}");
        Assert.Equal(RecheckRefusalCode.NotAnalysable, RecheckPlanReader.Plan(path, new RecheckAsk("title-too-long"), settings).Refused?.Code);

        var neverAnalysed = crawls.Copy(crawls.Then);
        Execute(neverAnalysed, "DELETE FROM analysis");
        Assert.Equal(RecheckRefusalCode.NotAnalyzed, RecheckPlanReader.Plan(neverAnalysed, new RecheckAsk("title-too-long"), settings).Refused?.Code);
        await Task.CompletedTask;
    }

    /// <summary>A crawl file never rechecked reads as having none: the tables are only made by the first recheck.</summary>
    [Fact]
    public void ACrawlNeverRecheckedHasNoRechecks()
    {
        Assert.Empty(RecheckReader.ReadAll(crawls.Then));
        Assert.Null(RecheckReader.Read(crawls.Then, 1));
        Assert.Null(RecheckReader.ReadRows(crawls.Then, 1, null, null, 0, 10));
        Assert.DoesNotContain(CrawlRunReader.Read(crawls.Then)!.Runs!, run => run.IsRecheck);
    }

    private static SyntheticResponse Page(string title, params string[] links) => new(200, "text/html; charset=utf-8",
        $"<!DOCTYPE html><html lang=\"en\"><head><title>{title} page of the recheck test site</title></head><body><h1>{title}</h1>" +
        string.Concat(links.Select(link => $"<a href=\"{link}\">{link}</a> ")) + "</body></html>", null, null);

    private static void Execute(string path, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
