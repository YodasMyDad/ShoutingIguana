using System.Net;
using System.Text.Json.Nodes;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Sidecar.Tests.Support;

namespace ShoutingIguana.Sidecar.Tests;

/// <summary>
/// The links grid, the sitemaps and robots.txt as the UI calls them (E3b): the JSON each gives, every count opening its
/// rows, the problems for a call that can't be answered, and what a crawl never analysed or changed since still answers.
/// </summary>
public sealed class LinksSitemapsRobotsEndpointsTests(GridSidecar grid) : IClassFixture<GridSidecar>
{
    private TestSidecar Sidecar => grid.Sidecar;

    [Fact]
    public async Task TheLinksGridPagesFiltersSortsAndCounts()
    {
        var columns = (await Sidecar.GetJsonAsync("api/link-columns")).AsArray().Select(column => column!).ToList();
        Assert.Equal(LinkGridReader.Columns.Select(column => column.Id), columns.Select(column => (string?)column["id"]));
        Assert.Equal(["id", "type", "needsAnalysis", "sortable", "filterable", "values"], columns[0].AsObject().Select(property => property.Key));
        var kind = columns.Single(column => (string?)column["id"] == "kind");
        Assert.Equal(("enum", false, true), ((string?)kind["type"], (bool)kind["sortable"]!, (bool)kind["filterable"]!));
        Assert.Contains("hyperlink", kind["values"]!.AsArray().Select(value => (string?)value));
        Assert.True((bool)columns.Single(column => (string?)column["id"] == "to.statusCode")["sortable"]!);
        Assert.False((bool)columns.Single(column => (string?)column["id"] == "from.statusCode")["sortable"]!);
        Assert.True((bool)columns.Single(column => (string?)column["id"] == "to.inlinks")["needsAnalysis"]!);

        // A page: one object a link, keyed by column, the link's id first; the URL grid's page around it.
        var all = await PostOkAsync($"api/crawls/{grid.Id}/links");
        Assert.Equal(["columns", "rows", "offset", "matching", "total", "next", "summary", "unavailable"], all.AsObject().Select(property => property.Key));
        Assert.Equal(["id", "from.url", "to.url", "kind", "text"], all["columns"]!.AsArray().Select(column => (string?)column));
        Assert.Equal(["id", "from.url", "to.url", "kind", "text"], all["rows"]![0]!.AsObject().Select(property => property.Key));
        Assert.Equal((100, 0), (all["rows"]!.AsArray().Count, (int)all["offset"]!));
        var total = (long)all["total"]!;
        Assert.Equal((total, """{"available":true,"reason":null}"""), ((long)all["matching"]!, all["summary"]!.ToJsonString()));
        Assert.Equal(Enumerable.Range(1, 100).Select(id => (long)id), all["rows"]!.AsArray().Select(row => (long)row!["id"]!));

        // Filtered on the link and on its target, sorted by the target, with the columns asked for.
        var query = JsonNode.Parse("""
            {"columns":["to.url","to.statusCode","followed","position"],
             "filters":[{"column":"kind","op":"in","values":["image"]},{"column":"to.statusClass","op":"in","values":["2xx"]}],
             "sort":[{"column":"to.url","desc":true}],"limit":25}
            """)!;
        var images = await PostOkAsync($"api/crawls/{grid.Id}/links", query);
        Assert.Equal(["id", "to.url", "to.statusCode", "followed", "position"], images["rows"]![0]!.AsObject().Select(property => property.Key));
        Assert.All(images["rows"]!.AsArray(), row => Assert.InRange((int)row!["to.statusCode"]!, 200, 299));
        Assert.Equal(images["rows"]!.AsArray().Select(row => (string?)row!["to.url"]).OrderDescending(StringComparer.Ordinal), images["rows"]!.AsArray().Select(row => (string?)row!["to.url"]));
        Assert.InRange((long)images["matching"]!, 26, total - 1);

        // Read on by cursor to the end: every matching link once.
        List<long> ids = [.. images["rows"]!.AsArray().Select(row => (long)row!["id"]!)];
        for (var next = (string?)images["next"]; next is not null;)
        {
            var page = await PostOkAsync($"api/crawls/{grid.Id}/links", With(query, "after", next));
            Assert.Null(page["offset"]);
            ids.AddRange(page["rows"]!.AsArray().Select(row => (long)row!["id"]!));
            next = (string?)page["next"];
        }

        Assert.Equal(((long)images["matching"]!, (long)images["matching"]!), (ids.Count, ids.Distinct().Count()));

        // The facets: every count opens its links.
        var facets = await PostOkAsync($"api/crawls/{grid.Id}/links/facets", JsonNode.Parse("""{"filters":[{"column":"kind","op":"in","values":["hyperlink"]}]}"""));
        Assert.Equal(["matching", "total", "summary", "unavailable", "facets"], facets.AsObject().Select(property => property.Key));
        Assert.Equal(LinkGridReader.FACETS, facets["facets"]!.AsObject().Select(facet => facet.Key));
        Assert.Equal(["followed", "nofollow"], facets["facets"]!["follow"]!.AsArray().Select(bucket => (string?)bucket!["value"]).Order());
        foreach (var bucket in facets["facets"]!.AsObject().SelectMany(facet => facet.Value!.AsArray()))
        {
            var opened = await PostOkAsync($"api/crawls/{grid.Id}/links", new JsonObject
            {
                ["filters"] = new JsonArray(JsonNode.Parse("""{"column":"kind","op":"in","values":["hyperlink"]}"""), bucket!["filter"]!.DeepClone()),
                ["limit"] = 1
            });
            Assert.Equal((long)bucket["count"]!, (long)opened["matching"]!);
        }

        Assert.Equal((long)facets["matching"]!, facets["facets"]!["to.statusClass"]!.AsArray().Sum(bucket => (long)bucket!["count"]!));
    }

    [Fact]
    public async Task ALinksQueryThatCantRunSaysWhy()
    {
        async Task<JsonNode> RefusedAsync(string body, string path = "links") =>
            TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/{path}", JsonNode.Parse(body)), HttpStatusCode.UnprocessableEntity, "invalid-query");

        Assert.Contains("columns[0]", (await RefusedAsync("""{"columns":["anchor"]}"""))["errors"]!.AsObject().Select(error => error.Key));
        Assert.Contains("sort[0].column", (await RefusedAsync("""{"sort":[{"column":"text"}]}"""))["errors"]!.AsObject().Select(error => error.Key));
        Assert.Contains("sort", (await RefusedAsync("""{"sort":[{"column":"to.url"},{"column":"id"}]}"""))["errors"]!.AsObject().Select(error => error.Key));
        Assert.Contains("filters[0].op", (await RefusedAsync("""{"filters":[{"column":"to.statusCode","op":"contains","value":"4"}]}"""))["errors"]!.AsObject().Select(error => error.Key));
        Assert.Contains("after", (await RefusedAsync("""{"after":"nonsense"}"""))["errors"]!.AsObject().Select(error => error.Key));
        Assert.Contains("facets[0]", (await RefusedAsync("""{"facets":["status"]}""", "links/facets"))["errors"]!.AsObject().Select(error => error.Key));
        TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/links", JsonNode.Parse("""{"colour":"red"}""")), HttpStatusCode.BadRequest, "bad-request");
        TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/links", JsonNode.Parse("""{"sitemapId":1}""")), HttpStatusCode.BadRequest, "bad-request");
        TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/urls", JsonNode.Parse("""{"sitemapId":1}""")), HttpStatusCode.BadRequest, "bad-request");
        TestSidecar.AssertProblem(await Sidecar.PostAsync("api/crawls/0000000000000000/links"), HttpStatusCode.NotFound, "crawl-not-found");

        // A crawl never analysed: its links are read as they are; a column that needs the analysis is named and left out, and a
        // filter or sort on one is refused.
        var fresh = await PostOkAsync($"api/crawls/{grid.FreshId}/links", JsonNode.Parse("""{"columns":["to.url","to.inlinks"],"filters":[{"column":"kind","op":"in","values":["hyperlink"]}]}"""));
        Assert.Equal(("""{"available":false,"reason":"notAnalyzed"}""", """["to.inlinks"]"""), (fresh["summary"]!.ToJsonString(), fresh["unavailable"]!.ToJsonString()));
        Assert.True((long)fresh["matching"]! > 0 && (long)fresh["matching"]! < (long)fresh["total"]!);
        var needs = TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.FreshId}/links", JsonNode.Parse("""{"sort":[{"column":"to.inlinks","desc":true}]}""")),
            HttpStatusCode.Conflict, "needs-analysis");
        Assert.Equal(("notAnalyzed", """["to.inlinks"]"""), ((string?)needs["reason"], needs["columns"]!.ToJsonString()));
        Assert.Equal((long)fresh["total"]!, (long)(await PostOkAsync($"api/crawls/{grid.FreshId}/links/facets"))["matching"]!);
    }

    [Fact]
    public async Task TheSitemapsAreListedAndOneOpensItsUrls()
    {
        var list = await Sidecar.GetJsonAsync($"api/crawls/{grid.Id}/sitemaps");
        Assert.Equal(["discovery", "sitemaps", "summary"], list.AsObject().Select(property => property.Key));
        Assert.Equal(("done", """{"available":true,"reason":null}"""), ((string?)list["discovery"], list["summary"]!.ToJsonString()));
        var sitemaps = list["sitemaps"]!.AsArray();
        var index = sitemaps[0]!;
        Assert.Equal(["id", "url", "parentId", "foundVia", "state", "statusCode", "isIndex", "entries", "sitemaps", "error", "urls"], index.AsObject().Select(property => property.Key));
        Assert.Equal((grid.Url("/sitemap.xml"), "robotsTxt", "read", true, 3), ((string?)index["url"], (string?)index["foundVia"], (string?)index["state"], (bool)index["isIndex"]!, (int)index["sitemaps"]!));
        Assert.Null(index["parentId"]);
        Assert.Equal(["listed", "fetched", "indexable", "nonIndexable"], index["urls"]!.AsObject().Select(property => property.Key));
        var missing = sitemaps[^1]!;
        Assert.Equal((grid.Url("/sitemaps/missing.xml"), "index", "failed", 404, "HTTP 404", (int)index["id"]!),
            ((string?)missing["url"], (string?)missing["foundVia"], (string?)missing["state"], (int)missing["statusCode"]!, (string?)missing["error"], (int)missing["parentId"]!));

        // Each count opens its URLs in the sitemap's grid, which is the URL grid: its columns, filters, sorts and paging.
        foreach (var sitemap in sitemaps)
        {
            async Task<long> OpensAsync(string filters) =>
                (long)(await PostOkAsync($"api/crawls/{grid.Id}/sitemaps/urls", JsonNode.Parse($$"""{"sitemapId":{{(int)sitemap!["id"]!}},"filters":[{{filters}}],"limit":1}""")))["matching"]!;
            Assert.Equal((long)sitemap!["urls"]!["listed"]!, await OpensAsync(string.Empty));
            Assert.Equal((long)sitemap["urls"]!["fetched"]!, await OpensAsync("""{"column":"fetched","op":"is","value":true}"""));
            Assert.Equal((long)sitemap["urls"]!["indexable"]!, await OpensAsync("""{"column":"indexable","op":"is","value":true}"""));
            Assert.Equal((long)sitemap["urls"]!["nonIndexable"]!, await OpensAsync("""{"column":"indexable","op":"is","value":false}"""));
        }

        var urls = await PostOkAsync($"api/crawls/{grid.Id}/sitemaps/urls", JsonNode.Parse(
            $$"""{"sitemapId":{{(int)index["id"]!}},"columns":["url","statusCode","indexability"],"sort":[{"column":"url"}],"limit":3}"""));
        Assert.Equal(["columns", "rows", "offset", "matching", "total", "next", "summary", "unavailable"], urls.AsObject().Select(property => property.Key));
        Assert.Equal(["id", "url", "statusCode", "indexability"], urls["rows"]![0]!.AsObject().Select(property => property.Key));
        Assert.Equal(((long)index["urls"]!["listed"]!, (long)index["urls"]!["listed"]!, 3), ((long)urls["total"]!, (long)urls["matching"]!, urls["rows"]!.AsArray().Count));
        Assert.True((long)urls["total"]! < (long)(await PostOkAsync($"api/crawls/{grid.Id}/urls"))["total"]!);

        TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/sitemaps/urls", JsonNode.Parse("""{"sitemapId":999}""")), HttpStatusCode.NotFound, "sitemap-not-found");
        TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/sitemaps/urls", JsonNode.Parse("""{"sitemapId":0}""")), HttpStatusCode.NotFound, "sitemap-not-found");
        var none = TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/sitemaps/urls", JsonNode.Parse("""{"limit":5}""")), HttpStatusCode.UnprocessableEntity, "invalid-query");
        Assert.Contains("sitemapId", none["errors"]!.AsObject().Select(error => error.Key));
        TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/sitemaps/urls", JsonNode.Parse("""{"sitemapId":"one"}""")), HttpStatusCode.BadRequest, "bad-request");
        TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/sitemaps/urls"), HttpStatusCode.BadRequest, "bad-request");

        // A crawl cut short by its URL limit, never analysed: what it has, counted by the crawl's own judgement.
        var fresh = await Sidecar.GetJsonAsync($"api/crawls/{grid.FreshId}/sitemaps");
        Assert.Equal("""{"available":false,"reason":"notAnalyzed"}""", fresh["summary"]!.ToJsonString());
        Assert.NotEmpty(fresh["sitemaps"]!.AsArray());
        TestSidecar.AssertProblem(await Sidecar.SendAsync(HttpMethod.Get, "api/crawls/0000000000000000/sitemaps"), HttpStatusCode.NotFound, "crawl-not-found");
    }

    [Fact]
    public async Task RobotsTxtIsListedShownAndSaysWhatItBlocked()
    {
        // The site's small crawl found nothing robots.txt refuses, so a copy is given one such URL.
        var id = grid.Copy(copy => TestCrawlFiles.AddRefusedByRobots(copy, grid.Url("/private/1")));
        var site = grid.Url(string.Empty);

        var origins = await PostOkAsync($"api/crawls/{id}/robots");
        Assert.Equal(["total", "offset", "respected", "userAgent", "blocked", "origins"], origins.AsObject().Select(property => property.Key));
        Assert.Equal((true, "ShoutingIguana", 1L, 0), ((bool)origins["respected"]!, (string?)origins["userAgent"], (long)origins["blocked"]!, (int)origins["offset"]!));
        var first = origins["origins"]![0]!;
        Assert.Equal(["origin", "isInternal", "state", "statusCode", "fetchedAt", "error", "bytes", "blocked"], first.AsObject().Select(property => property.Key));
        Assert.Equal((site, true, "read", 200, 1L), ((string?)first["origin"], (bool)first["isInternal"]!, (string?)first["state"], (int)first["statusCode"]!, (long)first["blocked"]!));
        Assert.EndsWith("Z", (string?)first["fetchedAt"]);
        Assert.Contains(origins["origins"]!.AsArray(), origin => (string?)origin!["state"] == "missing" && !(bool)origin["isInternal"]!);

        // The site's https twin never answered, and isn't the start URL's origin: the crawl gave no verdict and went ahead.
        var twin = Assert.Single(origins["origins"]!.AsArray(), origin => (string?)origin!["origin"] == "https" + site[4..])!;
        Assert.Equal((true, "unanswered", null, 0L), ((bool)twin["isInternal"]!, (string?)twin["state"], (int?)twin["statusCode"], (long)twin["blocked"]!));
        Assert.NotNull((string?)twin["error"]);
        Assert.Equal((long)origins["total"]!, origins["origins"]!.AsArray().Count);
        var paged = await PostOkAsync($"api/crawls/{id}/robots", JsonNode.Parse("""{"offset":1,"limit":1}"""));
        Assert.Equal(origins["origins"]![1]!.ToJsonString(), Assert.Single(paged["origins"]!.AsArray())!.ToJsonString());

        // The file, line by line, with what its rule blocked.
        var file = await PostOkAsync($"api/crawls/{id}/robots/file", new JsonObject { ["origin"] = site });
        Assert.Equal(["origin", "isInternal", "state", "statusCode", "fetchedAt", "error", "bytes", "blocked", "userAgent", "lines", "unmatched"], file.AsObject().Select(property => property.Key));
        Assert.Equal(first.ToJsonString(), new JsonObject(first.AsObject().Select(property => KeyValuePair.Create(property.Key, file[property.Key]?.DeepClone()))).ToJsonString());
        Assert.Equal("""{"number":1,"text":"User-agent: *","kind":"userAgent","applies":true,"blocked":null,"problem":null}""", file["lines"]![0]!.ToJsonString());
        Assert.Equal("""{"number":2,"text":"Disallow: /private/","kind":"disallow","applies":true,"blocked":1,"problem":null}""", file["lines"]![1]!.ToJsonString());
        Assert.Equal(["blank", "sitemap"], file["lines"]!.AsArray().Skip(2).Select(line => (string?)line!["kind"]));
        Assert.Equal("""{"unreachable":0,"unknown":0}""", file["unmatched"]!.ToJsonString());

        // The blocked URLs, each with the line that refused it; narrowed by line and by reason.
        var blocked = await PostOkAsync($"api/crawls/{id}/robots/blocked", new JsonObject { ["origin"] = site });
        Assert.Equal(["origin", "total", "matching", "offset", "rows"], blocked.AsObject().Select(property => property.Key));
        var row = Assert.Single(blocked["rows"]!.AsArray())!;
        Assert.Equal(["id", "url", "reason", "line", "rule", "error"], row.AsObject().Select(property => property.Key));
        Assert.Equal((grid.Url("/private/1"), "rule", 2, "Disallow: /private/"), ((string?)row["url"], (string?)row["reason"], (int)row["line"]!, (string?)row["rule"]));
        Assert.Equal((1L, 1L), ((long)blocked["total"]!, (long)blocked["matching"]!));
        Assert.Equal((1L, 0L), await MatchingAsync(new JsonObject { ["origin"] = site, ["line"] = 1 }));
        Assert.Equal((1L, 1L), await MatchingAsync(new JsonObject { ["origin"] = site, ["line"] = 2, ["reason"] = "rule" }));
        Assert.Equal((1L, 0L), await MatchingAsync(new JsonObject { ["origin"] = site, ["reason"] = "unknown" }));

        // The URL the inspector shows for it says the same: refused, with no response to show.
        var inspected = await PostOkAsync($"api/crawls/{id}/urls/inspect", new JsonObject { ["urlId"] = (int)row["id"]! });
        Assert.Equal(("robotsDisallowed", true), ((string?)inspected["response"]!["outcome"], (bool)inspected["response"]!["fetched"]!));
        Assert.Null(inspected["response"]!["statusCode"]);

        foreach (var path in new[] { "robots/file", "robots/blocked" })
        {
            TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{id}/{path}", new JsonObject { ["origin"] = "https://elsewhere.example" }), HttpStatusCode.NotFound, "origin-not-found");
            var none = TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{id}/{path}", new JsonObject()), HttpStatusCode.UnprocessableEntity, "invalid-query");
            Assert.Contains("origin", none["errors"]!.AsObject().Select(error => error.Key));
            TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{id}/{path}", JsonNode.Parse("""{"origin":7}""")), HttpStatusCode.BadRequest, "bad-request");
            TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{id}/{path}"), HttpStatusCode.BadRequest, "bad-request");
        }

        var reason = TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{id}/robots/blocked", new JsonObject { ["origin"] = site, ["reason"] = "because" }),
            HttpStatusCode.UnprocessableEntity, "invalid-query");
        Assert.Contains("reason", reason["errors"]!.AsObject().Select(error => error.Key));
        Assert.Contains("limit", TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{id}/robots", JsonNode.Parse("""{"limit":0}""")),
            HttpStatusCode.UnprocessableEntity, "invalid-query")["errors"]!.AsObject().Select(error => error.Key));

        async Task<(long Total, long Matching)> MatchingAsync(JsonNode body)
        {
            var page = await PostOkAsync($"api/crawls/{id}/robots/blocked", body);
            return ((long)page["total"]!, (long)page["matching"]!);
        }
    }

    private async Task<JsonNode> PostOkAsync(string path, JsonNode? body = null)
    {
        var (status, answer) = await Sidecar.PostAsync(path, body);
        Assert.True(status == HttpStatusCode.OK, $"POST {path}: {(int)status} {answer?.ToJsonString()}");
        return answer!;
    }

    private static JsonNode With(JsonNode query, string name, string value)
    {
        var copy = query.DeepClone();
        copy[name] = value;
        return copy;
    }
}
