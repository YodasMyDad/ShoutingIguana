using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>
/// The time a crawl spends crawling, kept with the crawl a stretch at a time: paused time and the time between a stop and
/// a continue are in none of it, live and in the file alike, to the millisecond. A run going on is stored every few
/// seconds, so one cut short loses only those. Where a run wasn't timed (a file from before the time was kept, an engine
/// that doesn't keep it), the crawl's time isn't known, and is never a part given for the whole.
/// </summary>
public sealed class CrawlTimeTests : IDisposable
{
    private const string HTML = "text/html; charset=utf-8";

    private static readonly DateTimeOffset START = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly CrawlFiles _files = new();

    public void Dispose() => _files.Dispose();

    /// <summary>
    /// Three runs on a clock the test moves, a page held back so each lasts as long as the test says: the first paused
    /// for a minute part-way, the second stored while it goes on, the third to the crawl's end.
    /// </summary>
    [Fact]
    public async Task TheTimeSpentCrawlingLeavesOutPausesAndTheTimeBetweenRuns()
    {
        await using var site = await StartSiteAsync();
        site.Hold("/held");
        var time = new FakeTimeProvider(START);
        var path = _files.NewPath();
        var options = new CrawlOptions { StartUrl = site.BaseUri.AbsoluteUri, Concurrency = 2, DiscoverSitemaps = false };
        var engine = new CrawlEngine(NullLoggerFactory.Instance, time);

        // The first run: ten seconds, a minute paused, five more, then stopped while paused, so no moment of stopping counts.
        CrawlSummary first;
        await using (var job = engine.Start(new CrawlRequest(options, path)))
        {
            await TestCrawls.WaitUntilAsync(path, "SELECT COUNT(*) FROM run_time");
            time.Advance(TimeSpan.FromSeconds(10));
            Assert.Equal(TimeSpan.FromSeconds(10), job.Progress.TimeCrawling);
            job.Pause();
            time.Advance(TimeSpan.FromMinutes(1));
            Assert.Equal(TimeSpan.FromSeconds(10), job.Progress.TimeCrawling);
            job.Resume();
            time.Advance(TimeSpan.FromSeconds(5));
            job.Pause();
            first = await FakeClock.PumpAsync(time, job.StopAsync());
        }

        Assert.Equal((CrawlStatus.Stopped, TimeSpan.FromSeconds(15)), (first.Status, first.Progress.TimeCrawling));
        var runs = CrawlRunReader.Read(path)!;
        Assert.Equal(TimeSpan.FromSeconds(15), runs.TimeCrawling);
        var run = Assert.Single(runs.Runs!);
        Assert.Equal((START, START.AddSeconds(75), TimeSpan.FromSeconds(15)), (run.StartedAt, run.EndedAt, run.TimeCrawling));
        Assert.Equal([new CrawlingStretch(START, START.AddSeconds(10)), new CrawlingStretch(START.AddSeconds(70), START.AddSeconds(75))], run.Crawling);
        Assert.Equal(TimeSpan.FromSeconds(15), CrawlSummaryReader.Read(path).TimeCrawling);
        Assert.Equal(runs, CrawlHistoryReader.Read(path).Runs, RunsEqual);

        // Continued an hour later: the hour is no part of it. While the run goes on, its time is stored every few seconds,
        // so a reader (and a run cut short) has it to within those.
        time.Advance(TimeSpan.FromHours(1));
        CrawlSummary second;
        TimeSpan stored;
        await using (var job = engine.Start(new CrawlRequest(options, path, Resume: true)))
        {
            await TestCrawls.WaitUntilAsync(path, "SELECT COUNT(*) FROM run_time WHERE run = 2");
            Assert.Equal(TimeSpan.FromSeconds(15), job.Progress.TimeCrawling);
            await FakeClock.PumpUntilAsync(time, () => TestCrawls.Count(path, "SELECT crawled_ms FROM run_time WHERE run = 2") >= 5_000, TimeSpan.FromMilliseconds(500));
            var going = CrawlRunReader.Read(path)!;
            Assert.Equal(2, going.Runs!.Count);
            Assert.Null(going.Runs[1].EndedAt);
            Assert.InRange(going.Runs[1].TimeCrawling, TimeSpan.FromSeconds(5), job.Progress.TimeCrawling!.Value - TimeSpan.FromSeconds(15));
            Assert.Equal(going.Runs[1].Crawling[0].To - going.Runs[1].Crawling[0].From, going.Runs[1].TimeCrawling);

            job.Pause();
            stored = job.Progress.TimeCrawling!.Value - TimeSpan.FromSeconds(15);
            time.Advance(TimeSpan.FromSeconds(30));
            job.Resume();
            time.Advance(TimeSpan.FromSeconds(20));
            job.Pause();
            second = await FakeClock.PumpAsync(time, job.StopAsync());
        }

        Assert.Equal(TimeSpan.FromSeconds(35) + stored, second.Progress.TimeCrawling);
        runs = CrawlRunReader.Read(path)!;
        Assert.Equal(second.Progress.TimeCrawling, runs.TimeCrawling);
        Assert.Equal([TimeSpan.FromSeconds(15), stored + TimeSpan.FromSeconds(20)], runs.Runs!.Select(each => each.TimeCrawling));
        Assert.Equal([stored, TimeSpan.FromSeconds(20)], runs.Runs![1].Crawling.Select(stretch => stretch.To - stretch.From));
        Assert.True(runs.Runs[1].StartedAt >= runs.Runs[0].EndedAt + TimeSpan.FromHours(1));
        Assert.Equal(runs.Runs[1].Crawling[^1].To, runs.Runs[1].EndedAt);

        // The last run, to the crawl's end: the crawl's last progress and its file say the same time, to the millisecond.
        site.Release("/held");
        CrawlSummary last;
        await using (var job = engine.Start(new CrawlRequest(options, path, Resume: true)))
        {
            last = await FakeClock.PumpAsync(time, job.Completion);
        }

        Assert.Equal(CrawlStatus.Completed, last.Status);
        runs = CrawlRunReader.Read(path)!;
        Assert.Equal(3, runs.Runs!.Count);
        Assert.Equal(last.Progress.TimeCrawling, runs.TimeCrawling);
        Assert.Equal(runs.TimeCrawling, CrawlSummaryReader.Read(path).TimeCrawling);
        Assert.Equal(runs.TimeCrawling, runs.Runs.Aggregate(TimeSpan.Zero, (sum, each) => sum + each.TimeCrawling));
        Assert.True(runs.TimeCrawling > second.Progress.TimeCrawling && runs.Runs[2].EndedAt is not null);
        Assert.All(runs.Runs, each => Assert.Equal(each.TimeCrawling, each.Crawling.Aggregate(TimeSpan.Zero, (sum, stretch) => sum + (stretch.To - stretch.From))));

        // Every stretch has ended, each run's rows share its number, and the last carries the crawl row's stamp.
        Assert.Equal(["1 | 1", "1 | 1", "2 | 1", "2 | 1", "3 | 1"], TestCrawls.Rows(path, "SELECT run, ended, id FROM run_time").Select(row => row[..5]));
        Assert.Equal(1, TestCrawls.Count(path, "SELECT (SELECT crawl_updated_at FROM run_time ORDER BY id DESC LIMIT 1) = (SELECT updated_at FROM crawl)"));
    }

    /// <summary>
    /// A crawl from before the time was kept has none, and none once it's continued: the time its earlier runs took isn't
    /// known, so no part is given for the whole. Its file then says so, in a row without a time, before the run's own.
    /// </summary>
    [Fact]
    public async Task ACrawlFromBeforeTheTimeWasKeptHasNoTimeEvenOnceContinued()
    {
        await using var site = await StartSiteAsync();
        var path = _files.NewPath();
        var options = new CrawlOptions { StartUrl = site.BaseUri.AbsoluteUri, DiscoverSitemaps = false };
        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(options, path)).Status);
        Assert.NotNull(CrawlSummaryReader.Read(path).TimeCrawling);
        GridQueries.Change(path, "DROP TABLE run_time");

        Assert.Null(CrawlRunReader.Read(path));
        Assert.Null(CrawlSummaryReader.Read(path).TimeCrawling);
        Assert.Null(CrawlHistoryReader.Read(path).Runs);

        var continued = await TestCrawls.RunAsync(options, path, resume: true);
        Assert.Equal(CrawlStatus.Completed, continued.Status);
        Assert.Null(continued.Progress.TimeCrawling);
        Assert.Null(CrawlRunReader.Read(path));
        Assert.Null(CrawlSummaryReader.Read(path).TimeCrawling);
        Assert.Equal(["1 | 1 | 1", "2 | 0 | 1"], TestCrawls.Rows(path, "SELECT run, crawled_ms IS NULL, ended FROM run_time"));
        Assert.Equal(1, TestCrawls.Count(path, "SELECT (SELECT started_at FROM run_time WHERE run = 1) = (SELECT started_at FROM crawl)"));

        // And for good: however many runs are timed after it.
        Assert.Null((await TestCrawls.RunAsync(options, path, resume: true)).Progress.TimeCrawling);
        Assert.Null(CrawlRunReader.Read(path));
        Assert.Equal(["1 | 1 | 1", "2 | 0 | 1", "3 | 0 | 1"], TestCrawls.Rows(path, "SELECT run, crawled_ms IS NULL, ended FROM run_time"));
    }

    /// <summary>
    /// An engine that doesn't keep the time continues a crawl this one made: it stamps the crawl row, as every run does,
    /// and stores no stretch. The stamp gives it away: the time isn't known from then on, and the next run here says so.
    /// </summary>
    [Fact]
    public async Task ARunByAnEngineThatKeepsNoTimeIsToldByTheCrawlsStamp()
    {
        await using var site = await StartSiteAsync();
        var path = _files.NewPath();
        var options = new CrawlOptions { StartUrl = site.BaseUri.AbsoluteUri, DiscoverSitemaps = false };
        await TestCrawls.RunAsync(options, path);
        Assert.NotNull(CrawlRunReader.Read(path));

        GridQueries.Stamp(path);
        Assert.Null(CrawlRunReader.Read(path));
        Assert.Null(CrawlSummaryReader.Read(path).TimeCrawling);

        var continued = await TestCrawls.RunAsync(options, path, resume: true);
        Assert.Null(continued.Progress.TimeCrawling);
        Assert.Null(CrawlRunReader.Read(path));
        Assert.Equal(["1 | 0 | 1", "2 | 1 | 1", "3 | 0 | 1"], TestCrawls.Rows(path, "SELECT run, crawled_ms IS NULL, ended FROM run_time"));
    }

    /// <summary>
    /// A run cut short leaves its last stretch going, as long as it was last stored: read so, with the run not ended, and
    /// ended there once the crawl is continued, whose time is then the whole of it.
    /// </summary>
    [Fact]
    public async Task ARunCutShortCountsUpToWhereItWasLastStored()
    {
        await using var site = await StartSiteAsync();
        var path = _files.NewPath();
        var options = new CrawlOptions { StartUrl = site.BaseUri.AbsoluteUri, DiscoverSitemaps = false };
        await TestCrawls.RunAsync(options, path);

        // As a process that died leaves the file: running, its stretch going, 1,234 ms stored of it.
        GridQueries.Change(path, "UPDATE run_time SET ended = 0, crawled_ms = 1234; UPDATE crawl SET status = 0, finished_at = NULL");
        var cut = CrawlRunReader.Read(path)!;
        Assert.Equal((TimeSpan.FromMilliseconds(1_234), (DateTimeOffset?)null), (cut.TimeCrawling, cut.Runs![0].EndedAt));
        Assert.Equal(cut.Runs[0].StartedAt.AddMilliseconds(1_234), cut.Runs[0].Crawling[0].To);

        var continued = await TestCrawls.RunAsync(options, path, resume: true);
        var runs = CrawlRunReader.Read(path)!;
        Assert.Equal(2, runs.Runs!.Count);
        Assert.Equal((TimeSpan.FromMilliseconds(1_234), cut.Runs[0].Crawling[0].To), (runs.Runs[0].TimeCrawling, runs.Runs[0].EndedAt));
        Assert.Equal(continued.Progress.TimeCrawling, runs.TimeCrawling);
        Assert.Equal(TimeSpan.FromMilliseconds(1_234) + runs.Runs[1].TimeCrawling, runs.TimeCrawling);
    }

    /// <summary>
    /// Files are shared: times that can't be true, or a table another program made its own way, read as not known, and
    /// don't stop the crawl being continued, which then keeps no time. A crawl paused a thousand times has its time, and
    /// no list.
    /// </summary>
    [Fact]
    public async Task WhatTheFileSaysOfTheTimeIsBelievedOnlyWhenItCanBeTrue()
    {
        await using var site = await StartSiteAsync();
        var path = _files.NewPath();
        var options = new CrawlOptions { StartUrl = site.BaseUri.AbsoluteUri, DiscoverSitemaps = false };
        await TestCrawls.RunAsync(options, path);
        Assert.NotNull(CrawlRunReader.Read(path));

        foreach (var change in new[]
                 {
                     "UPDATE run_time SET crawled_ms = -1", "UPDATE run_time SET crawled_ms = 400000000000", "UPDATE run_time SET crawled_ms = 'long'",
                     "UPDATE run_time SET started_at = 9223372036854775807", "UPDATE run_time SET crawled_ms = NULL", "DELETE FROM run_time"
                 })
        {
            var copy = Copy(path);
            GridQueries.Change(copy, change);
            Assert.Null(CrawlRunReader.Read(copy));
            Assert.Null(CrawlSummaryReader.Read(copy).TimeCrawling);
        }

        var paused = Copy(path);
        GridQueries.Change(paused, $"""
            WITH RECURSIVE more(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM more WHERE n < {CrawlRunReader.MAX_STRETCHES})
            INSERT INTO run_time (run, started_at, crawled_ms, ended, crawl_updated_at)
            SELECT 1, (SELECT started_at FROM crawl) + n * 10, 2, 1, (SELECT updated_at FROM crawl) FROM more
            """);
        var known = CrawlRunReader.Read(path)!.TimeCrawling;
        Assert.Equal(known + TimeSpan.FromMilliseconds(2 * CrawlRunReader.MAX_STRETCHES), CrawlRunReader.Read(paused)!.TimeCrawling);
        Assert.Null(CrawlRunReader.Read(paused)!.Runs);

        var theirs = Copy(path);
        GridQueries.Change(theirs, "DROP TABLE run_time; CREATE TABLE run_time (id INTEGER PRIMARY KEY, note TEXT NOT NULL)");
        Assert.Null(CrawlRunReader.Read(theirs));
        var continued = await TestCrawls.RunAsync(options, theirs, resume: true);
        Assert.Equal((CrawlStatus.Completed, (TimeSpan?)null), (continued.Status, continued.Progress.TimeCrawling));
        Assert.Equal(0, TestCrawls.Count(theirs, "SELECT COUNT(*) FROM run_time"));
    }

    private string Copy(string path)
    {
        var copy = _files.NewPath();
        File.Copy(path, copy);
        return copy;
    }

    private static bool RunsEqual(CrawlRuns? expected, CrawlRuns? actual) =>
        expected is not null && actual is not null && expected.TimeCrawling == actual.TimeCrawling &&
        expected.Runs!.Select(run => (run.StartedAt, run.EndedAt, run.TimeCrawling, run.Crawling.Count))
            .SequenceEqual(actual.Runs!.Select(run => (run.StartedAt, run.EndedAt, run.TimeCrawling, run.Crawling.Count)));

    /// <summary>A home page linking two pages, one of which a test can hold back.</summary>
    private static Task<TestWebHost> StartSiteAsync() => TestWebHost.StartAsync((_, _) => (path, _) => path switch
    {
        "/" => Page("Home", """<a href="/a">A</a> <a href="/held">Held</a>"""),
        "/a" or "/held" => Page(path[1..], "<p>A page.</p>"),
        _ => new SyntheticResponse(404, HTML, "<p>Not found</p>", null, null)
    });

    private static SyntheticResponse Page(string title, string body) => new(200, HTML,
        $"""<!DOCTYPE html><html lang="en"><head><title>{title}</title></head><body><h1>{title}</h1>{body}</body></html>""", null, null);
}
