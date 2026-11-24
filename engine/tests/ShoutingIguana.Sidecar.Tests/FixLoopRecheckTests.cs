using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Sidecar.Crawls;
using ShoutingIguana.Sidecar.Library;
using ShoutingIguana.Sidecar.Tests.Support;

namespace ShoutingIguana.Sidecar.Tests;

/// <summary>
/// Rechecks in the sidecar (the fix loop): the plan, the flow (the analysis first when it is out of date, the recheck's run,
/// the analysis that settles it) with its events in order, the reads, the refusals both ways, the secrets, and Stop, end
/// to end on the in-process synthetic site: crawled at revision 0, switched to revision 1, rechecked, settled.
/// </summary>
public sealed class FixLoopRecheckTests(RecheckSite site) : IClassFixture<RecheckSite>, IAsyncLifetime
{
    private const string CLIENT_ERRORS = "internal-client-error";

    private TestSidecar _sidecar = null!;

    public async Task InitializeAsync() => _sidecar = await TestSidecar.StartAsync();

    public async Task DisposeAsync() => await _sidecar.DisposeAsync();

    /// <summary>
    /// A crawl whose analysis is out of date is analysed first, then rechecked, then analysed again, which settles the
    /// recheck: the crawl's events say analysing, rechecking, analysing, then what the file says; the recheck's say it
    /// started, how it went, that its run ended, and then its result. The reads and the library item say the same.
    /// </summary>
    [Fact]
    public async Task ARecheckIsAnalysedFirstThenRunAndSettledWithItsEventsInOrder()
    {
        var path = FixLoopCrawls.AddTo(_sidecar, site.Then, "site", change: TestCrawlFiles.StampAsCrawledAgain);
        var id = CrawlIds.For(path);

        var plan = (await _sidecar.PostAsync($"api/crawls/{id}/rechecks/plan", new JsonObject { ["checkId"] = CLIENT_ERRORS })).Body!;
        var expected = RecheckPlanReader.Plan(path, new RecheckAsk(CLIENT_ERRORS), new AnalysisSettings());
        Assert.Equal((CLIENT_ERRORS, false, "[\"urls\"]", expected.Judged, expected.Phase1, expected.Phase2Max, expected.Limit, expected.Capped, expected.NotRechecked),
            ((string?)plan["checkId"], (bool)plan["urlList"]!, plan["kinds"]!.ToJsonString(), (int)plan["judged"]!, (int)plan["phase1"]!, (int)plan["phase2Max"]!,
                (int)plan["limit"]!, (bool)plan["capped"]!, (int)plan["notRechecked"]!));
        Assert.True((bool)plan["analysisFirst"]!);
        Assert.True(expected.Judged > 0);
        Assert.Equal("""{"missingHeaders":[],"proxyCredentials":false,"auth":false}""", plan["secrets"]!.ToJsonString());
        Assert.Null(plan["refused"]);
        Assert.False(plan.AsObject().ContainsKey("recheckId"));

        await using var events = await EventStreamReader.ConnectAsync(_sidecar.Client);
        var started = await _sidecar.PostAsync($"api/crawls/{id}/rechecks", new JsonObject { ["checkId"] = CLIENT_ERRORS });
        Assert.True(started.Status == HttpStatusCode.Accepted, started.Body?.ToJsonString());
        Assert.Equal((1, true), ((int)started.Body!["recheckId"]!, (bool)started.Body["analysisFirst"]!));
        await events.CrawlStatusAsync(id, "completed");

        // The crawl's statuses, and where the recheck's events and the progress fall among them.
        List<SseEvent> seen = [.. events.Seen().Where(seen => (string?)seen.Data["crawlId"] == id)];
        Assert.Equal(["analyzing", "rechecking", "analyzing", "completed"], seen.Where(seen => seen.Name == "crawl").Select(seen => (string)seen.Data["status"]!));
        var crawlEvents = seen.Select((seen, index) => (seen, index)).Where(entry => entry.seen.Name == "crawl").Select(entry => entry.index).ToList();
        var (rechecking, analysingAfter, completed) = (crawlEvents[1], crawlEvents[2], crawlEvents[3]);
        Assert.Contains(seen.Take(rechecking), seen => seen.Name == "analysis");
        Assert.Contains(seen.Skip(analysingAfter).Take(completed - analysingAfter), seen => seen.Name == "analysis");

        var rechecks = seen.Select((seen, index) => (seen, index)).Where(entry => entry.seen.Name == "recheck").ToList();
        Assert.All(rechecks, entry => Assert.Equal(1, (int)entry.seen.Data["recheckId"]!));
        Assert.True(rechecks[0].index > rechecking);
        Assert.Equal(("running", null), ((string?)rechecks[0].seen.Data["status"], rechecks[0].seen.Data["result"]));
        var runEnded = rechecks.Single(entry => entry.index < analysingAfter && (string?)entry.seen.Data["status"] != "running");
        Assert.Equal(("completed", null), ((string?)runEnded.seen.Data["status"], runEnded.seen.Data["result"]));
        Assert.Equal(runEnded, rechecks.Where(entry => entry.index < analysingAfter).Last());
        var settled = rechecks[^1];
        Assert.True(settled.index > analysingAfter && settled.index < completed);
        Assert.Equal("completed", (string?)settled.seen.Data["status"]);
        Assert.NotNull(settled.seen.Data["result"]);
        Assert.Equal(rechecks.Count - 1, rechecks.Count(entry => entry.seen.Data["result"] is null));

        // The run's progress is the crawl's, as rechecking.
        List<SseEvent> progress = [.. seen.Skip(rechecking).Take(analysingAfter - rechecking).Where(seen => seen.Name == "progress")];
        Assert.NotEmpty(progress);
        Assert.All(progress, entry => Assert.Equal(("rechecking", "rechecking"), ((string?)entry.Data["status"], (string?)entry.Data["phase"])));
        Assert.DoesNotContain(seen.Skip(analysingAfter), seen => seen.Name == "progress");

        // The file keeps it, as the reads serve it.
        var record = RecheckReader.Read(path, 1)!;
        Assert.Equal(RecheckStatus.Completed, record.Status);
        Assert.True(record.Result!.Fixed > 0, $"{record.Result}");
        var listed = Assert.Single((await _sidecar.GetJsonAsync($"api/crawls/{id}/rechecks"))["rechecks"]!.AsArray())!;
        Assert.Equal((1, "completed", CLIENT_ERRORS, record.Judged, record.Fetched, record.CountBefore, false),
            ((int)listed["id"]!, (string?)listed["status"], (string?)listed["checkId"], (int)listed["judged"]!, (int)listed["fetched"]!, (long?)listed["countBefore"],
                (bool)listed["urlList"]!));
        var result = listed["result"]!;
        Assert.Equal((record.Result.Fixed, record.Result.Still, record.Result.NotChecked, record.Result.NotRechecked, record.Result.New, record.Result.ScoreBefore, record.Result.ScoreAfter),
            ((int)result["fixed"]!, (int)result["still"]!, (int)result["notChecked"]!, (int)result["notRechecked"]!, (int)result["new"]!, (int?)result["scoreBefore"],
                (int?)result["scoreAfter"]));
        Assert.Equal(result.ToJsonString(), settled.seen.Data["result"]!.ToJsonString());

        // How the site answered it for the URLs it judged, as the file keeps it.
        var answers = record.Answers!;
        Assert.Equal((answers.Read, answers.RateLimited, answers.Blocked, answers.RobotsTxt, answers.NoAnswer, answers.NotAsked, answers.Refused),
            ((int)listed["answers"]!["read"]!, (int)listed["answers"]!["rateLimited"]!, (int)listed["answers"]!["blocked"]!, (int)listed["answers"]!["robotsTxt"]!,
                (int)listed["answers"]!["noAnswer"]!, (int)listed["answers"]!["notAsked"]!, (int)listed["answers"]!["refused"]!));
        Assert.Equal(record.Judged, answers.Read + answers.Refused + answers.NotAsked);
        Assert.EndsWith("Z", (string?)listed["startedAt"]);
        Assert.NotNull(listed["settledAt"]);
        Assert.Equal(listed.ToJsonString(), (await _sidecar.GetJsonAsync($"api/crawls/{id}/rechecks/1")).ToJsonString());
        TestSidecar.AssertProblem(await _sidecar.SendAsync(HttpMethod.Get, $"api/crawls/{id}/rechecks/2"), HttpStatusCode.NotFound, "recheck-not-found");
        TestSidecar.AssertProblem(await _sidecar.SendAsync(HttpMethod.Get, $"api/crawls/{id}/rechecks/first"), HttpStatusCode.NotFound, "recheck-not-found");

        var rows = (await _sidecar.PostAsync($"api/crawls/{id}/rechecks/1/rows", new JsonObject { ["roles"] = new JsonArray("judged"), ["outcomes"] = new JsonArray("fixed") })).Body!;
        var expectedRows = RecheckReader.ReadRows(path, 1, [RecheckOutcome.Fixed], [RecheckRole.Judged], 0, 100)!;
        Assert.Equal((expectedRows.Total, record.Result.Fixed), ((int)rows["total"]!, (int)rows["total"]!));
        Assert.Equal(expectedRows.Rows.Select(row => $"{row.UrlId} {row.Url} judged fixed {row.FlaggedBefore} {row.DetailBefore} {row.DetailAfter} {row.StatusCode}"),
            rows["rows"]!.AsArray().Select(row => $"{(int)row!["urlId"]!} {(string?)row["url"]} {(string?)row["role"]} {(string?)row["outcome"]} {(bool)row["flaggedBefore"]!} {(string?)row["detailBefore"]} {(string?)row["detailAfter"]} {(int?)row["statusCode"]}"));
        var badOutcome = TestSidecar.AssertProblem(await _sidecar.PostAsync($"api/crawls/{id}/rechecks/1/rows", new JsonObject { ["outcomes"] = new JsonArray("gone") }),
            HttpStatusCode.UnprocessableEntity, "invalid-query");
        Assert.NotNull(badOutcome["errors"]!["outcomes[0]"]);
        TestSidecar.AssertProblem(await _sidecar.PostAsync($"api/crawls/{id}/rechecks/1/rows", new JsonObject { ["limit"] = 0 }), HttpStatusCode.UnprocessableEntity, "invalid-query");
        TestSidecar.AssertProblem(await _sidecar.PostAsync($"api/crawls/{id}/rechecks/9/rows"), HttpStatusCode.NotFound, "recheck-not-found");

        // The library item: the crawl as it was, its analysis today's, and its recheck in brief.
        var item = await _sidecar.GetJsonAsync($"api/crawls/{id}");
        Assert.Equal(("completed", false), ((string?)item["status"], (bool)item["outOfDate"]!));
        var brief = item["rechecks"]!;
        Assert.Equal(["count", "last"], brief.AsObject().Select(property => property.Key));
        Assert.Equal((1, 1, CLIENT_ERRORS, record.Judged, record.Result.Fixed),
            ((int)brief["count"]!, (int)brief["last"]!["id"]!, (string?)brief["last"]!["checkId"], (int)brief["last"]!["judged"]!, (int?)brief["last"]!["fixed"]));
        Assert.Equal(record.EndedAt!.Value.UtcDateTime, (DateTime)brief["last"]!["endedAt"]!);
    }

    /// <summary>
    /// What can't be rechecked, the plan says with the problem a recheck would answer, and a recheck is refused the same:
    /// a crawl that isn't the site's latest, a check only a full crawl can settle, a crawl never analysed, interrupted, or
    /// in use elsewhere. An ask that can't be is invalid.
    /// </summary>
    [Fact]
    public async Task WhatCantBeRecheckedIsSaidByThePlanAndRefused()
    {
        var older = CrawlIds.For(FixLoopCrawls.AddTo(_sidecar, site.Then, "older"));
        var latestPath = FixLoopCrawls.AddTo(_sidecar, site.Then, "latest", 10);
        var latest = CrawlIds.For(latestPath);
        await AssertRefusedAsync(_sidecar, older, new JsonObject { ["checkId"] = CLIENT_ERRORS }, HttpStatusCode.Conflict, "recheck-not-latest");
        await AssertRefusedAsync(_sidecar, latest, new JsonObject { ["checkId"] = "orphan-page" }, HttpStatusCode.UnprocessableEntity, "recheck-unsupported");

        await using (var alone = await TestSidecar.StartAsync())
        {
            await AssertRefusedAsync(alone, CrawlIds.For(FixLoopCrawls.AddTo(alone, site.Then, "unanalysed", change: TestCrawlFiles.DropAnalysis)),
                new JsonObject { ["checkId"] = CLIENT_ERRORS }, HttpStatusCode.NotFound, "not-analyzed");
        }

        await using (var alone = await TestSidecar.StartAsync())
        {
            await AssertRefusedAsync(alone, CrawlIds.For(FixLoopCrawls.AddTo(alone, site.Then, "interrupted", change: path => TestCrawlFiles.SetStoredStatus(path, CrawlStatus.Running))),
                new JsonObject { ["checkId"] = CLIENT_ERRORS }, HttpStatusCode.Conflict, "not-analysable");
        }

        using (TestCrawlFiles.HoldLock(latestPath))
        {
            await AssertRefusedAsync(_sidecar, latest, new JsonObject { ["checkId"] = CLIENT_ERRORS }, HttpStatusCode.Conflict, "crawl-busy");
        }

        // What can't be asked.
        var url = $"api/crawls/{latest}/rechecks/plan";
        TestSidecar.AssertProblem(await _sidecar.PostAsync(url, new JsonObject()), HttpStatusCode.UnprocessableEntity, "invalid-query");
        TestSidecar.AssertProblem(await _sidecar.PostAsync(url, new JsonObject { ["group"] = new JsonObject { ["detail"] = "x" } }), HttpStatusCode.UnprocessableEntity, "invalid-query");
        TestSidecar.AssertProblem(await _sidecar.PostAsync(url, new JsonObject { ["checkId"] = "no-such-check" }), HttpStatusCode.NotFound, "check-not-found");
        TestSidecar.AssertProblem(await _sidecar.PostAsync(url, new JsonObject { ["urlIds"] = new JsonArray([.. Enumerable.Range(1, 1_001).Select(urlId => (JsonNode)urlId)]) }),
            HttpStatusCode.UnprocessableEntity, "invalid-query");
        var unknown = TestSidecar.AssertProblem(await _sidecar.PostAsync(url, new JsonObject { ["urlIds"] = new JsonArray(999_999) }), HttpStatusCode.UnprocessableEntity, "invalid-query");
        Assert.NotNull(unknown["errors"]!["urlIds"]);
        TestSidecar.AssertProblem(await _sidecar.PostAsync(url, new JsonObject { ["checkId"] = CLIENT_ERRORS, ["extraHeaders"] = new JsonObject() }), HttpStatusCode.BadRequest,
            "bad-request");
        TestSidecar.AssertProblem(await _sidecar.PostAsync($"api/crawls/0123456789abcdef/rechecks/plan", new JsonObject { ["checkId"] = CLIENT_ERRORS }), HttpStatusCode.NotFound,
            "crawl-not-found");
    }

    /// <summary>
    /// A crawl that sent headers keeps only their names: the plan says which a recheck needs, and a recheck without them is
    /// refused as Continue refuses (<c>needs-secrets</c>), until they're given or skipped.
    /// </summary>
    [Fact]
    public async Task ARecheckNeedsTheSecretsContinueWould()
    {
        var path = FixLoopCrawls.AddTo(_sidecar, site.Then, "headers", change: path => TestCrawlFiles.SetHeaderNames(path, "X-Secret"));
        var id = CrawlIds.For(path);
        var home = UrlGridReader.Lookup(path, "/")!.Id;
        var ask = new JsonObject { ["urlIds"] = new JsonArray(home) };

        var plan = (await _sidecar.PostAsync($"api/crawls/{id}/rechecks/plan", ask)).Body!;
        Assert.Equal("""{"missingHeaders":["X-Secret"],"proxyCredentials":false,"auth":false}""", plan["secrets"]!.ToJsonString());
        Assert.Null(plan["refused"]);
        var needs = TestSidecar.AssertProblem(await _sidecar.PostAsync($"api/crawls/{id}/rechecks", ask.DeepClone()), HttpStatusCode.UnprocessableEntity, "needs-secrets");
        Assert.Equal("""["X-Secret"]""", needs["missingHeaders"]!.ToJsonString());
        Assert.StartsWith("Rechecking this crawl needs", (string?)needs["title"], StringComparison.Ordinal);

        var sent = site.Server.Requests.Watch("X-Secret");
        await using var events = await EventStreamReader.ConnectAsync(_sidecar.Client);
        var given = ask.DeepClone().AsObject();
        given["extraHeaders"] = new JsonObject { ["X-Secret"] = "1" };
        var started = await _sidecar.PostAsync($"api/crawls/{id}/rechecks", given);
        Assert.True(started.Status == HttpStatusCode.Accepted, started.Body?.ToJsonString());
        await events.CrawlStatusAsync(id, "completed");
        Assert.Contains("1", sent);
        var record = RecheckReader.Read(path, 1)!;
        Assert.Equal((true, (string?)null, 1), (record.UrlList, record.CheckId, record.Judged));

        var skipped = ask.DeepClone().AsObject();
        skipped["skipMissingHeaders"] = true;
        Assert.Equal(HttpStatusCode.Accepted, (await _sidecar.PostAsync($"api/crawls/{id}/rechecks", skipped)).Status);
        await events.CrawlStatusAsync(id, "completed");
        Assert.Equal(2, RecheckReader.ReadAll(path).Count);
    }

    /// <summary>
    /// A recheck and another run of its site never run together here, whichever comes second; a crawl being rechecked is
    /// busy for everything else; a stream that connects meanwhile hears of the recheck at once; and Stop ends it, after
    /// which it is analysed and settled as stopped.
    /// </summary>
    [Fact]
    public async Task ARecheckAndARunOfItsSiteNeverRunTogetherAndStopEndsIt()
    {
        // Out of date, so it is analysed first: a stream that connects once it rechecks hears of no analysis.
        var path = FixLoopCrawls.AddTo(_sidecar, site.Then, "site", change: TestCrawlFiles.StampAsCrawledAgain);
        var id = CrawlIds.For(path);
        var manager = _sidecar.Host.Services.GetRequiredService<CrawlManager>();

        // Slowed, so it is still fetching while the refusals are asked for.
        manager.RecheckJob = (request, observer) =>
            new CrawlEngine(NullLoggerFactory.Instance).Recheck(request with { Options = request.Options with { MaxRequestsPerSecond = 2, Concurrency = 1 } }, observer);
        try
        {
            await using var events = await EventStreamReader.ConnectAsync(_sidecar.Client);
            Assert.Equal(HttpStatusCode.Accepted, (await _sidecar.PostAsync($"api/crawls/{id}/rechecks", new JsonObject { ["checkId"] = CLIENT_ERRORS })).Status);
            await events.NextAsync("recheck", data => (string?)data["crawlId"] == id);
            Assert.Equal("rechecking", (string?)(await _sidecar.GetJsonAsync($"api/crawls/{id}"))["status"]);

            var crawl = TestSidecar.AssertProblem(await _sidecar.PostAsync("api/crawls", new JsonObject { ["options"] = new JsonObject { ["startUrl"] = site.Server.BaseUri.AbsoluteUri } }),
                HttpStatusCode.Conflict, "site-busy");
            Assert.Equal("This site is being rechecked. Start the check once that’s done.", (string?)crawl["title"]);
            TestSidecar.AssertProblem(await _sidecar.PostAsync($"api/crawls/{id}/rechecks", new JsonObject { ["checkId"] = CLIENT_ERRORS }), HttpStatusCode.Conflict, "crawl-busy");
            Assert.Equal("crawl-busy", (string?)(await _sidecar.PostAsync($"api/crawls/{id}/rechecks/plan", new JsonObject { ["checkId"] = CLIENT_ERRORS })).Body!["refused"]!["type"]);
            TestSidecar.AssertProblem(await _sidecar.PostAsync($"api/crawls/{id}/analyze"), HttpStatusCode.Conflict, "crawl-busy");
            TestSidecar.AssertProblem(await _sidecar.PostAsync($"api/crawls/{id}/continue"), HttpStatusCode.Conflict, "crawl-busy");
            TestSidecar.AssertProblem(await _sidecar.SendAsync(HttpMethod.Delete, $"api/crawls/{id}"), HttpStatusCode.Conflict, "crawl-busy");
            TestSidecar.AssertProblem(await _sidecar.PostAsync($"api/crawls/{id}/pause"), HttpStatusCode.Conflict, "crawl-busy");

            // A stream that connects now hears of the recheck straight after its hello and the crawl's progress.
            await using (var later = await EventStreamReader.ConnectAsync(_sidecar.Client))
            {
                var hello = await later.NextAsync("hello");
                Assert.Contains(id, hello["live"]!.AsArray().Select(live => (string?)live));
                Assert.DoesNotContain(id, hello["analyzing"]!.AsArray().Select(analysing => (string?)analysing));
                Assert.Equal(1, (int)(await later.NextAsync("recheck", data => (string?)data["crawlId"] == id))["recheckId"]!);
                Assert.DoesNotContain(later.Seen(), seen => seen.Name == "analysis");
            }

            Assert.Equal(HttpStatusCode.Accepted, (await _sidecar.PostAsync($"api/crawls/{id}/stop")).Status);
            await events.CrawlStatusAsync(id, "stopping");
            var settled = await events.NextAsync("recheck", data => (string?)data["crawlId"] == id && data["result"] is not null);
            Assert.Equal("stopped", (string?)settled["status"]);
            await events.CrawlStatusAsync(id, "completed");
            var record = RecheckReader.Read(path, 1)!;
            Assert.Equal(RecheckStatus.Stopped, record.Status);
            Assert.True(record.Result!.NotRechecked > 0, $"{record.Result}");
        }
        finally
        {
            manager.RecheckJob = null;
        }

        // The other way: a crawl of the site running here holds a recheck of the site's latest crawl up.
        await using var stream = await EventStreamReader.ConnectAsync(_sidecar.Client);
        var running = await _sidecar.PostAsync("api/crawls", new JsonObject
        {
            ["options"] = new JsonObject { ["startUrl"] = site.Server.BaseUri.AbsoluteUri, ["browserFallback"] = false, ["maxRequestsPerSecond"] = 2, ["concurrency"] = 1 }, ["analyzeWhenDone"] = false
        });
        Assert.True(running.Status == HttpStatusCode.Created, running.Body?.ToJsonString());
        var runningId = (string)running.Body!["id"]!;
        try
        {
            var refused = (await _sidecar.PostAsync($"api/crawls/{id}/rechecks/plan", new JsonObject { ["checkId"] = CLIENT_ERRORS })).Body!["refused"]!;
            Assert.Equal(("site-busy", "A check of this site is running. Its results will show what’s fixed."), ((string?)refused["type"], (string?)refused["title"]));
            TestSidecar.AssertProblem(await _sidecar.PostAsync($"api/crawls/{id}/rechecks", new JsonObject { ["checkId"] = CLIENT_ERRORS }), HttpStatusCode.Conflict, "site-busy");
        }
        finally
        {
            await _sidecar.PostAsync($"api/crawls/{runningId}/stop");
            await stream.CrawlStatusAsync(runningId, "stopped");
        }

        // Once it has stopped, the crawl rechecked is no longer the site's latest.
        Assert.Equal("recheck-not-latest",
            (string?)(await _sidecar.PostAsync($"api/crawls/{id}/rechecks/plan", new JsonObject { ["checkId"] = CLIENT_ERRORS })).Body!["refused"]!["type"]);
    }

    /// <summary>
    /// Stopped while its analysis first runs, a recheck isn't run at all: the analysis is cancelled, the crawl is as its
    /// file says, and the file has no recheck.
    /// </summary>
    [Fact]
    public async Task ARecheckStoppedInItsAnalysisFirstIsNotRun()
    {
        var path = FixLoopCrawls.AddTo(_sidecar, site.Then, "site", change: TestCrawlFiles.StampAsCrawledAgain);
        var id = CrawlIds.For(path);
        var manager = _sidecar.Host.Services.GetRequiredService<CrawlManager>();
        using var analysing = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        manager.AnalysisStarting = _ =>
        {
            analysing.Set();
            release.Wait(EventStreamReader.TIMEOUT);
        };

        try
        {
            await using var events = await EventStreamReader.ConnectAsync(_sidecar.Client);
            Assert.Equal(HttpStatusCode.Accepted, (await _sidecar.PostAsync($"api/crawls/{id}/rechecks", new JsonObject { ["checkId"] = CLIENT_ERRORS })).Status);
            Assert.True(analysing.Wait(EventStreamReader.TIMEOUT));
            Assert.Equal(HttpStatusCode.Accepted, (await _sidecar.PostAsync($"api/crawls/{id}/stop")).Status);
            release.Set();
            await events.CrawlStatusAsync(id, "completed");

            Assert.Equal(["analyzing", "completed"],
                events.Seen().Where(seen => seen.Name == "crawl" && (string?)seen.Data["crawlId"] == id).Select(seen => (string)seen.Data["status"]!));
            Assert.DoesNotContain(events.Seen(), seen => seen.Name == "recheck");
            Assert.Empty(RecheckReader.ReadAll(path));
            Assert.Empty((await _sidecar.GetJsonAsync($"api/crawls/{id}/rechecks"))["rechecks"]!.AsArray());
        }
        finally
        {
            manager.AnalysisStarting = null;
            release.Set();
        }
    }

    /// <summary>
    /// A recheck that fails part-way stores nothing, so nothing is analysed after it: its run's event says failed, the crawl
    /// ends with why, and the crawl file is as it was, its analysis today's and no recheck in it.
    /// </summary>
    [Fact]
    public async Task ARecheckThatFailsIsNotAnalysedAndLeavesTheCrawlAsItWas()
    {
        var path = FixLoopCrawls.AddTo(_sidecar, site.Then, "site");
        var id = CrawlIds.For(path);
        var analysedAt = IssueReader.ReadAnalysis(path)!.AnalyzedAt;
        var manager = _sidecar.Host.Services.GetRequiredService<CrawlManager>();
        manager.RecheckJob = (request, observer) => new CrawlEngine(NullLoggerFactory.Instance).Recheck(request with
        {
            AtStage = stage => throw new InvalidOperationException($"A test's fault at {stage}")
        }, observer);
        try
        {
            await using var events = await EventStreamReader.ConnectAsync(_sidecar.Client);
            Assert.Equal(HttpStatusCode.Accepted, (await _sidecar.PostAsync($"api/crawls/{id}/rechecks", new JsonObject { ["checkId"] = CLIENT_ERRORS })).Status);
            var ended = await events.CrawlStatusAsync(id, "completed");

            Assert.Equal(["rechecking", "completed"],
                events.Seen().Where(seen => seen.Name == "crawl" && (string?)seen.Data["crawlId"] == id).Select(seen => (string)seen.Data["status"]!));
            Assert.StartsWith("The recheck failed: A test's fault at RobotsStored", (string?)ended["error"]);
            var last = events.Seen().Last(seen => seen.Name == "recheck" && (string?)seen.Data["crawlId"] == id);
            Assert.Equal(("failed", null), ((string?)last.Data["status"], last.Data["result"]));
            Assert.DoesNotContain(events.Seen(), seen => seen.Name == "analysis" && (string?)seen.Data["crawlId"] == id);
            Assert.Empty(RecheckReader.ReadAll(path));
            Assert.Equal(analysedAt, IssueReader.ReadAnalysis(path)!.AnalyzedAt);
            Assert.False((bool)(await _sidecar.GetJsonAsync($"api/crawls/{id}"))["outOfDate"]!);
        }
        finally
        {
            manager.RecheckJob = null;
        }
    }

    /// <summary>The plan's refusal, and the same problem when the recheck is asked for.</summary>
    private static async Task AssertRefusedAsync(TestSidecar sidecar, string id, JsonObject ask, HttpStatusCode status, string type)
    {
        var plan = await sidecar.PostAsync($"api/crawls/{id}/rechecks/plan", ask);
        Assert.True(plan.Status == HttpStatusCode.OK, plan.Body?.ToJsonString());
        var refused = plan.Body!["refused"]!;
        Assert.Equal(type, (string?)refused["type"]);
        var problem = TestSidecar.AssertProblem(await sidecar.PostAsync($"api/crawls/{id}/rechecks", ask.DeepClone()), status, type);
        Assert.Equal((string?)refused["title"], (string?)problem["title"]);
    }
}
