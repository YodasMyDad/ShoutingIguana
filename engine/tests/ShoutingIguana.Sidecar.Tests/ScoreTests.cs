using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Sidecar.Library;
using ShoutingIguana.Sidecar.Tests.Support;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Sidecar.Tests;

/// <summary>
/// The score as the API serves it: the analysis's own, as stored, on the analysis and (its headline) on the library item,
/// with the totals it was worked out from and the order to fix the checks in; or null, with why.
/// </summary>
public sealed class ScoreTests : IAsyncLifetime
{
    private SyntheticSiteServer _site = null!;
    private TestSidecar _sidecar = null!;

    public async Task InitializeAsync()
    {
        // Without sitemaps, so two checks are skipped for a reason that has plain words.
        _site = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 60, Sitemaps = false });
        _sidecar = await TestSidecar.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _sidecar.DisposeAsync();
        await _site.DisposeAsync();
    }

    [Fact]
    public async Task TheAnalysisServesTheScoreItStoredAndTheLibraryItsHeadline()
    {
        var path = await TestCrawlFiles.CrawlAsync(_site, Path.Combine(_sidecar.DataDirectory, "scored.sicrawl"), analyse: true,
            options: new CrawlOptions { StartUrl = _site.BaseUri.AbsoluteUri, Concurrency = 8 });
        var id = CrawlIds.For(path);
        var stored = IssueReader.ReadResults(path)!;
        var expected = stored.Score!;

        var analysis = await _sidecar.GetJsonAsync($"api/crawls/{id}/analysis");

        // The whole score, by the contract's names: camelCase, enums as camelCase strings.
        var score = analysis["score"]!;
        Assert.Equal((1, expected.Value, Camel(expected.Band), expected.Lost, expected.BeforeCeiling, expected.HeldBySerious, false),
            ((int)score["version"]!, (int)score["value"]!, (string)score["band"]!, (int)score["lost"]!, (int)score["beforeCeiling"]!, (bool)score["heldBySerious"]!,
                (bool)score["partial"]!));
        Assert.Equal(JsonValueKind.Null, analysis["noScoreReason"]?.GetValueKind() ?? JsonValueKind.Null);
        Assert.True(analysis.AsObject().ContainsKey("noScoreReason"));

        var covers = score["covers"]!;
        Assert.Equal((expected.Covers.Pages, expected.Covers.Urls, expected.Covers.ChecksRun, expected.Covers.ChecksSkipped, true, expected.Covers.BlockedByRobots),
            ((long)covers["pages"]!, (long)covers["urls"]!, (int)covers["checksRun"]!, (int)covers["checksSkipped"]!, (bool)covers["complete"]!, (long)covers["blockedByRobots"]!));
        Assert.True(covers.AsObject().ContainsKey("reason") && covers["reason"] is null);
        Assert.Equal(2, expected.Covers.ChecksSkipped);

        Assert.Equal(expected.Lines.Select(line => (line.CheckId, Camel(line.Severity), line.Count, line.Of, Camel(line.Unit), Camel(line.Extent), line.Points)),
            score["lines"]!.AsArray().Select(line => ((string)line!["checkId"]!, (string)line["severity"]!, (long)line["count"]!, (long)line["of"]!, (string)line["unit"]!,
                (string)line["extent"]!, (int)line["points"]!)));
        Assert.Contains(score["lines"]!.AsArray(), line => (string)line!["checkId"]! == "no-https" && (string)line["unit"]! == "site" && (string)line["extent"]! == "site");
        Assert.Contains(score["lines"]!.AsArray(), line => (string)line!["unit"]! == "pages");
        Assert.Contains(score["lines"]!.AsArray(), line => (string)line!["unit"]! == "urls");
        Assert.Equal(expected.IfFixed.CheckIds, score["ifFixed"]!["checkIds"]!.AsArray().Select(check => (string)check!));
        Assert.Equal(expected.IfFixed.Value, (int)score["ifFixed"]!["value"]!);

        // The totals the score goes by are the analysis's, beside it, and so is the order to fix the checks in.
        Assert.Equal((stored.Analysis.PagesAnalyzed, stored.Analysis.CrawledUrlCount), ((long)analysis["pagesAnalyzed"]!, (long?)analysis["crawledUrlCount"]));
        Assert.Equal((long)covers["urls"]!, (long)analysis["crawledUrlCount"]!);
        Assert.Equal(stored.FixFirst, analysis["fixFirst"]!.AsArray().Select(check => (string)check!));
        Assert.Equal(score["lines"]!.AsArray().Select(line => (string)line!["checkId"]!), analysis["fixFirst"]!.AsArray().Select(check => (string)check!));
        Assert.NotEmpty(stored.FixFirst);

        // The library item has the headline, and its count of URLs crawled is the one the score was worked out from.
        var item = await _sidecar.GetJsonAsync($"api/crawls/{id}");
        Assert.Equal($$"""{"value":{{expected.Value}},"band":"{{Camel(expected.Band)}}","partial":false}""", item["score"]!.ToJsonString());
        Assert.Equal((long)covers["urls"]!, (long)item["crawledUrlCount"]!);
        Assert.Equal(item["score"]!.ToJsonString(), (await _sidecar.GetJsonAsync("api/crawls")).AsArray().Single(listed => (string)listed!["id"]! == id)!["score"]!.ToJsonString());

        // A skip reason comes with its plain words where its code has them: why, and what would let the check run.
        var orphans = analysis["skipped"]!.AsArray().Single(skip => (string)skip!["checkId"]! == IssueCatalog.ORPHAN_PAGE.Id)!["reason"]!;
        Assert.Equal((SkippedCheckReasons.NO_SITEMAP, "No sitemap could be read.", "Your site has no sitemap, the list of its pages for search engines, to compare with.",
                "Add a sitemap, then check the site again."),
            ((string)orphans["code"]!, (string)orphans["text"]!, (string)orphans["plainText"]!, (string)orphans["plainAction"]!));
    }

    /// <summary>
    /// The analysis keeps the totals it was worked out from when the crawl changes after it: the library counts the crawl
    /// as it is now, the analysis the URLs crawled when it ran, and the score's lines are shares of those.
    /// </summary>
    [Fact]
    public async Task TheAnalysisKeepsItsOwnTotalsWhenTheCrawlChangesAfterIt()
    {
        var path = await TestCrawlFiles.CrawlAsync(_site, Path.Combine(_sidecar.DataDirectory, "changed.sicrawl"), maxUrls: 30, analyse: true);
        var id = CrawlIds.For(path);
        var before = await _sidecar.GetJsonAsync($"api/crawls/{id}/analysis");
        var crawled = (long)before["crawledUrlCount"]!;
        Assert.Equal(crawled, (long)(await _sidecar.GetJsonAsync($"api/crawls/{id}"))["crawledUrlCount"]!);

        // The crawl goes on: one more URL crawled, and the crawl stamped again.
        TestCrawlFiles.AddRefusedByRobots(path, new Uri(_site.BaseUri, "/private/later").AbsoluteUri);

        var item = await _sidecar.GetJsonAsync($"api/crawls/{id}");
        Assert.Equal((crawled + 1, true), ((long)item["crawledUrlCount"]!, (bool)item["outOfDate"]!));
        var after = await _sidecar.GetJsonAsync($"api/crawls/{id}/analysis");
        Assert.Equal(["crawlChanged"], after["outOfDateReasons"]!.AsArray().Select(reason => (string)reason!));
        Assert.Equal((crawled, crawled), ((long)after["crawledUrlCount"]!, (long)after["score"]!["covers"]!["urls"]!));
        Assert.Equal(before["score"]!.ToJsonString(), after["score"]!.ToJsonString());
        Assert.Equal(before["score"]!["value"]!.ToJsonString(), item["score"]!["value"]!.ToJsonString());

        // The limit kept part of the site out: the score is partial, and says which limit.
        Assert.Equal((true, false, "urlLimit"), ((bool)after["score"]!["partial"]!, (bool)after["score"]!["covers"]!["complete"]!, (string)after["score"]!["covers"]!["reason"]!));
        Assert.True((bool)item["score"]!["partial"]!);

        // A reason whose code has no plain words yet says null for them, never the engine's sentence in their place.
        var orphans = after["skipped"]!.AsArray().Single(skip => (string)skip!["checkId"]! == IssueCatalog.ORPHAN_PAGE.Id)!["reason"]!.AsObject();
        Assert.Equal(SkippedCheckReasons.URL_LIMIT, (string)orphans["code"]!);
        Assert.True(orphans.ContainsKey("plainText") && orphans["plainText"] is null && orphans.ContainsKey("plainAction") && orphans["plainAction"] is null);
    }

    /// <summary>A crawl stopped before it finished gives a partial score, "stopped", in the analysis and on the library item.</summary>
    [Fact]
    public async Task AStoppedCrawlsScoreIsPartial()
    {
        var path = Path.Combine(_sidecar.DataDirectory, "stopped.sicrawl");
        Directory.CreateDirectory(_sidecar.DataDirectory);
        await using (var job = new CrawlEngine(NullLoggerFactory.Instance).Start(
                         new CrawlRequest(new CrawlOptions { StartUrl = _site.BaseUri.AbsoluteUri, Concurrency = 2, MaxRequestsPerSecond = 100 }, path)))
        {
            var deadline = DateTime.UtcNow + EventStreamReader.TIMEOUT;
            while (job.Progress.Fetched < 40 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(5);
            }

            Assert.Equal(CrawlStatus.Stopped, (await job.StopAsync()).Status);
        }

        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        var id = CrawlIds.For(path);

        var score = (await _sidecar.GetJsonAsync($"api/crawls/{id}/analysis"))["score"]!;
        Assert.Equal((true, false, "stopped"), ((bool)score["partial"]!, (bool)score["covers"]!["complete"]!, (string)score["covers"]!["reason"]!));
        var item = await _sidecar.GetJsonAsync($"api/crawls/{id}");
        Assert.Equal(("stopped", true), ((string)item["status"]!, (bool)item["score"]!["partial"]!));
    }

    /// <summary>No page read is no score, with its reason; a crawl never analysed has no analysis to ask, and no score in the library.</summary>
    [Fact]
    public async Task WithoutAScoreTheAnalysisSaysWhyAndTheLibrarySaysNull()
    {
        var nowhere = await TestCrawlFiles.CrawlAsync(_site, Path.Combine(_sidecar.DataDirectory, "nowhere.sicrawl"), analyse: true,
            options: new CrawlOptions { StartUrl = new Uri(_site.BaseUri, "/broken/1").AbsoluteUri, DiscoverSitemaps = false });
        var analysis = (await _sidecar.GetJsonAsync($"api/crawls/{CrawlIds.For(nowhere)}/analysis")).AsObject();
        Assert.Equal((0L, "noPages"), ((long)analysis["pagesAnalyzed"]!, (string)analysis["noScoreReason"]!));
        Assert.True(analysis.ContainsKey("score") && analysis["score"] is null);
        Assert.True((long)analysis["crawledUrlCount"]! > 0);
        Assert.False((bool)analysis["outOfDate"]!);
        Assert.Equal([IssueCatalog.INTERNAL_CLIENT_ERROR.Id], analysis["fixFirst"]!.AsArray().Select(check => (string)check!).Take(1));
        var item = (await _sidecar.GetJsonAsync($"api/crawls/{CrawlIds.For(nowhere)}")).AsObject();
        Assert.True(item.ContainsKey("score") && item["score"] is null);

        var fresh = await TestCrawlFiles.CrawlAsync(_site, Path.Combine(_sidecar.DataDirectory, "fresh.sicrawl"));
        var unanalysed = (await _sidecar.GetJsonAsync($"api/crawls/{CrawlIds.For(fresh)}")).AsObject();
        Assert.True(unanalysed.ContainsKey("score") && unanalysed["score"] is null);
        TestSidecar.AssertProblem(await _sidecar.SendAsync(HttpMethod.Get, $"api/crawls/{CrawlIds.For(fresh)}/analysis"), HttpStatusCode.NotFound, "not-analyzed");
    }

    /// <summary>
    /// An analysis by an engine from before the score has none, and says so: it is out of date, and what it found still
    /// has its order to fix. A score by another version of the formula is served as stored, and the analysis is out of date.
    /// </summary>
    [Fact]
    public async Task AnAnalysisFromBeforeTheScoreOrByAnotherFormulaIsOutOfDate()
    {
        var earlier = await TestCrawlFiles.CrawlAsync(_site, Path.Combine(_sidecar.DataDirectory, "earlier.sicrawl"), analyse: true);
        TestCrawlFiles.AsAnalysedByAnEarlierEngine(earlier);
        var analysis = (await _sidecar.GetJsonAsync($"api/crawls/{CrawlIds.For(earlier)}/analysis")).AsObject();
        Assert.Equal(("notScored", true), ((string)analysis["noScoreReason"]!, (bool)analysis["outOfDate"]!));
        Assert.True(analysis["score"] is null && analysis["crawledUrlCount"] is null);
        Assert.NotEmpty(analysis["fixFirst"]!.AsArray());
        Assert.Null((await _sidecar.GetJsonAsync($"api/crawls/{CrawlIds.For(earlier)}"))["score"]);

        var other = await TestCrawlFiles.CrawlAsync(_site, Path.Combine(_sidecar.DataDirectory, "other-formula.sicrawl"), analyse: true);
        var value = IssueReader.ReadResults(other)!.Score!.Value;
        TestCrawlFiles.ScoredByAnotherVersion(other);
        var scored = await _sidecar.GetJsonAsync($"api/crawls/{CrawlIds.For(other)}/analysis");
        Assert.Equal(["engineChanged"], scored["outOfDateReasons"]!.AsArray().Select(reason => (string)reason!));
        Assert.Equal(value, (int)scored["score"]!["value"]!);
        var item = await _sidecar.GetJsonAsync($"api/crawls/{CrawlIds.For(other)}");
        Assert.Equal((true, value), ((bool)item["outOfDate"]!, (int)item["score"]!["value"]!));
    }

    private static string Camel<TEnum>(TEnum value)
        where TEnum : struct, Enum => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
}
