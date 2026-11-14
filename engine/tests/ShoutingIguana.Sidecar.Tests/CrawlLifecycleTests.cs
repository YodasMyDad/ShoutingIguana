using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Sidecar.Crawls;
using ShoutingIguana.Sidecar.Exports;
using ShoutingIguana.Sidecar.Events.Models;
using ShoutingIguana.Sidecar.Hosting;
using ShoutingIguana.Sidecar.Tests.Support;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Sidecar.Tests;

/// <summary>Crawls started, watched, paused, stopped, continued and analysed through the API, against a 60-page synthetic site.</summary>
public sealed class CrawlLifecycleTests : IAsyncLifetime
{
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
    public async Task ACrawlRunsToItsAnalysisOverTheEventStream()
    {
        await using var events = await EventStreamReader.ConnectAsync(_sidecar.Client);
        Assert.Empty((await events.NextAsync("hello"))["live"]!.AsArray());

        var started = await StartAsync(new JsonObject { ["startUrl"] = _site.BaseUri.AbsoluteUri, ["concurrency"] = 8 });
        var id = (string)started["id"]!;
        var path = (string)started["path"]!;
        Assert.Equal("running", (string?)started["status"]);
        Assert.Equal(_sidecar.DataDirectory, Path.GetDirectoryName(path));
        Assert.Matches(@"^127\.0\.0\.1-\d{8}-\d{6}(-\d+)?\.sicrawl$", (string?)started["fileName"]);

        await events.CrawlStatusAsync(id, "running");
        var progress = await events.NextAsync("progress", data => (string?)data["crawlId"] == id);
        Assert.NotNull(progress["elapsedMs"]);
        Assert.NotNull(progress["urlsPerSecond"]);
        Assert.Null(progress["pagesPerSecond"]);
        await events.CrawlStatusAsync(id, "completed");
        await events.CrawlStatusAsync(id, "analyzing");
        var analysed = await events.CrawlStatusAsync(id, "completed");
        Assert.Null(analysed["error"]);
        var seen = events.Seen();
        Assert.Equal("hello", seen[0].Name);

        // The site plants a firewall block: an alert, with a stable code, live and afterwards.
        var blocked = seen.First(sent => sent.Name == "alert" && (string?)sent.Data["code"] == "blocked").Data;
        Assert.Equal(id, (string?)blocked["crawlId"]);
        Assert.Equal("warning", (string?)blocked["level"]);
        var alerts = (await _sidecar.GetJsonAsync($"api/crawls/{id}/alerts")).AsArray();
        Assert.Contains(alerts, alert => (string?)alert!["code"] == "blocked");

        // Every check either ran, with a count, or is listed as skipped with why: never both, never neither.
        var analysis = await _sidecar.GetJsonAsync($"api/crawls/{id}/analysis");
        var counts = IssueReader.ReadCounts(path);
        Assert.Equal(IssueCatalog.All.Where(check => counts.ContainsKey(check.Id)).Select(check => check.Id),
            analysis["counts"]!.AsArray().Select(count => (string?)count!["checkId"]));
        Assert.All(analysis["counts"]!.AsArray(), count => Assert.Equal(counts[(string)count!["checkId"]!], (long?)count["count"]));
        var skipped = IssueReader.ReadSkippedChecks(path);
        Assert.Equal(IssueCatalog.All.Where(check => !counts.ContainsKey(check.Id)).Select(check => check.Id), skipped.Select(skip => skip.CheckId));
        Assert.Equal(skipped.Select(skip => (skip.CheckId, skip.Reason, skip.Text)), analysis["skipped"]!.AsArray()
            .Select(skip => ((string)skip!["checkId"]!, (string)skip["reason"]!["code"]!, (string)skip["reason"]!["text"]!)));
        Assert.Equal(skipped.Count > 0, (bool?)analysis["isPartial"]);
        Assert.False((bool?)analysis["outOfDate"]);
        Assert.Empty(analysis["outOfDateReasons"]!.AsArray());
        Assert.Equal(IssueReader.ReadAnalysis(path)!.PagesAnalyzed, (long?)analysis["pagesAnalyzed"]);
        Assert.NotNull(analysis["notes"]);

        var item = await _sidecar.GetJsonAsync($"api/crawls/{id}");
        var stats = CrawlStatsReader.Read(path);
        Assert.Equal("completed", (string?)item["status"]);
        Assert.Equal(stats.Outcomes.Values.Sum(), (long?)item["fetchedUrlCount"]);
        Assert.Equal(stats.Outcomes.Values.Sum() - stats.NotCheckedCount, (long?)item["crawledUrlCount"]);
        Assert.Equal(stats.HtmlPageCount, (long?)item["pageCount"]);
        Assert.NotNull(item["issues"]);
        Assert.False((bool?)item["outOfDate"]);

        var crawlStats = await _sidecar.GetJsonAsync($"api/crawls/{id}/stats");
        Assert.Equal("completed", (string?)crawlStats["status"]);
        Assert.Equal(stats.LinkCount, (long?)crawlStats["linkCount"]);
        Assert.Equal(stats.Outcomes[Engine.Fetching.Models.FetchOutcome.Success], (long?)crawlStats["outcomes"]!["success"]);
        Assert.Equal(stats.StatusCodes[200], (long?)crawlStats["statusCodes"]!["200"]);

        // The crawler's probe of the site's https:// version, which doesn't answer, is among the outcomes, and said apart.
        Assert.Equal("""{"fetched":1,"outcomes":{"networkError":1},"statusCodes":{}}""", crawlStats["siteVersions"]!.ToJsonString());
        Assert.True((long)crawlStats["outcomes"]!["networkError"]! >= 1);

        // One check's URLs, a page at a time, in crawl order.
        var busiest = counts.MaxBy(count => count.Value).Key;
        var all = IssueReader.ReadIssues(path, busiest, 100);
        Assert.True(all.Count >= 2, busiest);
        var second = await _sidecar.GetJsonAsync($"api/crawls/{id}/issues/{busiest}?limit=1&offset=1");
        Assert.Equal(counts[busiest], (long?)second["total"]);
        Assert.Equal(all[1].Url, (string?)Assert.Single(second["rows"]!.AsArray())!["url"]);
        Assert.Equal(all[1].Detail, (string?)second["rows"]![0]!["detail"]);
        TestSidecar.AssertProblem(await _sidecar.SendAsync(HttpMethod.Get, $"api/crawls/{id}/issues/no-such-check"), HttpStatusCode.NotFound, "check-not-found");
        TestSidecar.AssertProblem(await _sidecar.SendAsync(HttpMethod.Get, $"api/crawls/{id}/issues/title-too-long?limit=0"), HttpStatusCode.BadRequest, "bad-request");

        // Analysed again on request.
        Assert.Equal(HttpStatusCode.Accepted, (await _sidecar.PostAsync($"api/crawls/{id}/analyze")).Status);
        await events.CrawlStatusAsync(id, "analyzing");
        Assert.Null((await events.CrawlStatusAsync(id, "completed"))["error"]);
    }

    [Fact]
    public async Task ACrawlPausesResumesAndStops()
    {
        await using var events = await EventStreamReader.ConnectAsync(_sidecar.Client);
        var id = (string)(await StartAsync(SlowCrawl()))["id"]!;
        await events.CrawlStatusAsync(id, "running");

        Assert.Equal(HttpStatusCode.NoContent, (await _sidecar.PostAsync($"api/crawls/{id}/pause")).Status);
        await events.CrawlStatusAsync(id, "paused");
        await events.NextAsync("progress", data => (string?)data["crawlId"] == id && (string?)data["status"] == "paused");
        Assert.Equal("paused", (string?)(await _sidecar.GetJsonAsync($"api/crawls/{id}"))["status"]);
        Assert.Equal(HttpStatusCode.NoContent, (await _sidecar.PostAsync($"api/crawls/{id}/pause")).Status);

        // A stream that connects now hears of the live crawl at once, with its progress.
        await using (var late = await EventStreamReader.ConnectAsync(_sidecar.Client))
        {
            Assert.Equal([id], (await late.NextAsync("hello"))["live"]!.AsArray().Select(live => (string?)live));
            Assert.Equal("paused", (string?)(await late.NextAsync("progress", data => (string?)data["crawlId"] == id))["status"]);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await _sidecar.PostAsync($"api/crawls/{id}/resume")).Status);
        await events.CrawlStatusAsync(id, "running");

        // Live here, so it can't be removed, deleted or analysed.
        TestSidecar.AssertProblem(await _sidecar.SendAsync(HttpMethod.Delete, $"api/crawls/{id}"), HttpStatusCode.Conflict, "crawl-busy");
        TestSidecar.AssertProblem(await _sidecar.PostAsync($"api/crawls/{id}/delete-file"), HttpStatusCode.Conflict, "crawl-busy");
        TestSidecar.AssertProblem(await _sidecar.PostAsync($"api/crawls/{id}/analyze"), HttpStatusCode.Conflict, "not-analysable");
        TestSidecar.AssertProblem(await _sidecar.PostAsync($"api/crawls/{id}/continue"), HttpStatusCode.Conflict, "crawl-busy");

        Assert.Equal(HttpStatusCode.Accepted, (await _sidecar.PostAsync($"api/crawls/{id}/stop")).Status);
        await events.CrawlStatusAsync(id, "stopping");
        await events.CrawlStatusAsync(id, "stopped");

        // Stopped by the user, it is analysed at once, as a completed crawl is: its results "so far" are there when it
        // ends (the closing audit's gap 2), and it says stopped again with them.
        await events.CrawlStatusAsync(id, "analyzing");
        await events.CrawlStatusAsync(id, "stopped");
        var stopped = await _sidecar.GetJsonAsync($"api/crawls/{id}");
        Assert.Equal("stopped", (string?)stopped["status"]);
        Assert.NotNull(stopped["analyzedAt"]);
        TestSidecar.AssertProblem(await _sidecar.PostAsync($"api/crawls/{id}/pause"), HttpStatusCode.Conflict, "crawl-busy");
        TestSidecar.AssertProblem(await _sidecar.PostAsync($"api/crawls/{id}/stop"), HttpStatusCode.Conflict, "crawl-busy");
        var analysis = await _sidecar.GetJsonAsync($"api/crawls/{id}/analysis");
        Assert.True((bool?)analysis["score"]?["partial"] ?? analysis["noScoreReason"] is not null);
    }

    /// <summary>A crawl started not to be analysed when it ends isn't analysed when it's stopped either.</summary>
    [Fact]
    public async Task ACrawlStartedNotToBeAnalysedIsNotAnalysedWhenStopped()
    {
        await using var events = await EventStreamReader.ConnectAsync(_sidecar.Client);
        var started = await _sidecar.PostAsync("api/crawls", new JsonObject { ["options"] = SlowCrawl(), ["analyzeWhenDone"] = false });
        var id = (string)started.Body!["id"]!;
        await events.CrawlStatusAsync(id, "running");
        Assert.Equal(HttpStatusCode.Accepted, (await _sidecar.PostAsync($"api/crawls/{id}/stop")).Status);
        await events.CrawlStatusAsync(id, "stopped");

        Assert.Null((await _sidecar.GetJsonAsync($"api/crawls/{id}"))["analyzedAt"]);
        TestSidecar.AssertProblem(await _sidecar.SendAsync(HttpMethod.Get, $"api/crawls/{id}/analysis"), HttpStatusCode.NotFound, "not-analyzed");
    }

    /// <summary>Header values aren't stored, so continuing asks for them again, unless told to go on without them.</summary>
    [Fact]
    public async Task ContinuingAsksForTheSecretsTheFileDoesntKeep()
    {
        var keys = _site.Requests.Watch("X-Crawler-Key");
        await using var events = await EventStreamReader.ConnectAsync(_sidecar.Client);
        var crawl = SlowCrawl();
        crawl["extraHeaders"] = new JsonObject { ["X-Crawler-Key"] = "s3cret-value" };
        var started = await StartAsync(crawl);
        var id = (string)started["id"]!;
        var path = (string)started["path"]!;
        await events.NextAsync("progress", data => (string?)data["crawlId"] == id && (long?)data["fetched"] >= 2);
        Assert.Equal(HttpStatusCode.Accepted, (await _sidecar.PostAsync($"api/crawls/{id}/stop")).Status);
        await events.CrawlStatusAsync(id, "stopped");
        // Its results so far are worked out before it can be continued.
        await events.CrawlStatusAsync(id, "analyzing");
        await events.CrawlStatusAsync(id, "stopped");

        var stored = await _sidecar.GetJsonAsync($"api/crawls/{id}/options");
        Assert.Equal("""{"X-Crawler-Key":""}""", stored["extraHeaders"]!.ToJsonString());
        Assert.False((bool?)stored["hasRedactedProxyCredentials"]);
        Assert.True((await File.ReadAllBytesAsync(path)).AsSpan().IndexOf("s3cret-value"u8) < 0);

        var needs = TestSidecar.AssertProblem(await _sidecar.PostAsync($"api/crawls/{id}/continue", new JsonObject()),
            HttpStatusCode.UnprocessableEntity, "needs-secrets");
        Assert.Equal(["X-Crawler-Key"], needs["missingHeaders"]!.AsArray().Select(name => (string?)name));
        Assert.False((bool?)needs["proxyCredentials"]);

        // Posting back the empty-valued header GET /options returns isn't giving it a value: still asked for.
        var roundTripped = TestSidecar.AssertProblem(
            await _sidecar.PostAsync($"api/crawls/{id}/continue", new JsonObject { ["extraHeaders"] = stored["extraHeaders"]!.DeepClone() }),
            HttpStatusCode.UnprocessableEntity, "needs-secrets");
        Assert.Equal(["X-Crawler-Key"], roundTripped["missingHeaders"]!.AsArray().Select(name => (string?)name));

        // What may change is validated as a new crawl's options are, keyed by field.
        var invalid = TestSidecar.AssertProblem(await _sidecar.PostAsync($"api/crawls/{id}/continue",
            new JsonObject { ["concurrency"] = 0, ["skipMissingHeaders"] = true }), HttpStatusCode.UnprocessableEntity, "invalid-options");
        Assert.Equal(["concurrency"], invalid["errors"]!.AsObject().Select(field => field.Key));

        var sentBefore = keys.Count;
        var continued = await _sidecar.PostAsync($"api/crawls/{id}/continue",
            new JsonObject { ["skipMissingHeaders"] = true, ["maxRequestsPerSecond"] = 0, ["concurrency"] = 8 });
        Assert.Equal(HttpStatusCode.OK, continued.Status);
        Assert.Equal("running", (string?)continued.Body!["status"]);
        await events.CrawlStatusAsync(id, "completed");
        await events.CrawlStatusAsync(id, "analyzing");
        await events.CrawlStatusAsync(id, "completed");

        // Sent with its value before the stop, and not at all (not even empty) after.
        Assert.True(sentBefore > 0);
        Assert.Equal(sentBefore, keys.Count);
        Assert.Equal(["s3cret-value"], keys.Distinct());
        Assert.Equal(CrawlStatus.Completed, CrawlSummaryReader.Read(path).Status);
    }

    [Fact]
    public async Task ContinuingAsksForAProxyPasswordTheFileDoesntKeep()
    {
        // The site's own port stands in for the proxy: what matters is what the crawl file keeps.
        var path = await TestCrawlFiles.CrawlAsync(_site, Path.Combine(_sidecar.DataDirectory, "proxied.sicrawl"), options: new CrawlOptions
        {
            StartUrl = _site.BaseUri.AbsoluteUri,
            MaxUrls = 5,
            ProxyUrl = $"http://crawler:pa55word@127.0.0.1:{_site.BaseUri.Port}/"
        });
        var id = Library.CrawlIds.For(path);
        TestCrawlFiles.SetStoredStatus(path, CrawlStatus.Stopped);

        Assert.True((bool?)(await _sidecar.GetJsonAsync($"api/crawls/{id}/options"))["hasRedactedProxyCredentials"]);
        var needs = TestSidecar.AssertProblem(await _sidecar.PostAsync($"api/crawls/{id}/continue", new JsonObject { ["skipMissingHeaders"] = true }),
            HttpStatusCode.UnprocessableEntity, "needs-secrets");
        Assert.True((bool?)needs["proxyCredentials"]);
        Assert.Empty(needs["missingHeaders"]!.AsArray());

        // A UI that pre-fills the form from GET /options and posts it back gives the proxy as the file keeps it: the user
        // name redacted. That isn't the password, so it is still asked for, not resumed with the redaction marker.
        var storedProxy = (string?)(await _sidecar.GetJsonAsync($"api/crawls/{id}/options"))["proxyUrl"];
        Assert.Contains(CrawlOptions.REDACTED, storedProxy);
        var roundTripped = TestSidecar.AssertProblem(
            await _sidecar.PostAsync($"api/crawls/{id}/continue", new JsonObject { ["skipMissingHeaders"] = true, ["proxyUrl"] = storedProxy }),
            HttpStatusCode.UnprocessableEntity, "needs-secrets");
        Assert.True((bool?)roundTripped["proxyCredentials"]);
    }

    [Fact]
    public async Task ACrawlStartedWithoutAnalysisIsAnalysedOnlyWhenAsked()
    {
        await using var events = await EventStreamReader.ConnectAsync(_sidecar.Client);
        var started = await _sidecar.PostAsync("api/crawls", new JsonObject
        {
            ["options"] = new JsonObject { ["startUrl"] = _site.BaseUri.AbsoluteUri, ["maxUrls"] = 10 },
            ["analyzeWhenDone"] = false
        });
        Assert.Equal(HttpStatusCode.Created, started.Status);
        var id = (string)started.Body!["id"]!;
        await events.CrawlStatusAsync(id, "completed");

        var item = await _sidecar.GetJsonAsync($"api/crawls/{id}");
        Assert.Equal("completed", (string?)item["status"]);
        Assert.Null(item["analyzedAt"]);
        Assert.Null(item["issues"]);
        TestSidecar.AssertProblem(await _sidecar.SendAsync(HttpMethod.Get, $"api/crawls/{id}/analysis"), HttpStatusCode.NotFound, "not-analyzed");
        TestSidecar.AssertProblem(await _sidecar.SendAsync(HttpMethod.Get, $"api/crawls/{id}/issues/title-too-long"), HttpStatusCode.NotFound, "not-analyzed");

        // Cancelling when no analysis runs changes nothing; an unknown crawl has nothing to cancel.
        Assert.Equal(HttpStatusCode.Accepted, (await _sidecar.PostAsync($"api/crawls/{id}/analyze/cancel")).Status);
        TestSidecar.AssertProblem(await _sidecar.SendAsync(HttpMethod.Get, $"api/crawls/{id}/analysis"), HttpStatusCode.NotFound, "not-analyzed");
        TestSidecar.AssertProblem(await _sidecar.PostAsync("api/crawls/0123456789abcdef/analyze/cancel"), HttpStatusCode.NotFound, "crawl-not-found");

        // A URL-limited crawl can't support the orphan check: it's skipped, not counted as clean, and says why.
        Assert.Equal(HttpStatusCode.Accepted, (await _sidecar.PostAsync($"api/crawls/{id}/analyze")).Status);
        await events.CrawlStatusAsync(id, "analyzing");
        await events.CrawlStatusAsync(id, "completed");
        var analysis = await _sidecar.GetJsonAsync($"api/crawls/{id}/analysis");
        var orphanSkip = analysis["skipped"]!.AsArray().Single(skip => (string?)skip!["checkId"] == IssueCatalog.ORPHAN_PAGE.Id)!;
        Assert.Equal(SkippedCheckReasons.URL_LIMIT, (string?)orphanSkip["reason"]!["code"]);
        Assert.Matches(@"^The URL limit kept \d+ internal URLs? out of the crawl", (string?)orphanSkip["reason"]!["text"]);
        Assert.True((bool?)analysis["isPartial"]);
        Assert.False((bool?)analysis["outOfDate"]);
        Assert.DoesNotContain(analysis["counts"]!.AsArray(), count => (string?)count!["checkId"] == IssueCatalog.ORPHAN_PAGE.Id);
        Assert.NotEmpty(analysis["notes"]!.AsArray());
        var orphans = await _sidecar.GetJsonAsync($"api/crawls/{id}/issues/{IssueCatalog.ORPHAN_PAGE.Id}");
        Assert.Null(orphans["total"]);
        Assert.Empty(orphans["rows"]!.AsArray());
    }

    [Fact]
    public async Task ANewCrawlsOptionsAreValidatedByField()
    {
        var crawls = CrawlFileCount();
        var invalid = TestSidecar.AssertProblem(await _sidecar.PostAsync("api/crawls", new JsonObject
        {
            ["options"] = new JsonObject { ["startUrl"] = "not a url", ["maxDepth"] = -1 }
        }), HttpStatusCode.UnprocessableEntity, "invalid-options");
        Assert.Equal(["maxDepth", "startUrl"], invalid["errors"]!.AsObject().Select(field => field.Key).Order(StringComparer.Ordinal));

        var mistyped = TestSidecar.AssertProblem(await _sidecar.PostAsync("api/crawls", new JsonObject
        {
            ["options"] = new JsonObject { ["startUrl"] = _site.BaseUri.AbsoluteUri, ["concurrency"] = "many" }
        }), HttpStatusCode.UnprocessableEntity, "invalid-options");
        Assert.Equal(["concurrency"], mistyped["errors"]!.AsObject().Select(field => field.Key));

        TestSidecar.AssertProblem(await _sidecar.PostAsync("api/crawls", new JsonObject()), HttpStatusCode.BadRequest, "bad-request");
        Assert.Equal(crawls, CrawlFileCount());
    }

    /// <summary>A shutdown stops every crawl and leaves its file stopped, unlocked and resumable.</summary>
    [Fact]
    public async Task ShuttingDownLeavesAStoppedResumableFile()
    {
        var sidecar = await TestSidecar.StartAsync();
        try
        {
            await using var events = await EventStreamReader.ConnectAsync(sidecar.Client);
            var started = await sidecar.PostAsync("api/crawls", new JsonObject { ["options"] = SlowCrawl() });
            Assert.Equal(HttpStatusCode.Created, started.Status);
            var id = (string)started.Body!["id"]!;
            var path = (string)started.Body["path"]!;
            await events.NextAsync("progress", data => (string?)data["crawlId"] == id && (long?)data["fetched"] >= 2);

            Assert.Equal(HttpStatusCode.Accepted, (await sidecar.PostAsync("api/shutdown")).Status);
            await sidecar.Host.WaitForShutdownAsync().WaitAsync(EventStreamReader.TIMEOUT);
            await events.Ended.WaitAsync(EventStreamReader.TIMEOUT);

            Assert.Equal(CrawlStatus.Stopped, CrawlSummaryReader.Read(path).Status);
            Assert.False(CrawlDatabase.IsInUse(path));

            // Resumable, by the engine as by the CLI.
            var options = CrawlDatabase.ReadOptions(path).PlanResume(new ResumeChanges { MaxRequestsPerSecond = 0, Concurrency = 8 }).Options;
            await using var job = new Engine.Crawling.CrawlEngine(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance)
                .Start(new CrawlRequest(options, path, Resume: true));
            Assert.Equal(CrawlStatus.Completed, (await job.Completion.WaitAsync(EventStreamReader.TIMEOUT)).Status);
        }
        finally
        {
            await sidecar.DisposeAsync();
        }
    }

    /// <summary>
    /// Shutdown tells crawls to stop as soon as it begins, before the web server drains the requests in flight, so a slow
    /// request can't let a crawl fetch on after shutdown was asked. Proven by the stop running in StoppingAsync: nothing
    /// new may start once it has, and the crawl is left stopped and resumable.
    /// </summary>
    [Fact]
    public async Task ShutdownStopsCrawlsBeforeDrainingRequests()
    {
        await using var events = await EventStreamReader.ConnectAsync(_sidecar.Client);
        var started = await StartAsync(SlowCrawl());
        var id = (string)started["id"]!;
        var path = (string)started["path"]!;
        await events.NextAsync("progress", data => (string?)data["crawlId"] == id && (long?)data["fetched"] >= 1);

        // StoppingAsync runs before any hosted service (the web server included) stops: the engine's own fix point.
        var shutdown = new CrawlShutdown(_sidecar.Host.Services.GetRequiredService<CrawlManager>(), _sidecar.Host.Services.GetRequiredService<ExportManager>());
        await shutdown.StoppingAsync(CancellationToken.None);

        // From here nothing new starts, because the crawls were already told to stop.
        TestSidecar.AssertProblem(await _sidecar.PostAsync("api/crawls", new JsonObject { ["options"] = SlowCrawl() }),
            HttpStatusCode.Conflict, "crawl-busy");

        await shutdown.StopAsync(CancellationToken.None);
        Assert.Equal(CrawlStatus.Stopped, CrawlSummaryReader.Read(path).Status);
        Assert.False(CrawlDatabase.IsInUse(path));
    }

    /// <summary>
    /// A crawl's events reach the stream in the order the manager made the changes, because each change and the event that
    /// announces it happen under one lock. Forced here: while an event is being published, another thread's call that
    /// needs that lock can't run until the publish is done.
    /// </summary>
    [Fact]
    public async Task EventsArePublishedUnderTheLockSoTheyKeepTheirOrder()
    {
        var manager = _sidecar.Host.Services.GetRequiredService<CrawlManager>();
        var acted = 0;
        Task? probe = null;
        var reachedLock = new ManualResetEventSlim();
        var raced = false;
        manager.Publishing = evt =>
        {
            // The crawl id comes from the event itself, so the very first event (the crawl's own, published as it starts)
            // is the one probed, whatever its kind.
            var crawlId = evt switch
            {
                CrawlEventDto crawlEvent => crawlEvent.CrawlId,
                ProgressEventDto progress => progress.CrawlId,
                _ => null
            };
            if (crawlId is null || Interlocked.Exchange(ref acted, 1) == 1)
            {
                return;
            }

            // Another thread calls into the manager, which takes the same lock to read _crawls; it can't finish while we
            // hold it. Were the event published outside the lock, this call would run straight through.
            probe = Task.Run(() =>
            {
                reachedLock.Set();
                manager.PathOf(crawlId);
            });
            reachedLock.Wait();
            raced = probe.Wait(TimeSpan.FromMilliseconds(500));
        };

        try
        {
            await StartAsync(new JsonObject { ["startUrl"] = _site.BaseUri.AbsoluteUri, ["maxUrls"] = 5 });
            using var timeout = new CancellationTokenSource(EventStreamReader.TIMEOUT);
            while (Volatile.Read(ref acted) == 0)
            {
                await Task.Delay(20, timeout.Token);
            }

            await (probe ?? Task.CompletedTask);
            Assert.False(raced, "A manager call ran while an event was being published: publishing isn't under the lock.");
        }
        finally
        {
            manager.Publishing = null;
        }
    }

    /// <summary>
    /// A stop during a continue's start, then a start that fails, still ends: the crawl's stored status and a library
    /// change are published, so the UI leaves "stopping" instead of waiting for ever.
    /// </summary>
    [Fact]
    public async Task AStopThenAFailedStartStillEnds()
    {
        var path = await TestCrawlFiles.CrawlAsync(_site, Path.Combine(_sidecar.DataDirectory, "continue-fails.sicrawl"), maxUrls: 5);
        var id = Library.CrawlIds.For(path);
        TestCrawlFiles.SetStoredStatus(path, CrawlStatus.Stopped);

        var manager = _sidecar.Host.Services.GetRequiredService<CrawlManager>();
        using var release = new ManualResetEventSlim();
        manager.StartJob = (_, _) =>
        {
            release.Wait(EventStreamReader.TIMEOUT);
            throw new CrawlFileInUseException("database is locked");
        };

        try
        {
            await using var events = await EventStreamReader.ConnectAsync(_sidecar.Client);
            var continuing = _sidecar.PostAsync($"api/crawls/{id}/continue");

            // The crawl is opening (its start is blocked): the library shows it running. Stop it while it starts.
            await WaitForStatusAsync(id, "running");
            Assert.Equal(HttpStatusCode.Accepted, (await _sidecar.PostAsync($"api/crawls/{id}/stop")).Status);
            await events.CrawlStatusAsync(id, "stopping");

            // Now let the start fail: the crawl ends, with its stored status and a library event, not stuck on stopping.
            release.Set();
            await events.CrawlStatusAsync(id, "stopped");
            await events.NextAsync("library");
            TestSidecar.AssertProblem(await continuing, HttpStatusCode.Conflict, "crawl-busy");
            Assert.Equal("stopped", (string?)(await _sidecar.GetJsonAsync($"api/crawls/{id}"))["status"]);
        }
        finally
        {
            manager.StartJob = null;
        }
    }

    /// <summary>
    /// Continuing a crawl someone is part-way through reading (an export, another program) is refused within seconds as
    /// busy, in words that say so, rather than blaming another program for writing to it. The crawl stays as it was.
    /// </summary>
    [Fact]
    public async Task ContinuingACrawlBeingReadIsRefusedAsBusySayingWhy()
    {
        var path = await TestCrawlFiles.CrawlAsync(_site, Path.Combine(_sidecar.DataDirectory, "continue-read.sicrawl"), maxUrls: 5);
        var id = Library.CrawlIds.For(path);
        TestCrawlFiles.SetStoredStatus(path, CrawlStatus.Stopped);

        using (var connection = CrawlDatabase.OpenReader(path))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT url FROM urls";
            using var reading = command.ExecuteReader();
            Assert.True(reading.Read());
            var refused = TestSidecar.AssertProblem(await _sidecar.PostAsync($"api/crawls/{id}/continue"), HttpStatusCode.Conflict, "crawl-busy");
            Assert.Contains("being read", (string?)refused["title"], StringComparison.Ordinal);
        }

        Assert.Equal("stopped", (string?)(await _sidecar.GetJsonAsync($"api/crawls/{id}"))["status"]);
    }

    private async Task WaitForStatusAsync(string id, string status)
    {
        using var timeout = new CancellationTokenSource(EventStreamReader.TIMEOUT);
        while ((string?)(await _sidecar.GetJsonAsync($"api/crawls/{id}"))["status"] != status)
        {
            await Task.Delay(20, timeout.Token);
        }
    }

    private int CrawlFileCount() => Directory.Exists(_sidecar.DataDirectory) ? Directory.GetFiles(_sidecar.DataDirectory).Length : 0;

    /// <summary>Slow enough to pause, stop or shut down part-way: the 60-page site has a few hundred URLs.</summary>
    private JsonObject SlowCrawl() => new() { ["startUrl"] = _site.BaseUri.AbsoluteUri, ["maxRequestsPerSecond"] = 5, ["concurrency"] = 2 };

    private async Task<JsonNode> StartAsync(JsonObject options)
    {
        var started = await _sidecar.PostAsync("api/crawls", new JsonObject { ["options"] = options });
        Assert.True(started.Status == HttpStatusCode.Created, $"{(int)started.Status} {started.Body?.ToJsonString()}");
        return started.Body!;
    }
}
