using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Sidecar.Tests.Support;

namespace ShoutingIguana.Sidecar.Tests;

/// <summary>
/// The grids' endpoints as the UI calls them: the JSON each gives, every count opening its URLs, paging by offset and by
/// cursor alike, the problems for a query that can't run, and what changes without a URL summary.
/// </summary>
public sealed class GridEndpointsTests(GridSidecar grid) : IClassFixture<GridSidecar>
{
    private TestSidecar Sidecar => grid.Sidecar;

    [Fact]
    public async Task TheColumnsListWhatEachGridTakes()
    {
        var columns = (await Sidecar.GetJsonAsync("api/url-columns")).AsArray().Select(column => column!).ToList();
        Assert.Equal(UrlGridReader.IssueRowColumns.Select(column => column.Id), columns.Select(column => (string?)column["id"]));
        var statusClass = columns.Single(column => (string?)column["id"] == "statusClass");
        Assert.Equal("enum", (string?)statusClass["type"]);
        Assert.Equal(["2xx", "3xx", "4xx", "5xx", "other", "noResponse"], statusClass["values"]!.AsArray().Select(value => (string?)value));
        Assert.Equal((false, true, true, false), Flags(statusClass));
        Assert.Equal((true, true, true, false), Flags(columns.Single(column => (string?)column["id"] == "inlinks")));
        Assert.Equal((false, false, false, false), Flags(columns.Single(column => (string?)column["id"] == "metaRobots")));
        Assert.Equal((false, true, true, true), Flags(columns.Single(column => (string?)column["id"] == "detail")));
        Assert.Equal((false, false, true, true), Flags(columns.Single(column => (string?)column["id"] == "relatedUrl")));
        Assert.All(columns, column => Assert.Contains((string?)column["type"], new[] { "number", "text", "enum", "boolean" }));

        static (bool, bool, bool, bool) Flags(JsonNode column) =>
            ((bool)column["needsAnalysis"]!, (bool)column["sortable"]!, (bool)column["filterable"]!, (bool)column["issueRowsOnly"]!);
    }

    [Fact]
    public async Task AUrlsPageCarriesTheColumnsAskedFor()
    {
        // No body: every URL, in crawl order, with its URL.
        var all = await PostOkAsync($"api/crawls/{grid.Id}/urls");
        Assert.Equal(["id", "url"], all["columns"]!.AsArray().Select(column => (string?)column));
        var first = all["rows"]![0]!.AsObject();
        Assert.Equal(["id", "url"], first.Select(property => property.Key));
        Assert.Equal((long?)all["total"], (long?)all["matching"]);
        Assert.Equal(0, (int?)all["offset"]);
        Assert.NotNull((string?)all["next"]);
        Assert.Equal("""{"available":true,"reason":null}""", all["summary"]!.ToJsonString());
        Assert.Empty(all["unavailable"]!.AsArray());

        // Typed values, filtered and sorted; the next page by cursor is the page by offset.
        var query = JsonNode.Parse("""
            {"columns":["url","statusCode","indexable","title","type","inSitemap"],
             "filters":[{"column":"type","op":"in","values":["page"]},{"column":"statusCode","op":"between","min":200,"max":299}],
             "sort":[{"column":"wordCount","desc":true}],"limit":5}
            """)!;
        var page = await PostOkAsync($"api/crawls/{grid.Id}/urls", query);
        var rows = page["rows"]!.AsArray();
        Assert.Equal(5, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal(200, (int?)row!["statusCode"]);
            Assert.Equal("page", (string?)row["type"]);
            Assert.True(row["indexable"] is null || row["indexable"]!.GetValueKind() is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False);
        });
        Assert.True((long?)page["matching"] < (long?)page["total"]);

        var next = query.DeepClone();
        next["after"] = (string?)page["next"];
        var byCursor = await PostOkAsync($"api/crawls/{grid.Id}/urls", next);
        var byOffset = await PostOkAsync($"api/crawls/{grid.Id}/urls", With(query, "offset", 5));
        Assert.Equal(byOffset["rows"]!.ToJsonString(), byCursor["rows"]!.ToJsonString());
        Assert.Null(byCursor["offset"]);
        Assert.Equal(5, (int?)byOffset["offset"]);
    }

    public static TheoryData<string, string> BadQueries() => new()
    {
        { """{"filters":[{"column":"type","op":"like","values":["page"]}]}""", "filters[0].op" },
        { """{"filters":[{"op":"eq","value":1}]}""", "filters[0].column" },
        { """{"filters":[{"column":"statusCode","op":"eq","value":"404"}]}""", "filters[0].value" },
        { """{"filters":[{"column":"statusCode","op":"in","values":[{"code":404}]}]}""", "filters[0].values" },
        { """{"filters":[{"column":"url","op":"regex","value":"(a)\\1"}]}""", "filters[0].value" },
        { """{"filters":[{"column":"type","op":"in","values":["spreadsheet"]}]}""", "filters[0].values" },
        { """{"sort":[{"column":"title"}]}""", "sort[0].column" },
        { """{"sort":[{"desc":true}]}""", "sort[0].column" },
        { """{"columns":["colour"]}""", "columns[0]" },
        { """{"limit":0}""", "limit" },
        { """{"limit":1001}""", "limit" },
        { """{"after":"not a cursor"}""", "after" }
    };

    [Theory]
    [MemberData(nameof(BadQueries))]
    public async Task AQueryThatCantRunIsAnInvalidQueryNamingTheField(string body, string field)
    {
        var problem = TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/urls", JsonNode.Parse(body)), HttpStatusCode.UnprocessableEntity, "invalid-query");
        Assert.Contains(field, problem["errors"]!.AsObject().Select(error => error.Key));
        Assert.False(string.IsNullOrEmpty((string?)problem["detail"]));
    }

    [Fact]
    public async Task ARequestTheApiDoesntTakeIsABadRequest()
    {
        TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/urls", JsonNode.Parse("""{"colour":"red"}""")), HttpStatusCode.BadRequest, "bad-request");
        TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/urls", JsonNode.Parse("""{"group":{"detail":null,"relatedUrlId":1}}""")),
            HttpStatusCode.BadRequest, "bad-request");
        using var malformed = new StringContent("{\"limit\":", Encoding.UTF8, "application/json");
        using var response = await Sidecar.Client.PostAsync($"api/crawls/{grid.Id}/urls", malformed);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        TestSidecar.AssertProblem(await Sidecar.PostAsync("api/crawls/0123456789abcdef/urls"), HttpStatusCode.NotFound, "crawl-not-found");
    }

    [Fact]
    public async Task WithoutASummaryTheGridSaysWhyAndRefusesItsColumns()
    {
        var changed = grid.Copy(TestCrawlFiles.StampAsCrawledAgain);
        var page = await PostOkAsync($"api/crawls/{changed}/urls", JsonNode.Parse("""{"columns":["url","inlinks","statusCode"],"limit":3}"""));
        Assert.Equal("""{"available":false,"reason":"crawlChanged"}""", page["summary"]!.ToJsonString());
        Assert.Equal(["inlinks"], page["unavailable"]!.AsArray().Select(column => (string?)column));
        Assert.Equal(["id", "url", "statusCode"], page["rows"]![0]!.AsObject().Select(property => property.Key));

        var refused = TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{changed}/urls", JsonNode.Parse("""
            {"filters":[{"column":"check","op":"in","values":["title-too-long"]}],"sort":[{"column":"inlinks","desc":true}]}
            """)), HttpStatusCode.Conflict, "needs-analysis");
        Assert.Equal("crawlChanged", (string?)refused["reason"]);
        Assert.Equal(["inlinks"], refused["columns"]!.AsArray().Select(column => (string?)column));

        // The check filter reads the last analysis's issues, as a check's rows do, so a group's count still opens its URLs.
        var check = new JsonObject { ["filters"] = JsonNode.Parse("""[{"column":"check","op":"in","values":["title-too-long"]}]"""), ["limit"] = 1 };
        var rows = await PostOkAsync($"api/crawls/{changed}/issues/title-too-long/rows", new JsonObject { ["limit"] = 1 });
        Assert.Equal((long?)rows["total"], (long?)(await PostOkAsync($"api/crawls/{changed}/urls", check))["matching"]);

        var neverAnalysed = TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.FreshId}/urls/facets", JsonNode.Parse("""
            {"filters":[{"column":"issues","op":"gt","value":0},{"column":"check","op":"in","values":["title-too-long"]}]}
            """)), HttpStatusCode.Conflict, "needs-analysis");
        Assert.Equal("notAnalyzed", (string?)neverAnalysed["reason"]);
        Assert.Equal(["issues", "check"], neverAnalysed["columns"]!.AsArray().Select(column => (string?)column));
    }

    [Fact]
    public async Task EveryFacetCountOpensItsUrls()
    {
        var facets = await PostOkAsync($"api/crawls/{grid.Id}/urls/facets");
        Assert.Equal(UrlGridReader.FACETS, facets["facets"]!.AsObject().Select(facet => facet.Key));
        Assert.Equal((long?)facets["total"], (long?)facets["matching"]);
        foreach (var (name, buckets) in facets["facets"]!.AsObject())
        {
            foreach (var bucket in buckets!.AsArray())
            {
                var opened = await PostOkAsync($"api/crawls/{grid.Id}/urls", new JsonObject { ["filters"] = new JsonArray(bucket!["filter"]!.DeepClone()), ["limit"] = 1 });
                Assert.True((long?)bucket["count"] == (long?)opened["matching"], $"{name} {bucket["value"]?.ToJsonString() ?? "null"}");
            }
        }

        // A range bucket has its bounds, the last none above; filtered, the facets count the matching URLs.
        var times = facets["facets"]!["responseTime"]!.AsArray();
        Assert.Equal(0, (long?)times[0]!["min"]);
        Assert.Equal(249, (long?)times[0]!["max"]);
        var pages = await PostOkAsync($"api/crawls/{grid.Id}/urls/facets", JsonNode.Parse("""
            {"filters":[{"column":"type","op":"in","values":["page"]}],"facets":["statusClass","depth","severity"]}
            """));
        Assert.Equal(["statusClass", "depth", "severity"], pages["facets"]!.AsObject().Select(facet => facet.Key));
        Assert.True((long?)pages["matching"] < (long?)pages["total"]);

        var unknown = TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/urls/facets", JsonNode.Parse("""{"facets":["colour"]}""")),
            HttpStatusCode.UnprocessableEntity, "invalid-query");
        Assert.Contains("facets[0]", unknown["errors"]!.AsObject().Select(error => error.Key));
    }

    [Fact]
    public async Task ALookupFindsAUrlByItsText()
    {
        var found = await PostOkAsync($"api/crawls/{grid.Id}/urls/lookup", new JsonObject { ["url"] = grid.Url("/p/7#reviews") });
        Assert.Equal(["id", "url", "requested"], found.AsObject().Select(property => property.Key));
        Assert.Equal(grid.Url("/p/7"), (string?)found["url"]);
        Assert.Null(found["requested"]);
        Assert.Equal((int?)found["id"], (int?)(await PostOkAsync($"api/crawls/{grid.Id}/urls/lookup", new JsonObject { ["url"] = "/p/7" }))["id"]);

        TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/urls/lookup", new JsonObject { ["url"] = "/no/such/page" }), HttpStatusCode.NotFound, "url-not-found");
        foreach (var text in new[] { "not a url", "  " })
        {
            var problem = TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/urls/lookup", new JsonObject { ["url"] = text }),
                HttpStatusCode.UnprocessableEntity, "invalid-query");
            Assert.Contains("url", problem["errors"]!.AsObject().Select(error => error.Key));
        }

        TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/urls/lookup"), HttpStatusCode.BadRequest, "bad-request");
    }

    [Fact]
    public async Task TheInspectorGivesOneUrlInFull()
    {
        var product = await InspectAsync(IdOf("/p/7"));
        Assert.Equal(
            ["id", "url", "isInternal", "type", "discovery", "response", "redirects", "indexability", "content", "limits", "links", "headers", "structuredData",
             "hreflang", "issues", "namedBy", "summary", "source"],
            product.AsObject().Select(property => property.Key));
        Assert.Equal("page", (string?)product["type"]);
        Assert.Equal("success", (string?)product["response"]!["outcome"]);
        Assert.EndsWith("Z", (string?)product["response"]!["crawledAt"]);
        Assert.Equal("""{"stored":true}""", product["source"]!.ToJsonString());
        Assert.True((bool?)product["limits"]!["fromAnalysis"]);
        Assert.Equal(60, (int?)product["limits"]!["titleMax"]);

        // The checklist: each test with only the facts that go with it.
        var indexability = product["indexability"]!;
        Assert.True((bool?)indexability["indexable"]);
        Assert.Equal("none", (string?)indexability["reason"]);
        Assert.Equal("analysis", (string?)indexability["judgedBy"]);
        var tests = indexability["tests"]!.AsArray();
        Assert.Equal("""{"test":"answered","outcome":"pass","statusCode":200}""", tests[0]!.ToJsonString());
        Assert.Equal("""{"test":"robotsTxt","outcome":"pass"}""", tests[1]!.ToJsonString());
        Assert.Equal("""{"test":"noindex","outcome":"pass","meta":false,"header":false}""", tests[2]!.ToJsonString());
        Assert.Equal(grid.Url("/p/7"), (string?)tests[3]!["canonical"]!["url"]);

        var gone = await InspectAsync(IdOf("/plants/gone"));
        Assert.Equal("clientError", (string?)gone["indexability"]!["reason"]);
        Assert.Equal("""{"test":"answered","outcome":"fail","statusCode":404}""", gone["indexability"]!["tests"]![0]!.ToJsonString());
        Assert.Null(gone["content"]);

        var chain = await InspectAsync(IdOf("/plants/chain-a"));
        Assert.Equal([grid.Url("/plants/chain-b"), grid.Url("/plants/chain-c"), grid.Url("/plants/thin")],
            chain["redirects"]!["hops"]!.AsArray().Select(hop => (string?)hop!["url"]));
        Assert.Equal("http", (string?)chain["redirects"]!["hops"]![0]!["via"]);

        // Any id no URL has, past what an id holds too (this one is 1 if cut to an int), is a URL the crawl doesn't have;
        // none at all is a field error.
        foreach (var missing in new[] { "999999", "0", "-1", "4294967297" })
        {
            TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/urls/inspect", JsonNode.Parse($$"""{"urlId":{{missing}}}""")),
                HttpStatusCode.NotFound, "url-not-found");
        }

        var none = TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/urls/inspect", JsonNode.Parse("{}")), HttpStatusCode.UnprocessableEntity, "invalid-query");
        Assert.Contains("urlId", none["errors"]!.AsObject().Select(error => error.Key));
    }

    [Fact]
    public async Task LinksPageAlikeByOffsetAndByCursor()
    {
        var home = IdOf("/");
        var first = await PostOkAsync($"api/crawls/{grid.Id}/urls/inlinks", JsonNode.Parse($$"""{"urlId":{{home}},"kinds":["hyperlink"],"limit":5}"""));
        var link = first["rows"]![0]!;
        Assert.Equal(["id", "url", "kind", "text", "position", "flags", "followed", "fragment", "statusCode", "indexable", "fetched"], link.AsObject().Select(property => property.Key));
        Assert.Equal("hyperlink", (string?)link["kind"]);
        Assert.NotNull(link["flags"]!.AsArray());
        Assert.True((long?)first["total"] > 5);

        var byCursor = await PostOkAsync($"api/crawls/{grid.Id}/urls/inlinks",
            new JsonObject { ["urlId"] = home, ["kinds"] = new JsonArray("hyperlink"), ["after"] = (string?)first["next"], ["limit"] = 5 });
        var byOffset = await PostOkAsync($"api/crawls/{grid.Id}/urls/inlinks", JsonNode.Parse($$"""{"urlId":{{home}},"kinds":["hyperlink"],"offset":5,"limit":5}"""));
        Assert.Equal(byOffset["rows"]!.ToJsonString(), byCursor["rows"]!.ToJsonString());

        // A cursor carries no SQL and nothing crawled: a hash and a row id, whatever the list.
        var cursor = Encoding.UTF8.GetString(System.Buffers.Text.Base64Url.DecodeFromChars((string)first["next"]!));
        Assert.DoesNotContain("kind", cursor, StringComparison.Ordinal);
        Assert.Matches("""^\{"h":"[0-9a-f]{16}","i":\d+,"k":"[0-9a-f]{16}"\}$""", cursor);

        var outlinks = await PostOkAsync($"api/crawls/{grid.Id}/urls/outlinks", new JsonObject { ["urlId"] = home });
        Assert.NotEmpty(outlinks["rows"]!.AsArray());
        Assert.DoesNotContain(outlinks["rows"]!.AsArray(), row => (int?)row!["id"] == home);

        var kind = TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/urls/outlinks", JsonNode.Parse($$"""{"urlId":{{home}},"kinds":["anchor"]}""")),
            HttpStatusCode.UnprocessableEntity, "invalid-query");
        Assert.Contains("kinds[0]", kind["errors"]!.AsObject().Select(error => error.Key));
        TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/urls/inlinks", JsonNode.Parse("""{"urlId":999999}""")), HttpStatusCode.NotFound, "url-not-found");
    }

    [Fact]
    public async Task TheSourceIsTheStoredHtmlOrWhyThereIsNone()
    {
        var page = await PostOkAsync($"api/crawls/{grid.Id}/urls/source", new JsonObject { ["urlId"] = IdOf("/p/7") });
        Assert.Equal(["stored", "html", "bytes", "truncated"], page.AsObject().Select(property => property.Key));
        Assert.StartsWith("<!DOCTYPE html>", (string?)page["html"], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Encoding.UTF8.GetByteCount((string)page["html"]!), (long?)page["bytes"]);
        Assert.False((bool)page["truncated"]!);
        Assert.Equal("""{"stored":false,"reason":"notPage"}""", (await PostOkAsync($"api/crawls/{grid.Id}/urls/source", new JsonObject { ["urlId"] = IdOf("/img/1.jpg") })).ToJsonString());

        var unstored = UrlGridReader.Lookup(System.IO.Path.Combine(Sidecar.DataDirectory, "fresh.sicrawl"), grid.Url("/"))!.Id;
        Assert.Equal("""{"stored":false,"reason":"notStored"}""", (await PostOkAsync($"api/crawls/{grid.FreshId}/urls/source", new JsonObject { ["urlId"] = unstored })).ToJsonString());
        TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/urls/source", JsonNode.Parse("""{"urlId":999999}""")), HttpStatusCode.NotFound, "url-not-found");
    }

    [Fact]
    public async Task ACheckGroupsOpenTheirRows()
    {
        var checks = (await Sidecar.GetJsonAsync("api/checks"))["checks"]!.AsArray();
        Assert.All(checks, check => Assert.Equal(GroupingName(IssueCatalog.Find((string)check!["id"]!)!.Grouping), (string?)check["grouping"]));

        var counts = IssueReader.ReadCounts(grid.Path);
        var related = counts.Where(count => count.Value > 0).Select(count => IssueCatalog.Find(count.Key)!)
            .First(check => check.Grouping == IssueGrouping.RelatedUrl).Id;
        var groups = await PostOkAsync($"api/crawls/{grid.Id}/issues/{related}/groups", JsonNode.Parse("""{"limit":1000}"""));
        Assert.Equal(["checkId", "grouping", "total", "groupCount", "offset", "groups"], groups.AsObject().Select(property => property.Key));
        Assert.Equal("relatedUrl", (string?)groups["grouping"]);
        Assert.Equal(counts[related], (long?)groups["total"]);
        foreach (var group in groups["groups"]!.AsArray())
        {
            Assert.Equal(["detail", "relatedUrlId", "relatedUrl", "pages", "share", "likelyTemplate"], group!.AsObject().Select(property => property.Key));
            var key = new JsonObject { ["detail"] = group["detail"]?.DeepClone(), ["relatedUrlId"] = group["relatedUrlId"]?.DeepClone() };
            var rows = await PostOkAsync($"api/crawls/{grid.Id}/issues/{related}/rows", new JsonObject { ["group"] = key, ["limit"] = 1000 });
            Assert.Equal((long?)group["pages"], (long?)rows["total"]);
            Assert.Equal((long?)group["pages"], rows["rows"]!.AsArray().Count);
            Assert.All(rows["rows"]!.AsArray(), row => Assert.Equal((string?)group["relatedUrl"], (string?)row!["relatedUrl"]));

            // The URL grid opens the same rows with the check filter's group.
            var filter = new JsonObject { ["column"] = "check", ["op"] = "in", ["values"] = new JsonArray(related), ["group"] = key.DeepClone() };
            Assert.Equal((long?)group["pages"], (long?)(await PostOkAsync($"api/crawls/{grid.Id}/urls", new JsonObject { ["filters"] = new JsonArray(filter), ["limit"] = 1 }))["matching"]);
        }

        // A check that isn't grouped lists its URLs flat, the detail as a column.
        var flat = counts.Where(count => count.Value > 0).Select(count => IssueCatalog.Find(count.Key)!).First(check => check.Grouping == IssueGrouping.None).Id;
        var none = await PostOkAsync($"api/crawls/{grid.Id}/issues/{flat}/groups");
        Assert.Equal("none", (string?)none["grouping"]);
        Assert.Equal(counts[flat], (long?)none["total"]);
        Assert.Empty(none["groups"]!.AsArray());
        var rowsOfNone = await PostOkAsync($"api/crawls/{grid.Id}/issues/{flat}/rows");
        Assert.Equal(["id", "url", "detail", "relatedUrl"], rowsOfNone["columns"]!.AsArray().Select(column => (string?)column));
        var grouped = TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/issues/{flat}/rows", JsonNode.Parse("""{"group":{"detail":null,"relatedUrlId":null}}""")),
            HttpStatusCode.UnprocessableEntity, "invalid-query");
        Assert.Contains("group", grouped["errors"]!.AsObject().Select(error => error.Key));
    }

    [Fact]
    public async Task ACheckNotRunHasNoCountsAndAnUnknownOneIsNotFound()
    {
        // The limited crawl skipped the orphan check: no total and no rows, never 0.
        var skipped = await PostOkAsync($"api/crawls/{grid.LimitedId}/issues/{IssueCatalog.ORPHAN_PAGE.Id}/groups");
        Assert.Null(skipped["total"]);
        Assert.Null(skipped["groupCount"]);
        Assert.Empty(skipped["groups"]!.AsArray());
        var rows = await PostOkAsync($"api/crawls/{grid.LimitedId}/issues/{IssueCatalog.ORPHAN_PAGE.Id}/rows");
        Assert.Null((long?)rows["total"]);
        Assert.Null((long?)rows["matching"]);
        Assert.Empty(rows["rows"]!.AsArray());

        foreach (var path in new[] { "groups", "rows" })
        {
            TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/issues/no-such-check/{path}"), HttpStatusCode.NotFound, "check-not-found");
            TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.FreshId}/issues/{IssueCatalog.ORPHAN_PAGE.Id}/{path}"), HttpStatusCode.NotFound, "not-analyzed");
        }

        TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/issues/{IssueCatalog.ORPHAN_PAGE.Id}/groups", JsonNode.Parse("""{"limit":0}""")),
            HttpStatusCode.UnprocessableEntity, "invalid-query");
    }

    private async Task<JsonNode> PostOkAsync(string path, JsonNode? body = null)
    {
        var (status, answer) = await Sidecar.PostAsync(path, body);
        Assert.True(status == HttpStatusCode.OK, $"POST {path}: {(int)status} {answer?.ToJsonString()}");
        return answer!;
    }

    private int IdOf(string path) => UrlGridReader.Lookup(grid.Path, grid.Url(path))!.Id;

    private Task<JsonNode> InspectAsync(int urlId) => PostOkAsync($"api/crawls/{grid.Id}/urls/inspect", new JsonObject { ["urlId"] = urlId });

    private static JsonNode With(JsonNode query, string name, int value)
    {
        var copy = query.DeepClone();
        copy[name] = value;
        return copy;
    }

    private static string GroupingName(IssueGrouping grouping) => grouping switch
    {
        IssueGrouping.PageDetail or IssueGrouping.ResponseDetail => "detail",
        IssueGrouping.RelatedUrl => "relatedUrl",
        _ => "none"
    };
}
