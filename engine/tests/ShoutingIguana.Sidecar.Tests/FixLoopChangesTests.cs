using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Sidecar.Library;
using ShoutingIguana.Sidecar.Tests.Support;

namespace ShoutingIguana.Sidecar.Tests;

/// <summary>
/// The fix loop's comparison in the sidecar: each crawl's site key, the previous crawl it is compared with by default, and
/// its changes since, worked out from the library's cached summaries without opening a file.
/// </summary>
public sealed class FixLoopChangesTests(FixLoopCrawls crawls) : IClassFixture<FixLoopCrawls>, IAsyncLifetime
{
    private TestSidecar _sidecar = null!;

    public async Task InitializeAsync() => _sidecar = await TestSidecar.StartAsync();

    public async Task DisposeAsync() => await _sidecar.DisposeAsync();

    /// <summary>The app's grouping (<c>sites.ts</c>, <c>siteGroups</c>) on its own fixtures: the host without www., and a port of its own.</summary>
    [Theory]
    [InlineData("http://127.0.0.1:61877/", "127.0.0.1:61877")]
    [InlineData("http://127.0.0.1:61877/shop", "127.0.0.1:61877")]
    [InlineData("http://127.0.0.1:61878/", "127.0.0.1:61878")]
    [InlineData("http://example.com:80/", "example.com")]
    [InlineData("https://example.com/", "example.com")]
    [InlineData("https://www.example.com/", "example.com")]
    [InlineData("https://WWW.Example.com:443/blog/", "example.com")]
    [InlineData("http://example.com:443/", "example.com:443")]
    [InlineData("https://shop.example.com:8443/", "shop.example.com:8443")]
    [InlineData("not a URL", null)]
    [InlineData(null, null)]
    public void TheSiteKeyIsTheAppsGrouping(string? startUrl, string? siteKey) => Assert.Equal(siteKey, PreviousCrawls.SiteKeyOf(startUrl));

    /// <summary>
    /// The previous crawl is the newest earlier crawl of the site that can be compared, one with the same start URL first:
    /// never one not analysed, interrupted, removed from the library or deleted, and a site's first crawl has none. Two
    /// started at the same moment are in Home's order, by id.
    /// </summary>
    [Fact]
    public async Task ThePreviousCrawlIsTheNewestEarlierOneOfTheSiteTheSameStartUrlFirst()
    {
        var first = Id(FixLoopCrawls.AddTo(_sidecar, crawls.Then, "first"));
        var blog = Id(FixLoopCrawls.AddTo(_sidecar, crawls.Then, "blog", 10, path => TestCrawlFiles.SetStartUrl(path, crawls.Url("/blog/"))));
        var fixes = Id(FixLoopCrawls.AddTo(_sidecar, crawls.Now, "fixes", 20));
        Id(FixLoopCrawls.AddTo(_sidecar, crawls.Then, "interrupted", 25, path => TestCrawlFiles.SetStoredStatus(path, CrawlStatus.Running)));
        var unanalysed = Id(FixLoopCrawls.AddTo(_sidecar, crawls.Now, "unanalysed", 26, TestCrawlFiles.DropAnalysis));
        var latest = Id(FixLoopCrawls.AddTo(_sidecar, crawls.Now, "latest", 30));
        var other = Id(FixLoopCrawls.AddTo(_sidecar, crawls.Then, "other", 5, path => TestCrawlFiles.SetStartUrl(path, crawls.Url("/").Replace("127.0.0.1", "localhost"))));

        var items = await ListAsync();
        Assert.Equal($"127.0.0.1:{crawls.Server.BaseUri.Port}", (string?)items[first]["siteKey"]);
        Assert.Equal($"localhost:{crawls.Server.BaseUri.Port}", (string?)items[other]["siteKey"]);

        // A site's first crawl, and the only crawl of the other site, have none; nor has a crawl never analysed.
        Assert.Null(items[first]["previous"]);
        Assert.Null(items[first]["changes"]);
        Assert.Null(items[other]["previous"]);
        Assert.Null(items[unanalysed]["previous"]);

        // Another start URL is the site's crawl when no crawl from its own start URL came before it.
        Assert.Equal(first, Previous(items, blog));

        // The same start URL comes first, over a newer crawl of the site from another; the interrupted and the unanalysed
        // crawls are never the previous one.
        Assert.Equal(first, Previous(items, fixes));
        Assert.Equal(fixes, Previous(items, latest));

        // One removed from the library isn't a candidate: the next older one is. Nor is a file deleted.
        Assert.Equal(HttpStatusCode.NoContent, (await _sidecar.SendAsync(HttpMethod.Delete, $"api/crawls/{fixes}")).Status);
        Assert.Equal(first, Previous(await ListAsync(), latest));
        File.Delete(Path.Combine(_sidecar.DataDirectory, "first.sicrawl"));
        Assert.Equal(blog, Previous(await ListAsync(), latest));
        Assert.Equal(blog, (string?)(await _sidecar.GetJsonAsync($"api/crawls/{latest}"))["previous"]!["id"]);

        // Two crawls that started at the same moment: the one with the larger id is the earlier, as Home lists them.
        var twin = Id(FixLoopCrawls.AddTo(_sidecar, crawls.Now, "twin", 30));
        var (earlier, later) = string.CompareOrdinal(twin, latest) > 0 ? (twin, latest) : (latest, twin);
        items = await ListAsync();
        Assert.Equal(earlier, Previous(items, later));
        Assert.Equal(blog, Previous(items, earlier));
    }

    /// <summary>
    /// A crawl's previous crawl and its changes since come from the library's cached summaries: once the library has read
    /// each file, listing it (or getting one crawl) reads none, and the changes are the engine's comparison of the two
    /// summaries.
    /// </summary>
    [Fact]
    public async Task TheChangesComeFromTheCachedSummariesWithoutReadingAFile()
    {
        var then = FixLoopCrawls.AddTo(_sidecar, crawls.Then, "then");
        var now = FixLoopCrawls.AddTo(_sidecar, crawls.Now, "now", 10);
        await ListAsync();

        List<string> read = [];
        var cache = _sidecar.Host.Services.GetRequiredService<CrawlSummaryCache>();
        cache.Reading = read.Add;
        try
        {
            var item = (await ListAsync())[Id(now)];
            var single = await _sidecar.GetJsonAsync($"api/crawls/{Id(now)}");
            Assert.Empty(read);

            var expected = CrawlComparability.FromSummaries(CrawlSummaryReader.Read(then), CrawlSummaryReader.Read(now))!;
            var previous = item["previous"]!;
            Assert.Equal(Id(then), (string?)previous["id"]);
            Assert.Equal(CrawlSummaryReader.Read(then).StartedAt.UtcDateTime, (DateTime)previous["startedAt"]!);
            Assert.Equal((int)IssueReader.ReadAnalysis(then)!.Score!.Value, (int)previous["score"]!["value"]!);
            Assert.Equal("full", (string?)previous["comparable"]);
            Assert.False((bool)previous["needsAnalysis"]!);

            var changes = item["changes"]!;
            Assert.Equal(expected.ScoreChange, (int?)changes["score"]);
            Assert.NotNull(expected.ScoreChange);
            Assert.False((bool)changes["partial"]!);
            foreach (var state in Enum.GetValues<CheckChangeState>().Where(state => state != CheckChangeState.Same))
            {
                Assert.Equal(expected.Checks.Where(check => check.State == state).Select(check => check.CheckId),
                    changes[JsonNamingPolicy.CamelCase.ConvertName(state.ToString())]!.AsArray().Select(check => (string)check!));
            }

            // Revision 1 fixes some checks outright, and finds one that found nothing before.
            Assert.NotEmpty(changes["fixed"]!.AsArray());
            Assert.Contains("security-headers-missing", changes["new"]!.AsArray().Select(check => (string)check!));
            Assert.Equal(item.ToJsonString(), single.ToJsonString());
        }
        finally
        {
            cache.Reading = null;
        }
    }

    /// <summary>The previous crawl analysed by an earlier engine: counts only, saying it needs analysing again; analysed, like for like.</summary>
    [Fact]
    public async Task APreviousCrawlAnalysedByOtherRulesNeedsAnalysing()
    {
        var then = FixLoopCrawls.AddTo(_sidecar, crawls.Then, "then", change: TestCrawlFiles.ScoredByAnotherVersion);
        var now = Id(FixLoopCrawls.AddTo(_sidecar, crawls.Now, "now", 10));

        var item = (await ListAsync())[now];
        Assert.Equal(("countsOnly", true), ((string?)item["previous"]!["comparable"], (bool?)item["previous"]!["needsAnalysis"]));
        Assert.Null((int?)item["changes"]!["score"]);

        await AnalyseAsync(Id(then));
        item = (await ListAsync())[now];
        Assert.Equal(("full", false), ((string?)item["previous"]!["comparable"], (bool?)item["previous"]!["needsAnalysis"]));
        Assert.NotNull((int?)item["changes"]!["score"]);
    }

    /// <summary><c>/changes</c> is the engine's comparison of the crawl with its previous crawl, field for field.</summary>
    [Fact]
    public async Task TheChangesAreTheEnginesComparison()
    {
        var then = FixLoopCrawls.AddTo(_sidecar, crawls.Then, "then");
        var now = FixLoopCrawls.AddTo(_sidecar, crawls.Now, "now", 10);
        var expected = CrawlComparisonReader.ReadSummary(now, then)!;

        var changes = await _sidecar.GetJsonAsync($"api/crawls/{Id(now)}/changes");
        Assert.Equal(Id(now), (string?)changes["crawlId"]);
        var with = changes["with"]!;
        var analysis = IssueReader.ReadAnalysis(then)!;
        Assert.Equal((Id(then), "previous", "completed", crawls.Server.BaseUri.AbsoluteUri, analysis.PagesAnalyzed),
            ((string?)with["id"], (string?)with["chosen"], (string?)with["status"], (string?)with["startUrl"], (long)with["pagesAnalyzed"]!));
        Assert.Equal(analysis.AnalyzedAt.UtcDateTime, (DateTime)with["analyzedAt"]!);
        Assert.NotNull(with["finishedAt"]);
        Assert.Equal(CrawlSummaryReader.Read(then).CrawledUrlCount, (long?)with["crawledUrlCount"]);
        Assert.Equal(("full", 0, 0), ((string?)changes["comparable"], changes["differences"]!.AsArray().Count, changes["previousNeedsAnalysis"]!.AsArray().Count));

        var score = changes["score"]!;
        Assert.Equal((expected.Score!.Then.Value, expected.Score.Now.Value, expected.Score.Change),
            ((int)score["then"]!["value"]!, (int)score["now"]!["value"]!, (int?)score["change"]));
        Assert.Equal(expected.Score.Lines.Select(line => (line.CheckId, line.Then, line.Now)),
            score["lines"]!.AsArray().Select(line => ((string)line!["checkId"]!, (int)line["then"]!, (int)line["now"]!)));

        foreach (var state in Enum.GetValues<CheckChangeState>())
        {
            Assert.Equal(expected.Totals.Checks[state], (int)changes["totals"]!["checks"]![Camel(state)!]!);
        }

        foreach (var state in Enum.GetValues<RowChangeState>())
        {
            Assert.Equal(expected.Totals.Issues!.Count(state), (long)changes["totals"]!["issues"]![Camel(state)!]!);
        }

        Assert.Equal(expected.Checks.Select(check => string.Join(" ", check.CheckId, Camel(check.State), Camel(check.Severity), Camel(check.SeverityThen), check.Then, check.Now,
                string.Join(" ", Enum.GetValues<RowChangeState>().Select(state => check.Rows!.Count(state))))),
            changes["checks"]!.AsArray().Select(check => string.Join(" ", (string)check!["checkId"]!, (string)check["state"]!, (string?)check["severity"], (string?)check["severityThen"],
                (long?)check["then"], (long?)check["now"], string.Join(" ", Enum.GetValues<RowChangeState>().Select(state => (long)check[Camel(state)!]!)))));

        // The same with=previous; named, the comparison is "picked", and a later crawl can be named too.
        Assert.Equal(changes.ToJsonString(), (await _sidecar.GetJsonAsync($"api/crawls/{Id(now)}/changes?with=previous")).ToJsonString());
        Assert.Equal("picked", (string?)(await _sidecar.GetJsonAsync($"api/crawls/{Id(now)}/changes?with={Id(then)}"))["with"]!["chosen"]);
        var until = await _sidecar.GetJsonAsync($"api/crawls/{Id(then)}/changes?with={Id(now)}");
        Assert.Equal((Id(now), "picked", "full"), ((string?)until["with"]!["id"], (string?)until["with"]!["chosen"], (string?)until["comparable"]));
    }

    /// <summary>
    /// A site's first crawl has nothing to compare: <c>with</c> null and nothing else. A crawl that can't be compared is
    /// refused, and so is a crawl never analysed.
    /// </summary>
    [Fact]
    public async Task AFirstCrawlHasNoChangesAndACrawlThatCantBeComparedIsRefused()
    {
        var then = Id(FixLoopCrawls.AddTo(_sidecar, crawls.Then, "then"));
        var now = Id(FixLoopCrawls.AddTo(_sidecar, crawls.Now, "now", 10));
        var unanalysed = Id(FixLoopCrawls.AddTo(_sidecar, crawls.Now, "unanalysed", 20, TestCrawlFiles.DropAnalysis));
        var other = Id(FixLoopCrawls.AddTo(_sidecar, crawls.Then, "other", 5, path => TestCrawlFiles.SetStartUrl(path, crawls.Url("/").Replace("127.0.0.1", "localhost"))));

        var first = (await _sidecar.GetJsonAsync($"api/crawls/{then}/changes")).AsObject();
        Assert.Equal(["crawlId", "with"], first.Select(property => property.Key));
        Assert.Null(first["with"]);

        TestSidecar.AssertProblem(await _sidecar.SendAsync(HttpMethod.Get, $"api/crawls/{now}/changes?with={now}"), HttpStatusCode.Conflict, "crawl-not-comparable");
        TestSidecar.AssertProblem(await _sidecar.SendAsync(HttpMethod.Get, $"api/crawls/{now}/changes?with={other}"), HttpStatusCode.Conflict, "crawl-not-comparable");
        TestSidecar.AssertProblem(await _sidecar.SendAsync(HttpMethod.Get, $"api/crawls/{now}/changes?with={unanalysed}"), HttpStatusCode.Conflict, "crawl-not-comparable");
        TestSidecar.AssertProblem(await _sidecar.SendAsync(HttpMethod.Get, $"api/crawls/{now}/changes?with=0123456789abcdef"), HttpStatusCode.NotFound, "crawl-not-found");
        TestSidecar.AssertProblem(await _sidecar.SendAsync(HttpMethod.Get, $"api/crawls/{now}/changes?with=previous&with=previous"), HttpStatusCode.BadRequest, "bad-request");
        TestSidecar.AssertProblem(await _sidecar.SendAsync(HttpMethod.Get, $"api/crawls/{unanalysed}/changes"), HttpStatusCode.NotFound, "not-analyzed");
    }

    /// <summary>
    /// What differs is said by its code, with its field, side and values where it has them: a URL limit none then; and the
    /// previous crawl scored by another version, which is counts only until it's analysed again (no row split, no score change).
    /// </summary>
    [Fact]
    public async Task TheDifferencesAreSaidAndCountsOnlyHasNoRows()
    {
        FixLoopCrawls.AddTo(_sidecar, crawls.Then, "then");
        var limited = Id(FixLoopCrawls.AddTo(_sidecar, crawls.Now, "limited", 10, path => TestCrawlFiles.SetMaxUrls(path, 5_000)));
        var changes = await _sidecar.GetJsonAsync($"api/crawls/{limited}/changes");
        Assert.Equal("partial", (string?)changes["comparable"]);
        Assert.Equal("""[{"code":"limits","field":"maxUrls","then":null,"now":5000}]""", changes["differences"]!.ToJsonString());

        await using var counted = await TestSidecar.StartAsync();
        FixLoopCrawls.AddTo(counted, crawls.Then, "then", change: TestCrawlFiles.ScoredByAnotherVersion);
        var now = Id(FixLoopCrawls.AddTo(counted, crawls.Now, "now", 10));
        changes = await counted.GetJsonAsync($"api/crawls/{now}/changes");
        Assert.Equal(("countsOnly", """["engineChanged"]"""), ((string?)changes["comparable"], changes["previousNeedsAnalysis"]!.ToJsonString()));
        var rules = Assert.Single(changes["differences"]!.AsArray())!;
        Assert.Equal(("rules", "scoreVersion"), ((string?)rules["code"], (string?)rules["field"]));
        Assert.Equal((int)rules["now"]! + 1, (int)rules["then"]!);
        Assert.Null(changes["score"]!["change"]);
        Assert.Null(changes["totals"]!["issues"]);
        Assert.All(changes["checks"]!.AsArray(), check => Assert.All(Enum.GetValues<RowChangeState>(), state => Assert.Null(check![Camel(state)!])));

        // Its rows and groups are compared by nothing: each row's change is null, no group has one, and nothing is cleared.
        var rows = (await counted.PostAsync($"api/crawls/{now}/issues/title-too-long/rows", new JsonObject { ["compareWith"] = "previous" })).Body!;
        Assert.Equal("countsOnly", (string?)rows["compared"]!["comparable"]);
        Assert.All(rows["rows"]!.AsArray(), row => Assert.True(row!.AsObject().ContainsKey("change") && row["change"] is null));
        var groups = (await counted.PostAsync($"api/crawls/{now}/issues/broken-internal-links/groups", new JsonObject { ["compareWith"] = "previous" })).Body!;
        Assert.Equal(("countsOnly", (long?)null), ((string?)groups["compared"]!["comparable"], (long?)groups["compared"]!["fixedGroups"]));
        Assert.All(groups["groups"]!.AsArray(), group => Assert.False(group!.AsObject().ContainsKey("change")));
        Assert.Null((await counted.PostAsync($"api/crawls/{now}/issues/broken-internal-links/fixed")).Body!["total"]);
    }

    /// <summary>
    /// <c>compareWith</c> gives each of a check's groups and rows its change since the other crawl, as the engine reads it,
    /// and the page what it was compared with; without it they are as they were. Only a check's rows and groups take it.
    /// </summary>
    [Fact]
    public async Task ACheckGroupsAndRowsAreComparedWithAnotherCrawl()
    {
        var then = FixLoopCrawls.AddTo(_sidecar, crawls.Then, "then");
        var now = FixLoopCrawls.AddTo(_sidecar, crawls.Now, "now", 10);
        var other = Id(FixLoopCrawls.AddTo(_sidecar, crawls.Then, "other", 5, path => TestCrawlFiles.SetStartUrl(path, crawls.Url("/").Replace("127.0.0.1", "localhost"))));
        var startedAt = CrawlSummaryReader.Read(then).StartedAt.UtcDateTime;

        const string BROKEN_LINKS = "broken-internal-links";
        var groups = (await _sidecar.PostAsync($"api/crawls/{Id(now)}/issues/{BROKEN_LINKS}/groups", new JsonObject { ["compareWith"] = "previous" })).Body!;
        var expectedGroups = IssueGroupReader.ReadGroups(now, BROKEN_LINKS, 0, UrlGridReader.DEFAULT_LIMIT, null, then)!;
        Assert.Equal((Id(then), startedAt, "full", expectedGroups.Compared!.FixedGroups),
            ((string?)groups["compared"]!["with"], (DateTime)groups["compared"]!["startedAt"]!, (string?)groups["compared"]!["comparable"], (long?)groups["compared"]!["fixedGroups"]));
        Assert.True(expectedGroups.Compared.FixedGroups > 0);
        Assert.Equal(expectedGroups.Groups.Select(group => $"{group.RelatedUrl} {Camel(group.Change!.State)} {group.Change.Then}"),
            groups["groups"]!.AsArray().Select(group => $"{(string?)group!["relatedUrl"]} {(string?)group["change"]!["state"]} {(long)group["change"]!["then"]!}"));
        var plain = (await _sidecar.PostAsync($"api/crawls/{Id(now)}/issues/{BROKEN_LINKS}/groups", new JsonObject())).Body!.AsObject();
        Assert.False(plain.ContainsKey("compared"));
        Assert.All(plain["groups"]!.AsArray(), group => Assert.False(group!.AsObject().ContainsKey("change")));

        const string TITLE_TOO_LONG = "title-too-long";
        var query = new UrlQuery { Columns = ["url"] };
        var rows = (await _sidecar.PostAsync($"api/crawls/{Id(now)}/issues/{TITLE_TOO_LONG}/rows", new JsonObject { ["columns"] = new JsonArray("url"), ["compareWith"] = Id(then) })).Body!;
        var expectedRows = IssueGroupReader.ReadRows(now, TITLE_TOO_LONG, query, null, then)!;
        Assert.Equal((Id(then), "full"), ((string?)rows["compared"]!["with"], (string?)rows["compared"]!["comparable"]));
        Assert.Equal(expectedRows.Page!.Rows.Select((row, index) => $"{row.Values[1]} {Camel(expectedRows.Changes![index]!.State)} {expectedRows.Changes[index]!.DetailThen}"),
            rows["rows"]!.AsArray().Select(row => $"{(string?)row!["url"]} {(string?)row["change"]!["state"]} {(string?)row["change"]!["detailThen"]}"));
        Assert.Contains(rows["rows"]!.AsArray(), row => (string?)row!["change"]!["state"] == "still");
        var plainRows = (await _sidecar.PostAsync($"api/crawls/{Id(now)}/issues/{TITLE_TOO_LONG}/rows", new JsonObject { ["columns"] = new JsonArray("url") })).Body!.AsObject();
        Assert.False(plainRows.ContainsKey("compared"));
        Assert.All(plainRows["rows"]!.AsArray(), row => Assert.False(row!.AsObject().ContainsKey("change")));

        // A site's first crawl has nothing to compare with: as without compareWith.
        var first = (await _sidecar.PostAsync($"api/crawls/{Id(then)}/issues/{TITLE_TOO_LONG}/rows", new JsonObject { ["compareWith"] = "previous" })).Body!.AsObject();
        Assert.False(first.ContainsKey("compared"));

        TestSidecar.AssertProblem(await _sidecar.PostAsync($"api/crawls/{Id(now)}/issues/{TITLE_TOO_LONG}/rows", new JsonObject { ["compareWith"] = other }),
            HttpStatusCode.Conflict, "crawl-not-comparable");
        TestSidecar.AssertProblem(await _sidecar.PostAsync($"api/crawls/{Id(now)}/urls", new JsonObject { ["compareWith"] = "previous" }), HttpStatusCode.BadRequest, "bad-request");
        TestSidecar.AssertProblem(await _sidecar.PostAsync($"api/crawls/{Id(now)}/links", new JsonObject { ["compareWith"] = "previous" }), HttpStatusCode.BadRequest, "bad-request");
    }

    /// <summary><c>/fixed</c> is what the engine says the check flagged then and doesn't now: by URL or by cause, narrowed and paged.</summary>
    [Fact]
    public async Task WhatACheckNoLongerFlagsIsListedByUrlOrByCause()
    {
        var then = FixLoopCrawls.AddTo(_sidecar, crawls.Then, "then");
        var now = FixLoopCrawls.AddTo(_sidecar, crawls.Now, "now", 10);
        // The 404s: some answer again (fixed), and some nothing links to any more (gone).
        const string CLIENT_ERRORS = "internal-client-error";
        var errors = $"api/crawls/{Id(now)}/issues/{CLIENT_ERRORS}/fixed";
        var page = (await _sidecar.PostAsync(errors)).Body!;
        var expected = CrawlComparisonReader.ReadCleared(now, then, CLIENT_ERRORS, new ClearedQuery())!;
        Assert.Equal((CLIENT_ERRORS, Id(then), "full", "rows", expected.Total, 0),
            ((string?)page["checkId"], (string?)page["compared"]!["with"], (string?)page["compared"]!["comparable"], (string?)page["level"], (long?)page["total"], (int)page["offset"]!));
        Assert.False(page.AsObject().ContainsKey("groups"));
        Assert.Equal(expected.Rows.Select(Row), page["rows"]!.AsArray().Select(JsonRow));
        Assert.Contains(expected.Rows, row => row.State == RowChangeState.Gone);
        Assert.Contains(expected.Rows, row => row.State == RowChangeState.Fixed);

        var gone = (await _sidecar.PostAsync(errors, new JsonObject { ["states"] = new JsonArray("gone"), ["limit"] = 2, ["offset"] = 1 })).Body!;
        var expectedGone = CrawlComparisonReader.ReadCleared(now, then, CLIENT_ERRORS, new ClearedQuery(States: [RowChangeState.Gone], Offset: 1, Limit: 2))!;
        Assert.Equal(expectedGone.Rows.Select(Row), gone["rows"]!.AsArray().Select(JsonRow));
        Assert.Equal(expectedGone.Total, (long?)gone["total"]);
        Assert.Equal(2, gone["rows"]!.AsArray().Count);

        // The broken links' causes: each broken target no page links to now.
        const string BROKEN_LINKS = "broken-internal-links";
        var url = $"api/crawls/{Id(now)}/issues/{BROKEN_LINKS}/fixed";
        var causes = (await _sidecar.PostAsync(url, new JsonObject { ["level"] = "groups" })).Body!;
        var expectedCauses = CrawlComparisonReader.ReadCleared(now, then, BROKEN_LINKS, new ClearedQuery(ClearedLevel.Groups))!;
        Assert.Equal(expectedCauses.Groups.Select(group => $"{group.Detail} {group.RelatedUrl} {group.SharedText} {group.Then} {group.Fixed} {group.Gone} {group.NotChecked}"),
            causes["groups"]!.AsArray().Select(group => $"{(string?)group!["detail"]} {(string?)group["relatedUrl"]} {(string?)group["sharedText"]} {(long)group["then"]!} {(long)group["fixed"]!} {(long)group["gone"]!} {(long)group["notChecked"]!}"));
        Assert.False(causes.AsObject().ContainsKey("rows"));
        Assert.NotEmpty(expectedCauses.Groups);

        var cause = expectedCauses.Groups[0];
        var narrowed = (await _sidecar.PostAsync(url, new JsonObject { ["group"] = new JsonObject { ["detail"] = cause.Detail, ["relatedUrl"] = cause.RelatedUrl } })).Body!;
        Assert.Equal(CrawlComparisonReader.ReadCleared(now, then, BROKEN_LINKS, new ClearedQuery(Group: new ClearedGroupKey(cause.Detail, cause.RelatedUrl)))!.Rows.Select(Row),
            narrowed["rows"]!.AsArray().Select(JsonRow));

        // A site's first crawl has nothing cleared to list.
        var first = (await _sidecar.PostAsync($"api/crawls/{Id(then)}/issues/{BROKEN_LINKS}/fixed")).Body!;
        Assert.Equal("""{"checkId":"broken-internal-links","compared":null,"level":"rows","total":null,"offset":0,"rows":[]}""", first.ToJsonString());

        var levelError = TestSidecar.AssertProblem(await _sidecar.PostAsync(url, new JsonObject { ["level"] = "pages" }), HttpStatusCode.UnprocessableEntity, "invalid-query");
        Assert.NotNull(levelError["errors"]!["level"]);
        var stateError = TestSidecar.AssertProblem(await _sidecar.PostAsync(url, new JsonObject { ["states"] = new JsonArray("still") }), HttpStatusCode.UnprocessableEntity, "invalid-query");
        Assert.NotNull(stateError["errors"]!["states[0]"]);
        TestSidecar.AssertProblem(await _sidecar.PostAsync(url, new JsonObject { ["limit"] = 0 }), HttpStatusCode.UnprocessableEntity, "invalid-query");
        TestSidecar.AssertProblem(await _sidecar.PostAsync($"api/crawls/{Id(now)}/issues/no-such-check/fixed"), HttpStatusCode.NotFound, "check-not-found");

        static string Row(ClearedIssue row) => $"{row.Url} {row.UrlId} {row.ThenUrlId} {Camel(row.State)} {Camel(row.Reason?.Code)} {row.Reason?.Text} {row.DetailThen} {row.RelatedUrlThen}";
        static string JsonRow(JsonNode? row) => $"{(string?)row!["url"]} {(int?)row["urlId"]} {(int)row["thenUrlId"]!} {(string?)row["state"]} {(string?)row["reason"]?["code"]} {(string?)row["reason"]?["text"]} {(string?)row["detailThen"]} {(string?)row["relatedUrlThen"]}";
    }

    /// <summary>
    /// The report compares with the crawl's previous crawl by default, with nothing on <c>none</c>, and with a crawl named;
    /// one that can't be compared is said under About this check, never refused. The workbook's export takes it too, and
    /// no other export does.
    /// </summary>
    [Fact]
    public async Task TheReportAndTheWorkbookCompareWithThePreviousCrawlByDefault()
    {
        var then = Id(FixLoopCrawls.AddTo(_sidecar, crawls.Then, "then"));
        var now = Id(FixLoopCrawls.AddTo(_sidecar, crawls.Now, "now", 10));
        var other = Id(FixLoopCrawls.AddTo(_sidecar, crawls.Then, "other", 5, path => TestCrawlFiles.SetStartUrl(path, crawls.Url("/").Replace("127.0.0.1", "localhost"))));
        const string SINCE = "<h2 id=\"since-last-check\">Since your last check on ";
        const string COMPARED = "<dt>Compared with</dt>";

        Assert.Contains(SINCE, await ReportAsync(now));
        Assert.Contains(SINCE, await ReportAsync(now, "?compareWith=previous"));
        Assert.Contains(SINCE, await ReportAsync(now, $"?compareWith={then}"));
        var none = await ReportAsync(now, "?compareWith=none");
        Assert.DoesNotContain(SINCE, none);
        Assert.DoesNotContain(COMPARED, none);
        var first = await ReportAsync(then);
        Assert.DoesNotContain(SINCE, first);
        Assert.DoesNotContain(COMPARED, first);
        var otherSite = await ReportAsync(now, $"?compareWith={other}");
        Assert.DoesNotContain(SINCE, otherSite);
        Assert.Contains($"{COMPARED}<dd>Not compared: that crawl is of another site.</dd>", otherSite);
        Assert.Contains($"{COMPARED}<dd>Not compared: no crawl has this id.</dd>", await ReportAsync(now, "?compareWith=0123456789abcdef"));
        TestSidecar.AssertProblem(await _sidecar.SendAsync(HttpMethod.Get, $"api/crawls/{now}/report?compareWith=none&compareWith=previous"), HttpStatusCode.UnprocessableEntity,
            "invalid-export");

        var refused = TestSidecar.AssertProblem(await _sidecar.PostAsync($"api/crawls/{now}/export",
            new JsonObject { ["kind"] = "urls", ["format"] = "csv", ["path"] = Path.Combine(_sidecar.DataDirectory, "urls.csv"), ["compareWith"] = "previous" }),
            HttpStatusCode.UnprocessableEntity, "invalid-export");
        Assert.NotNull(refused["errors"]!["compareWith"]);
        foreach (var compareWith in new[] { "previous", "none", other })
        {
            var started = await _sidecar.PostAsync($"api/crawls/{now}/export", new JsonObject
            {
                ["kind"] = "workbook", ["format"] = "xlsx", ["path"] = Path.Combine(_sidecar.DataDirectory, $"workbook-{compareWith}.xlsx"), ["compareWith"] = compareWith
            });
            Assert.True(started.Status == HttpStatusCode.Created, started.Body?.ToJsonString());
            var deadline = DateTime.UtcNow + EventStreamReader.TIMEOUT;
            JsonNode export;
            while ((string?)(export = await _sidecar.GetJsonAsync($"api/exports/{(string)started.Body!["exportId"]!}"))["status"] == "running")
            {
                Assert.True(DateTime.UtcNow < deadline, "The export didn't end.");
                await Task.Delay(50);
            }

            Assert.True((string?)export["status"] == "done", export.ToJsonString());
        }
    }

    private async Task<string> ReportAsync(string id, string query = "")
    {
        using var response = await _sidecar.Client.GetAsync($"api/crawls/{id}/report{query}");
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, text);
        return text;
    }

    private static string? Camel(Enum? value) => value is null ? null : JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    private async Task AnalyseAsync(string id)
    {
        Assert.Equal(HttpStatusCode.Accepted, (await _sidecar.PostAsync($"api/crawls/{id}/analyze")).Status);
        var deadline = DateTime.UtcNow + EventStreamReader.TIMEOUT;
        while ((string?)(await _sidecar.GetJsonAsync($"api/crawls/{id}"))["status"] == "analyzing")
        {
            Assert.True(DateTime.UtcNow < deadline, "The analysis didn't end.");
            await Task.Delay(50);
        }
    }

    private async Task<Dictionary<string, JsonNode>> ListAsync() =>
        (await _sidecar.GetJsonAsync("api/crawls")).AsArray().ToDictionary(item => (string)item!["id"]!, item => item!);

    private static string? Previous(Dictionary<string, JsonNode> items, string id) => (string?)items[id]["previous"]?["id"];

    private static string Id(string path) => CrawlIds.For(path);
}
