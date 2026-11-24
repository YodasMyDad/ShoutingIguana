using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Sidecar.Crawls;
using ShoutingIguana.Sidecar.Tests.Support;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Sidecar.Tests;

/// <summary>
/// What the crawl screens watch, through the API: progress by status class and code with its phase and waits, the
/// analysis's progress, the chart's history since the start, and alerts kept with the crawl, against a 60-page synthetic site.
/// </summary>
public sealed class LiveDataTests : IAsyncLifetime
{
    private static readonly string[] PHASES = ["starting", "readingRobotsTxt", "crawling", "readingSitemaps", "retrying", "finishing"];

    /// <summary>A notice the engine never raised, so never stores: as if raised a moment ago and not stored yet.</summary>
    private static readonly CrawlNotice UNSTORED = new(CrawlNoticeLevel.Warning, "not-stored-yet", "Raised, and not stored yet.")
    {
        Id = 1_000_000,
        At = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero)
    };

    private SyntheticSiteServer _site = null!;
    private TestSidecar _sidecar = null!;

    public async Task InitializeAsync()
    {
        _site = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 60 });
        _sidecar = await TestSidecar.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _sidecar.DisposeAsync();
        await _site.DisposeAsync();
    }

    [Fact]
    public async Task ACrawlsProgressAndAnalysisComeLiveAndItsAlertsAndHistoryStayWithIt()
    {
        await using var events = await EventStreamReader.ConnectAsync(_sidecar.Client);
        var started = await StartAsync(new JsonObject { ["startUrl"] = _site.BaseUri.AbsoluteUri, ["browserFallback"] = false, ["concurrency"] = 8 });
        var id = (string)started["id"]!;
        var path = (string)started["path"]!;

        var progress = await events.NextAsync("progress", data => (string?)data["crawlId"] == id && (long?)data["fetched"] >= 1);
        AssertProgressAddsUp(progress);
        Assert.NotNull(progress["at"]);
        Assert.NotNull(progress["waits"]!.AsArray());
        Assert.Empty(progress["moreWaits"]!.AsObject());

        // No URL limit: there, and null, so an ETA knows there is none without asking for the options.
        Assert.True(progress.AsObject().ContainsKey("maxUrls"));
        Assert.Null(progress["maxUrls"]);

        await events.CrawlStatusAsync(id, "completed");
        await events.CrawlStatusAsync(id, "analyzing");
        await events.CrawlStatusAsync(id, "completed");

        // Every report's numbers agree with each other, mid-crawl as at the end: each is of one moment.
        List<SseEvent> seen = [.. events.Seen()];
        var reports = seen.Where(sent => sent.Name == "progress" && (string?)sent.Data["crawlId"] == id).Select(sent => sent.Data).ToList();
        Assert.True(reports.Count >= 2, $"{reports.Count} progress events");
        Assert.All(reports, AssertProgressAddsUp);

        // The crawl's last numbers are the file's: by class, by code, and the site's own URLs. Nothing holds it up any more.
        var last = reports[^1];
        Assert.Empty(last["waits"]!.AsArray());
        Assert.Equal("completed", (string?)last["status"]);
        Assert.Equal("finishing", (string?)last["phase"]);
        Assert.Equal(Count(path, "SELECT COUNT(*) FROM responses"), (long?)last["fetched"]);
        Assert.Equal(Count(path, $"SELECT COUNT(*) FROM responses WHERE status_code = 200 AND outcome <> {(int)FetchOutcome.Blocked}"),
            (long?)last["statusCodes"]!["200"]);
        Assert.Equal(Count(path, $"SELECT COUNT(*) FROM responses WHERE outcome = {(int)FetchOutcome.Blocked}"), (long?)last["statusClasses"]!["blocked"]);
        Assert.Equal(Count(path, "SELECT COUNT(*) FROM responses r JOIN urls u ON u.id = r.url_id WHERE u.is_internal = 1 AND u.source <> 3"),
            (long?)last["internalFetched"]);

        // The URLs crawled, as the URL grid counts them, and the part of the figures that is the crawler's own probe of
        // the site's https:// version, which doesn't answer: one network error, said apart, not the site's.
        Assert.Equal(Count(path, "SELECT COUNT(*) FROM responses WHERE error IS NULL OR error NOT LIKE 'Not checked%'"), (long?)last["crawled"]);
        var grid = await _sidecar.PostAsync($"api/crawls/{id}/urls", JsonNode.Parse("""{"filters":[{"column":"fetched","op":"is","value":true}],"limit":1}"""));
        Assert.Equal((long?)last["crawled"], (long?)grid.Body!["matching"]);
        var probes = last["siteVersions"]!;
        Assert.Equal(1, (long?)probes["fetched"]);
        Assert.Equal((1L, 1L), ((long)probes["statusClasses"]!["noResponse"]!, (long)probes["noResponse"]!["networkError"]!));
        Assert.Empty(probes["statusCodes"]!.AsObject());
        Assert.Equal(Count(path, "SELECT COUNT(*) FROM responses r JOIN urls u ON u.id = r.url_id WHERE u.source = 3 AND r.status_code IS NULL"),
            (long?)probes["statusClasses"]!["noResponse"]);
        var probeFree = await _sidecar.PostAsync($"api/crawls/{id}/urls", JsonNode.Parse(
            """{"filters":[{"column":"statusClass","op":"in","values":["noResponse"]},{"column":"source","op":"notIn","values":["siteVersion"]}],"limit":1}"""));
        Assert.Equal((long)last["statusClasses"]!["noResponse"]! - (long)last["noResponse"]!["notChecked"]! - 1, (long?)probeFree.Body!["matching"]);

        // The analysis, phase by phase in order, each to its end, all between "analyzing" and the crawl's return to "completed".
        var analyzing = seen.FindLastIndex(sent => sent.Name == "crawl" && (string?)sent.Data["crawlId"] == id && (string?)sent.Data["status"] == "analyzing");
        var analysed = seen.FindLastIndex(sent => sent.Name == "crawl" && (string?)sent.Data["crawlId"] == id && (string?)sent.Data["status"] == "completed");
        var analysis = seen.Select((sent, index) => (sent, index)).Where(item => item.sent.Name == "analysis").ToList();
        Assert.NotEmpty(analysis);
        Assert.All(analysis, item => Assert.InRange(item.index, analyzing + 1, analysed - 1));
        string[] phases = ["reading", "pageRules", "siteRules", "urlSummary", "writing"];
        Assert.Equal(phases, analysis.Select(item => (string)item.sent.Data["phase"]!).Distinct());
        foreach (var phase in phases)
        {
            var ofPhase = analysis.Select(item => item.sent.Data).Where(data => (string?)data["phase"] == phase).ToList();
            Assert.Equal(0, (long?)ofPhase[0]["done"]);
            Assert.Equal((long?)ofPhase[^1]["total"], (long?)ofPhase[^1]["done"]);
        }

        Assert.Equal(IssueReader.ReadAnalysis(path)!.PagesAnalyzed, (long?)analysis.Last(item => (string?)item.sent.Data["phase"] == "pageRules").sent.Data["total"]);

        // The firewall block's alert, live and from the file under the same id: which host, and how many it blocked.
        var live = seen.First(sent => sent.Name == "alert" && (string?)sent.Data["code"] == "blocked").Data;
        var alerts = (await _sidecar.GetJsonAsync($"api/crawls/{id}/alerts")).AsArray();
        var stored = alerts.Single(alert => (string?)alert!["code"] == "blocked")!;
        Assert.Equal((long?)live["id"], (long?)stored["id"]);
        Assert.Equal((string?)live["at"], (string?)stored["at"]);
        Assert.Equal("127.0.0.1", (string?)stored["data"]!["host"]);
        Assert.False(string.IsNullOrEmpty((string?)stored["data"]!["reason"]));
        Assert.Equal(1, (long?)live["data"]!["count"]);
        Assert.Equal(Count(path, $"SELECT COUNT(*) FROM responses WHERE outcome = {(int)FetchOutcome.Blocked}"), (long?)stored["data"]!["count"]);
        Assert.Equal(alerts.Select(alert => (long?)alert!["id"]).Order(), alerts.Select(alert => (long?)alert!["id"]));

        // No longer live here: the history is counted back from the file, from a zero at the start, to where the crawl's
        // last progress ended, the site's own URLs found included: the chart's line meets the headline.
        var history = await _sidecar.GetJsonAsync($"api/crawls/{id}/history");
        Assert.Null(history["liveFrom"]);
        var points = history["points"]!.AsArray();
        Assert.Equal(0, (long?)points[0]!["fetched"]);
        Assert.Equal((string?)history["startedAt"], (string?)points[0]!["at"]);
        Assert.Equal(Count(path, "SELECT COUNT(*) FROM responses"), (long?)points[^1]!["fetched"]);
        Assert.Equal((long?)last["internalFound"], (long?)points[^1]!["internalFound"]);
        Assert.All(points, point => Assert.Null(point!["discovered"]));

        // The time spent crawling: never more than the run's time while it ran, and where its last progress put it, in the
        // library and in the history, whose one run it is, in one stretch, from the crawl's start to its end.
        Assert.All(reports, report => Assert.InRange((long)report["crawlingMs"]!, 0, (long)report["elapsedMs"]! + 100));
        var item = await _sidecar.GetJsonAsync($"api/crawls/{id}");
        Assert.True((long)last["crawlingMs"]! > 0);
        Assert.Equal((long?)last["crawlingMs"], (long?)item["crawlingMs"]);
        Assert.Equal((long?)item["crawlingMs"], (long?)history["crawlingMs"]);
        var run = Assert.Single(history["runs"]!.AsArray())!;
        Assert.Equal((long?)item["crawlingMs"], (long?)run["crawlingMs"]);
        var stretch = Assert.Single(run["crawling"]!.AsArray())!;
        Assert.Equal(((string?)run["startedAt"], (string?)run["endedAt"]), ((string?)stretch["from"], (string?)stretch["to"]));
        Assert.Equal((long)run["crawlingMs"]!, (long)(At("to", stretch) - At("from", stretch)).TotalMilliseconds);
        Assert.InRange(At("startedAt", run), At("startedAt", item), At("finishedAt", item));
        Assert.InRange(At("endedAt", run), At("startedAt", run), At("finishedAt", item).AddSeconds(1));
    }

    /// <summary>
    /// A UI that opens while the crawl runs gets its history since the start; one that opens on a crawl continued later
    /// gets the earlier run counted back from the file, then this run as the sidecar watched it.
    /// </summary>
    [Fact]
    public async Task AUiThatOpensMidCrawlGetsItsHistorySinceTheStart()
    {
        await using var events = await EventStreamReader.ConnectAsync(_sidecar.Client);
        var started = await StartAsync(new JsonObject { ["startUrl"] = _site.BaseUri.AbsoluteUri, ["browserFallback"] = false, ["maxRequestsPerSecond"] = 20, ["concurrency"] = 2 });
        var id = (string)started["id"]!;
        var path = (string)started["path"]!;
        var early = await events.NextAsync("progress", data => (string?)data["crawlId"] == id && (long?)data["fetched"] >= 5);

        // Live, in its first run: the library item's time spent crawling is the progress's, already past what the file
        // has of a stretch it stores only every few seconds.
        var running = await _sidecar.GetJsonAsync($"api/crawls/{id}");
        Assert.Equal("running", (string?)running["status"]);
        Assert.True((long)early["crawlingMs"]! > 0, early.ToJsonString());
        Assert.InRange((long?)running["crawlingMs"] ?? -1, (long)early["crawlingMs"]!, long.MaxValue);

        var history = await _sidecar.GetJsonAsync($"api/crawls/{id}/history");
        Assert.Equal((string?)history["startedAt"], (string?)history["liveFrom"]);

        // And its history says when it has been crawling: its one run, going on, in one stretch.
        var firstRuns = history["runs"]!.AsArray();
        Assert.Null(Assert.Single(firstRuns)!["endedAt"]);
        Assert.Single(firstRuns[0]!["crawling"]!.AsArray());
        Assert.NotNull(history["crawlingMs"]);
        var points = history["points"]!.AsArray();
        Assert.True(points.Count >= 2, history.ToJsonString());
        Assert.All(points, point =>
        {
            Assert.NotNull(point!["discovered"]);
            Assert.NotNull(point["queued"]);
            Assert.NotNull(point["elapsedMs"]);
            Assert.InRange((long)point["internalFound"]!, (long)point["internalFetched"]!, long.MaxValue);
        });
        AssertRises(points);

        Assert.Equal(HttpStatusCode.Accepted, (await _sidecar.PostAsync($"api/crawls/{id}/stop")).Status);
        await events.CrawlStatusAsync(id, "stopped");
        // Stopped by the user, its results so far are worked out before it can be continued (Continue is busy till then).
        await events.CrawlStatusAsync(id, "analyzing");
        await events.CrawlStatusAsync(id, "stopped");
        var firstRun = Count(path, "SELECT COUNT(*) FROM responses");

        // One of its URLs as a host that had stopped answering leaves one: stored, and never asked. The crawl is done with
        // it, and it wasn't crawled.
        Execute(path, $"""
            UPDATE responses SET outcome = {(int)FetchOutcome.NetworkError}, status_code = NULL, error = 'Not checked: http://dead.example stopped answering'
            WHERE url_id = (SELECT MAX(url_id) FROM responses)
            """);

        var continued = await _sidecar.PostAsync($"api/crawls/{id}/continue", new JsonObject { ["maxRequestsPerSecond"] = 20 });
        Assert.Equal(HttpStatusCode.OK, continued.Status);
        var resumed = await events.NextAsync("progress", data => (string?)data["crawlId"] == id && (long?)data["fetched"] >= firstRun + 3);

        // Live again: the item's URLs crawled are the progress's, which leaves the URL that wasn't checked out, where the
        // URLs it's done with count it.
        Assert.Equal(1, (long?)resumed["noResponse"]!["notChecked"]);
        var live = await _sidecar.GetJsonAsync($"api/crawls/{id}");
        Assert.Equal("running", (string?)live["status"]);
        Assert.Equal(1, (long)live["fetchedUrlCount"]! - (long)live["crawledUrlCount"]!);
        Assert.InRange((long)live["crawledUrlCount"]!, (long)resumed["crawled"]!, long.MaxValue);

        // Before this run, the file's counts: fetched, but not found or queued, which it doesn't keep the time of.
        history = await _sidecar.GetJsonAsync($"api/crawls/{id}/history");
        var liveFrom = DateTime.Parse((string)history["liveFrom"]!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        Assert.True(liveFrom > DateTime.Parse((string)history["startedAt"]!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
        points = history["points"]!.AsArray();
        var earlier = points.Where(point => At(point!) < liveFrom).ToList();
        var later = points.Where(point => At(point!) >= liveFrom).ToList();
        Assert.True(earlier.Count >= 2 && later.Count >= 1, history.ToJsonString());
        Assert.All(earlier, point => Assert.Null(point!["discovered"]));
        Assert.All(later, point => Assert.NotNull(point!["discovered"]));
        Assert.All(points, point => Assert.InRange((long)point!["internalFound"]!, (long)point["internalFetched"]!, long.MaxValue));
        Assert.InRange((long)earlier[^1]!["fetched"]!, 1, firstRun);
        AssertRises(points);

        // Two runs, the second going on: its end isn't known yet, and the time between the two is in neither. The time
        // spent crawling is theirs added up, as the progress event has it, less than the clock time since the start.
        var runs = history["runs"]!.AsArray();
        Assert.Equal(2, runs.Count);
        Assert.NotNull(runs[0]!["endedAt"]);
        Assert.Null(runs[1]!["endedAt"]);
        Assert.True(At("startedAt", runs[1]!) > At("endedAt", runs[0]!));
        Assert.Equal((long?)history["crawlingMs"], runs.Sum(run => (long)run!["crawlingMs"]!));
        var progress = await events.NextAsync("progress", data => (string?)data["crawlId"] == id && (long?)data["crawlingMs"] >= (long)history["crawlingMs"]!);
        Assert.True((long)progress["crawlingMs"]! >= (long)runs[0]!["crawlingMs"]! + (long)progress["elapsedMs"]! - 100, progress.ToJsonString());

        Assert.Equal(HttpStatusCode.Accepted, (await _sidecar.PostAsync($"api/crawls/{id}/stop")).Status);
        await events.CrawlStatusAsync(id, "stopped");

        // Stopped: both runs have ended, and the library's time is what the history's runs add up to.
        history = await _sidecar.GetJsonAsync($"api/crawls/{id}/history");
        runs = history["runs"]!.AsArray();
        Assert.All(runs, run => Assert.NotNull(run!["endedAt"]));
        Assert.Equal((long?)(await _sidecar.GetJsonAsync($"api/crawls/{id}"))["crawlingMs"], runs.Sum(run => (long)run!["crawlingMs"]!));
        Assert.True((long)history["crawlingMs"]! < (At("endedAt", runs[1]!) - At("startedAt", runs[0]!)).TotalMilliseconds);
    }

    /// <summary>A crawl made by another program (the CLI) has its alerts in its file; one from before they were kept has none.</summary>
    [Fact]
    public async Task AlertsAreReadFromTheFileForACrawlThisSidecarDidntRun()
    {
        var path = await TestCrawlFiles.CrawlAsync(_site, Path.Combine(_sidecar.DataDirectory, "by-the-cli.sicrawl"),
            options: new Engine.Configuration.Models.CrawlOptions { StartUrl = _site.BaseUri.AbsoluteUri, BrowserFallback = false });
        var id = Library.CrawlIds.For(path);

        var alerts = (await _sidecar.GetJsonAsync($"api/crawls/{id}/alerts")).AsArray();
        var blocked = alerts.Single(alert => (string?)alert!["code"] == "blocked")!;
        Assert.Equal(id, (string?)blocked["crawlId"]);
        Assert.Equal("warning", (string?)blocked["level"]);
        Assert.Equal(Count(path, $"SELECT COUNT(*) FROM responses WHERE outcome = {(int)FetchOutcome.Blocked}"), (long?)blocked["data"]!["count"]);
        Assert.Contains(alerts, alert => (string?)alert!["code"] == "sitemaps" && (long?)alert["data"]!["urlCount"] > 0);
        Assert.Equal(CrawlNoticeReader.Read(path).Select(notice => notice.Id), alerts.Select(alert => (long)alert!["id"]!));

        Execute(path, "DROP TABLE notices");
        Assert.Empty((await _sidecar.GetJsonAsync($"api/crawls/{id}/alerts")).AsArray());
    }

    /// <summary>
    /// The engine stores each alert a second or so after raising it; GET /alerts for a crawl live here has the ones it
    /// hasn't stored yet too. One the file never gets stands for those here, so the test needn't race the writer.
    /// </summary>
    [Fact]
    public async Task AnAlertNotStoredYetIsReadWithTheStoredOnes()
    {
        var alerts = await AlertsWhileAnUnstoredOneIsLiveAsync(UNSTORED, forgetAfterReadingTheFile: false);

        Assert.Contains(alerts, alert => (string?)alert!["code"] == UNSTORED.Code && (long?)alert["id"] == UNSTORED.Id);
    }

    /// <summary>
    /// The crawl closes while GET /alerts reads its file: it stores its last alerts, and they're forgotten as stored, in
    /// between. None is missed, as the unstored ones are taken before the file is read. Forced here by forgetting them
    /// as soon as the file has been read.
    /// </summary>
    [Fact]
    public async Task AnAlertStoredAsTheCrawlClosesIsNeverMissed()
    {
        var alerts = await AlertsWhileAnUnstoredOneIsLiveAsync(UNSTORED, forgetAfterReadingTheFile: true);

        Assert.Contains(alerts, alert => (string?)alert!["code"] == UNSTORED.Code && (long?)alert["id"] == UNSTORED.Id);
    }

    /// <summary>
    /// An alert is its id and when it was raised: a crash can lose the last alerts before they're stored, and the next run
    /// gives their ids to new ones. One heard live keeps its place beside a stored one with its id and another time; here
    /// the crawl's first stored alert, id 1.
    /// </summary>
    [Fact]
    public async Task AnAlertIsItsIdAndItsTime()
    {
        var heard = UNSTORED with { Id = 1 };

        var alerts = await AlertsWhileAnUnstoredOneIsLiveAsync(heard, forgetAfterReadingTheFile: false,
            once: path => CrawlNoticeReader.Read(path).Any(notice => notice.Id == 1));

        Assert.Single(alerts, alert => (long?)alert!["id"] == 1 && (string?)alert["code"] == heard.Code);
        Assert.Single(alerts, alert => (long?)alert!["id"] == 1 && (string?)alert["code"] != heard.Code);
    }

    /// <summary>
    /// A crawl that isn't live here has its history counted back from its file once, and kept until the file changes. Shown
    /// by changing the file under it while keeping its size and modified time, which the kept history doesn't see; then
    /// changing its modified time too, which it does.
    /// </summary>
    [Fact]
    public async Task TheHistoryOfACrawlNotLiveHereIsKeptUntilItsFileChanges()
    {
        var path = await TestCrawlFiles.CrawlAsync(_site, Path.Combine(_sidecar.DataDirectory, "history.sicrawl"),
            options: new Engine.Configuration.Models.CrawlOptions { StartUrl = _site.BaseUri.AbsoluteUri, BrowserFallback = false });
        var id = Library.CrawlIds.For(path);
        var first = (await _sidecar.GetJsonAsync($"api/crawls/{id}/history")).ToJsonString();

        // Every fetch a minute later, in place: the same size, and the modified time put back.
        var modified = File.GetLastWriteTimeUtc(path);
        var size = new FileInfo(path).Length;
        Execute(path, "UPDATE responses SET fetched_at = fetched_at + 60000");
        File.SetLastWriteTimeUtc(path, modified);
        Assert.Equal(size, new FileInfo(path).Length);
        Assert.False(File.Exists(path + "-wal"));
        Assert.Equal(first, (await _sidecar.GetJsonAsync($"api/crawls/{id}/history")).ToJsonString());

        File.SetLastWriteTimeUtc(path, modified + TimeSpan.FromSeconds(2));
        var changed = await _sidecar.GetJsonAsync($"api/crawls/{id}/history");
        Assert.NotEqual(first, changed.ToJsonString());
        Assert.Equal(Count(path, "SELECT MAX(fetched_at) FROM responses"),
            new DateTimeOffset(At(changed["points"]!.AsArray()[^1]!)).ToUnixTimeMilliseconds());
    }

    /// <summary>
    /// GET /alerts while a crawl runs here, slowly, after <paramref name="notice"/> was raised through it, unstored: the
    /// alert event is awaited first, and then, if given, <paramref name="once"/> to hold of the crawl's file. With
    /// <paramref name="forgetAfterReadingTheFile"/>, the crawl's unstored alerts are forgotten as soon as the file is read,
    /// as they are when the crawl closes.
    /// </summary>
    private async Task<JsonArray> AlertsWhileAnUnstoredOneIsLiveAsync(CrawlNotice notice, bool forgetAfterReadingTheFile, Func<string, bool>? once = null)
    {
        var manager = _sidecar.Host.Services.GetRequiredService<CrawlManager>();
        var log = _sidecar.Host.Services.GetRequiredService<AlertLog>();
        manager.StartJob = (request, observer) =>
        {
            var job = new CrawlEngine(NullLoggerFactory.Instance).Start(request, observer);
            observer.OnNotice(notice);
            return job;
        };
        manager.AlertsFileRead = forgetAfterReadingTheFile ? log.Forget : null;

        try
        {
            await using var events = await EventStreamReader.ConnectAsync(_sidecar.Client);
            var started = await StartAsync(new JsonObject { ["startUrl"] = _site.BaseUri.AbsoluteUri, ["browserFallback"] = false, ["maxRequestsPerSecond"] = 5, ["concurrency"] = 1 });
            var id = (string)started["id"]!;
            var path = (string)started["path"]!;
            await events.NextAsync("alert", data => (string?)data["crawlId"] == id && (string?)data["code"] == notice.Code);
            var deadline = DateTime.UtcNow + EventStreamReader.TIMEOUT;
            while (once is not null && !once(path) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            Assert.True(once?.Invoke(path) ?? true, "The crawl's file never came to what the test waits for.");
            var alerts = (await _sidecar.GetJsonAsync($"api/crawls/{id}/alerts")).AsArray();
            Assert.Equal("running", (string?)(await _sidecar.GetJsonAsync($"api/crawls/{id}"))["status"]);
            Assert.Equal(HttpStatusCode.Accepted, (await _sidecar.PostAsync($"api/crawls/{id}/stop")).Status);
            await events.CrawlStatusAsync(id, "stopped");
            return alerts;
        }
        finally
        {
            manager.StartJob = null;
            manager.AlertsFileRead = null;
        }
    }

    /// <summary>Every URL fetched is in one class, and each class's codes add up to it.</summary>
    private static void AssertProgressAddsUp(JsonNode progress)
    {
        Assert.Contains((string?)progress["phase"], PHASES);
        var classes = progress["statusClasses"]!.AsObject();
        Assert.Equal(["2xx", "3xx", "4xx", "5xx", "other", "noResponse", "blocked"], classes.Select(entry => entry.Key));
        Assert.Equal((long?)progress["fetched"], classes.Sum(entry => (long)entry.Value!));
        var codes = progress["statusCodes"]!.AsObject().Select(entry => (Code: int.Parse(entry.Key, CultureInfo.InvariantCulture), Count: (long)entry.Value!)).ToList();
        Assert.Equal((long?)classes["2xx"], codes.Where(code => code.Code is >= 200 and < 300).Sum(code => code.Count));
        Assert.Equal((long?)classes["4xx"], codes.Where(code => code.Code is >= 400 and < 500).Sum(code => code.Count));
        var noResponse = progress["noResponse"]!.AsObject();
        Assert.Equal((long?)classes["noResponse"], noResponse.Sum(entry => (long)entry.Value!));
        Assert.Equal((long?)progress["robotsDisallowed"], (long?)noResponse["robotsDisallowed"]);
        Assert.InRange((long)progress["internalFetched"]!, 0, (long)progress["internalFound"]!);

        // The URLs crawled are those fetched but for the ones never asked; the probes' part is within each figure, and adds up too.
        Assert.Equal((long)progress["fetched"]! - (long)noResponse["notChecked"]!, (long?)progress["crawled"]);
        var probes = progress["siteVersions"]!;
        var probeClasses = probes["statusClasses"]!.AsObject();
        Assert.Equal(classes.Select(entry => entry.Key), probeClasses.Select(entry => entry.Key));
        Assert.Equal((long?)probes["fetched"], probeClasses.Sum(entry => (long)entry.Value!));
        Assert.All(probeClasses, entry => Assert.InRange((long)entry.Value!, 0, (long)classes[entry.Key]!));
        Assert.Equal(noResponse.Select(entry => entry.Key), probes["noResponse"]!.AsObject().Select(entry => entry.Key));
        Assert.Equal((long?)probeClasses["noResponse"], probes["noResponse"]!.AsObject().Sum(entry => (long)entry.Value!));
        Assert.All(probes["statusCodes"]!.AsObject(), entry => Assert.InRange((long)entry.Value!, 1, (long)progress["statusCodes"]![entry.Key]!));
    }

    private static void AssertRises(JsonArray points) =>
        Assert.All(points.Zip(points.Skip(1)), pair =>
        {
            Assert.True(At(pair.Second!) >= At(pair.First!));
            Assert.True((long)pair.Second!["fetched"]! >= (long)pair.First!["fetched"]!);
        });

    private static DateTime At(JsonNode point) => At("at", point);

    private static DateTime At(string name, JsonNode from) => DateTime.Parse((string)from[name]!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private async Task<JsonNode> StartAsync(JsonObject options)
    {
        var started = await _sidecar.PostAsync("api/crawls", new JsonObject { ["options"] = options });
        Assert.True(started.Status == HttpStatusCode.Created, $"{(int)started.Status} {started.Body?.ToJsonString()}");
        return started.Body!;
    }

    private static long Count(string path, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void Execute(string path, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
