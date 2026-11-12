using System.Net;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Sidecar.Library;
using ShoutingIguana.Sidecar.Tests.Support;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Sidecar.Tests;

/// <summary>
/// Whether an analysis still stands, as the library and the analysis endpoint say it, for crawl files this engine made
/// and for files an earlier engine analysed, which never fail: they're out of date, and analysing them again mends them.
/// </summary>
public sealed class AnalysisStateTests : IAsyncLifetime
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
    public async Task AnAnalysisByAnEarlierEngineIsOutOfDateUntilAnalysedAgain()
    {
        var path = await TestCrawlFiles.CrawlAsync(_site, Path.Combine(_sidecar.DataDirectory, "earlier.sicrawl"), analyse: true);
        TestCrawlFiles.AsAnalysedByAnEarlierEngine(path);
        var id = CrawlIds.For(path);

        Assert.True((bool?)(await _sidecar.GetJsonAsync($"api/crawls/{id}"))["outOfDate"]);
        var earlier = await _sidecar.GetJsonAsync($"api/crawls/{id}/analysis");
        Assert.Null(earlier["isPartial"]);
        Assert.True((bool?)earlier["outOfDate"]);
        Assert.Equal(["engineChanged"], earlier["outOfDateReasons"]!.AsArray().Select(reason => (string?)reason));
        Assert.Contains(earlier["skipped"]!.AsArray(), skip => (string?)skip!["checkId"] == IssueCatalog.ORPHAN_PAGE.Id);
        Assert.All(earlier["skipped"]!.AsArray(), skip => Assert.Null(skip!["reason"]));
        Assert.NotEmpty(earlier["notes"]!.AsArray());
        Assert.NotEmpty(earlier["counts"]!.AsArray());
        Assert.Equal(("notScored", true), ((string?)earlier["noScoreReason"], earlier["score"] is null));

        await using var events = await EventStreamReader.ConnectAsync(_sidecar.Client);
        Assert.Equal(HttpStatusCode.Accepted, (await _sidecar.PostAsync($"api/crawls/{id}/analyze")).Status);
        await events.CrawlStatusAsync(id, "analyzing");
        Assert.Null((await events.CrawlStatusAsync(id, "completed"))["error"]);

        Assert.False((bool?)(await _sidecar.GetJsonAsync($"api/crawls/{id}"))["outOfDate"]);
        var analysis = await _sidecar.GetJsonAsync($"api/crawls/{id}/analysis");
        Assert.True((bool?)analysis["isPartial"]);
        Assert.False((bool?)analysis["outOfDate"]);
        Assert.Empty(analysis["outOfDateReasons"]!.AsArray());
        var orphans = analysis["skipped"]!.AsArray().Single(skip => (string?)skip!["checkId"] == IssueCatalog.ORPHAN_PAGE.Id)!;
        Assert.Equal(SkippedCheckReasons.URL_LIMIT, (string?)orphans["reason"]!["code"]);
        Assert.NotNull(analysis["score"]);
        Assert.Null(analysis["noScoreReason"]);
    }

    [Fact]
    public async Task ACrawlThatChangedAfterItsAnalysisIsOutOfDate()
    {
        var path = await TestCrawlFiles.CrawlAsync(_site, Path.Combine(_sidecar.DataDirectory, "changed.sicrawl"), analyse: true);
        var id = CrawlIds.For(path);
        Assert.False((bool?)(await _sidecar.GetJsonAsync($"api/crawls/{id}"))["outOfDate"]);

        TestCrawlFiles.StampAsCrawledAgain(path);

        Assert.True((bool?)(await _sidecar.GetJsonAsync($"api/crawls/{id}"))["outOfDate"]);
        var analysis = await _sidecar.GetJsonAsync($"api/crawls/{id}/analysis");
        Assert.True((bool?)analysis["outOfDate"]);
        Assert.Equal(["crawlChanged"], analysis["outOfDateReasons"]!.AsArray().Select(reason => (string?)reason));
    }

    /// <summary>
    /// Someone part-way through reading a closed crawl (an export, another program) keeps an analysis out: it ends within
    /// seconds, its error a crawl-busy problem the UI can explain and offer to retry, and the results are unchanged.
    /// </summary>
    [Fact]
    public async Task AnAnalysisThatFindsTheFileBeingReadEndsAsBusy()
    {
        var path = await TestCrawlFiles.CrawlAsync(_site, Path.Combine(_sidecar.DataDirectory, "read.sicrawl"), analyse: true);
        var id = CrawlIds.For(path);
        var before = (await _sidecar.GetJsonAsync($"api/crawls/{id}/analysis")).ToJsonString();

        await using var events = await EventStreamReader.ConnectAsync(_sidecar.Client);
        using (var reading = IssueReader.ReadAll(path).GetEnumerator())
        {
            Assert.True(reading.MoveNext());
            Assert.Equal(HttpStatusCode.Accepted, (await _sidecar.PostAsync($"api/crawls/{id}/analyze")).Status);
            await events.CrawlStatusAsync(id, "analyzing");
            var ended = await events.CrawlStatusAsync(id, "completed");
            Assert.Equal("crawl-busy", (string?)ended["errorType"]);
            Assert.Contains("being read", (string?)ended["error"], StringComparison.Ordinal);
        }

        Assert.Equal(before, (await _sidecar.GetJsonAsync($"api/crawls/{id}/analysis")).ToJsonString());
    }

    [Fact]
    public async Task ACrawlNeverAnalysedIsNotOutOfDate()
    {
        var path = await TestCrawlFiles.CrawlAsync(_site, Path.Combine(_sidecar.DataDirectory, "fresh.sicrawl"));
        var id = CrawlIds.For(path);

        var item = await _sidecar.GetJsonAsync($"api/crawls/{id}");
        Assert.Null(item["analyzedAt"]);
        Assert.False((bool?)item["outOfDate"]);
        TestSidecar.AssertProblem(await _sidecar.SendAsync(HttpMethod.Get, $"api/crawls/{id}/analysis"), HttpStatusCode.NotFound, "not-analyzed");
    }
}
