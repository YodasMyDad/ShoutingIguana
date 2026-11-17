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
        Assert.Equal(["id", "type", "needsAnalysis", "sortable", "filterable", "values", "title", "unit", "valueTitles"], columns[0].AsObject().Select(property => property.Key));
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

    /// <summary>
    /// From a view of the links to its URLs (E6): the view's ends are listed, each once with its links, and the URL grids
    /// narrow to them, to the list's own count.
    /// </summary>
    [Fact]
    public async Task AViewOfTheLinksListsItsEndsAndTheUrlGridsNarrowToThem()
    {
        var view = JsonNode.Parse("""{"filters":[{"column":"kind","op":"in","values":["hyperlink"]},{"column":"to.statusClass","op":"in","values":["3xx","4xx"]}]}""")!;
        var links = (long)(await PostOkAsync($"api/crawls/{grid.Id}/links", view))["matching"]!;
        Assert.True(links > 0);
        foreach (var end in new[] { "from", "to" })
        {
            var asked = With(view, "end", end);
            var first = await PostOkAsync($"api/crawls/{grid.Id}/links/ends", asked);
            Assert.Equal(["end", "total", "links", "offset", "rows", "summary"], first.AsObject().Select(property => property.Key));
            Assert.Equal(["id", "url", "links"], first["rows"]![0]!.AsObject().Select(property => property.Key));
            Assert.Equal((end, links, 0, """{"available":true,"reason":null}"""), ((string?)first["end"], (long)first["links"]!, (int)first["offset"]!, first["summary"]!.ToJsonString()));
            var total = (long)first["total"]!;
            Assert.InRange(total, 2, Math.Min(links, 999));

            // Paged to its end: every end once, in the order the crawl found the URLs, and their links are the view's.
            List<JsonNode> rows = [];
            for (var offset = 0; rows.Count == offset; offset += 7)
            {
                var page = await PostOkAsync($"api/crawls/{grid.Id}/links/ends", new JsonObject
                {
                    ["end"] = end, ["filters"] = view["filters"]!.DeepClone(), ["offset"] = offset, ["limit"] = 7
                });
                Assert.Equal((total, links, offset), ((long)page["total"]!, (long)page["links"]!, (int)page["offset"]!));
                rows.AddRange(page["rows"]!.AsArray().Select(row => row!));
            }

            Assert.Equal(total, rows.Count);
            Assert.Equal(rows.Select(row => (int)row["id"]!).Distinct().Order(), rows.Select(row => (int)row["id"]!));
            Assert.Equal(links, rows.Sum(row => (long)row["links"]!));

            // The URL grid narrowed to them lists the same URLs, to the same count; its facets count them; and a filter of
            // the grid's own narrows them further.
            var narrowed = new JsonObject { ["linkEnds"] = asked.DeepClone(), ["columns"] = new JsonArray("url"), ["limit"] = 1_000 };
            var urls = await PostOkAsync($"api/crawls/{grid.Id}/urls", narrowed);
            Assert.Equal(total, (long)urls["matching"]!);
            Assert.Equal(rows.Select(row => ((int)row["id"]!, (string?)row["url"])), urls["rows"]!.AsArray().Select(row => ((int)row!["id"]!, (string?)row["url"])));
            var facets = await PostOkAsync($"api/crawls/{grid.Id}/urls/facets", new JsonObject { ["linkEnds"] = asked.DeepClone(), ["facets"] = new JsonArray("statusClass") });
            Assert.Equal((total, total), ((long)facets["matching"]!, facets["facets"]!["statusClass"]!.AsArray().Sum(bucket => (long)bucket!["count"]!)));
            var some = With(narrowed, "search", "/p/");
            Assert.Equal(rows.Count(row => ((string)row["url"]!).Contains("/p/", StringComparison.Ordinal)), (long)(await PostOkAsync($"api/crawls/{grid.Id}/urls", some))["matching"]!);
        }

        // A check's rows and a sitemap's URLs narrow the same way: a page with a long title has links on it, and none of
        // them says what no link says.
        var flagged = await PostOkAsync($"api/crawls/{grid.Id}/issues/title-too-long/rows", JsonNode.Parse("""{"linkEnds":{"end":"from"},"limit":1}"""));
        Assert.True((long)flagged["total"]! > 0);
        Assert.Equal((long)flagged["total"]!, (long)flagged["matching"]!);
        Assert.Equal(0, (long)(await PostOkAsync($"api/crawls/{grid.Id}/issues/title-too-long/rows",
            JsonNode.Parse("""{"linkEnds":{"end":"from","filters":[{"column":"text","op":"contains","value":"no link says this at all"}]}}""")))["matching"]!);
        var sitemap = (int)(await Sidecar.GetJsonAsync($"api/crawls/{grid.Id}/sitemaps"))["sitemaps"]![1]!["id"]!;
        var listed = await PostOkAsync($"api/crawls/{grid.Id}/sitemaps/urls", new JsonObject { ["sitemapId"] = sitemap, ["linkEnds"] = new JsonObject { ["end"] = "to" }, ["limit"] = 1 });
        Assert.InRange((long)listed["matching"]!, 1, (long)listed["total"]!);

        // What can't be answered says why, by field; the links grid takes the filters themselves.
        async Task<IEnumerable<string>> RefusedAsync(string path, string body) =>
            TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/{path}", JsonNode.Parse(body)), HttpStatusCode.UnprocessableEntity, "invalid-query")["errors"]!
                .AsObject().Select(error => error.Key);

        Assert.Equal(["end"], await RefusedAsync("links/ends", "{}"));
        Assert.Equal(["end"], await RefusedAsync("links/ends", """{"end":"both"}"""));
        Assert.Equal(["filters[0].column"], await RefusedAsync("links/ends", """{"end":"to","filters":[{"column":"anchor","op":"contains","value":"a"}]}"""));
        Assert.Equal(["limit"], await RefusedAsync("links/ends", """{"end":"to","limit":1001}"""));
        Assert.Equal(["linkEnds.end"], await RefusedAsync("urls", """{"linkEnds":{}}"""));
        Assert.Equal(["linkEnds.filters[0].column"], await RefusedAsync("urls", """{"linkEnds":{"end":"from","filters":[{"column":"anchor","op":"contains","value":"a"}]}}"""));
        Assert.Equal(["linkEnds.filters[0].op"], await RefusedAsync("urls/facets", """{"linkEnds":{"end":"from","filters":[{"column":"text","op":"gt","value":4}]}}"""));
        foreach (var (path, body) in new[]
                 {
                     ("links", """{"linkEnds":{"end":"to"}}"""), ("links/facets", """{"linkEnds":{"end":"to"}}"""), ("links/ends", """{"end":"to","sort":[]}"""),
                     ("urls", """{"linkEnds":{"end":"to","limit":5}}"""), ("links/ends", """{"end":7}""")
                 })
        {
            TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/{path}", JsonNode.Parse(body)), HttpStatusCode.BadRequest, "bad-request");
        }

        TestSidecar.AssertProblem(await Sidecar.PostAsync("api/crawls/0000000000000000/links/ends", JsonNode.Parse("""{"end":"to"}""")), HttpStatusCode.NotFound, "crawl-not-found");

        // A crawl never analysed lists its ends from the links themselves, and refuses a link filter that needs the analysis.
        var freshLinks = (long)(await PostOkAsync($"api/crawls/{grid.FreshId}/links", JsonNode.Parse("""{"filters":[{"column":"kind","op":"in","values":["hyperlink"]}],"limit":1}""")))["matching"]!;
        var fresh = await PostOkAsync($"api/crawls/{grid.FreshId}/links/ends", JsonNode.Parse("""{"end":"from","filters":[{"column":"kind","op":"in","values":["hyperlink"]}],"limit":3}"""));
        Assert.True(freshLinks > 3, $"The crawl never analysed has {freshLinks} hyperlinks.");
        Assert.Equal((freshLinks, 3, """{"available":false,"reason":"notAnalyzed"}"""), ((long)fresh["links"]!, fresh["rows"]!.AsArray().Count, fresh["summary"]!.ToJsonString()));
        Assert.Equal((long)fresh["total"]!, (long)(await PostOkAsync($"api/crawls/{grid.FreshId}/urls",
            JsonNode.Parse("""{"linkEnds":{"end":"from","filters":[{"column":"kind","op":"in","values":["hyperlink"]}]},"limit":1}""")))["matching"]!);
        var needs = TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.FreshId}/links/ends",
            JsonNode.Parse("""{"end":"from","filters":[{"column":"to.inlinks","op":"gt","value":3}]}""")), HttpStatusCode.Conflict, "needs-analysis");
        Assert.Equal("""["to.inlinks"]""", needs["columns"]!.ToJsonString());
    }

    [Fact]
    public async Task TheSitemapsAreListedAndOneOpensItsUrls()
    {
        var list = await Sidecar.GetJsonAsync($"api/crawls/{grid.Id}/sitemaps");
        Assert.Equal(["discovery", "sitemaps", "summary"], list.AsObject().Select(property => property.Key));
        Assert.Equal(("done", """{"available":true,"reason":null}"""), ((string?)list["discovery"], list["summary"]!.ToJsonString()));
        var sitemaps = list["sitemaps"]!.AsArray();
        var index = sitemaps[0]!;
        Assert.Equal(["id", "url", "parentId", "foundVia", "state", "statusCode", "isIndex", "entries", "sitemaps", "error", "urls", "listedSitemaps"],
            index.AsObject().Select(property => property.Key));

        // The index says how many sitemaps its file lists, beside how many of them the crawl has; a sitemap of URLs lists
        // none, and one that couldn't be read doesn't say.
        Assert.Equal([3, 0, 0, null], sitemaps.Select(sitemap => (int?)sitemap!["listedSitemaps"]));
        Assert.Equal((grid.Url("/sitemap.xml"), "robotsTxt", "read", true, 3), ((string?)index["url"], (string?)index["foundVia"], (string?)index["state"], (bool)index["isIndex"]!, (int)index["sitemaps"]!));
        Assert.Null(index["parentId"]);
        Assert.Equal(["listed", "fetched", "indexable", "nonIndexable", "robotsDisallowed", "redirectingOrBroken"], index["urls"]!.AsObject().Select(property => property.Key));
        Assert.Equal((1, 2), ((long)index["urls"]!["robotsDisallowed"]!, (long)index["urls"]!["redirectingOrBroken"]!));
        var missing = sitemaps[^1]!;
        Assert.Equal((grid.Url("/sitemaps/missing.xml"), "index", "failed", 404, "404 Not Found", (int)index["id"]!),
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
            Assert.Equal((long)sitemap["urls"]!["robotsDisallowed"]!, await OpensAsync("""{"column":"outcome","op":"in","values":["robotsDisallowed"]}"""));
            Assert.Equal((long)sitemap["urls"]!["redirectingOrBroken"]!,
                await OpensAsync("""{"column":"outcome","op":"in","values":["redirect","clientError","serverError","timeout","networkError"]}"""));
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
        // The site's sitemap lists one URL robots.txt refuses: the only one of the site's this small crawl comes to. The
        // partner site's own robots.txt refuses one of its pages, which a planted page links.
        var id = grid.Id;
        var site = grid.Url(string.Empty);

        var origins = await PostOkAsync($"api/crawls/{id}/robots");
        Assert.Equal(["total", "offset", "respected", "userAgent", "blocked", "origins", "matching", "counts"], origins.AsObject().Select(property => property.Key));
        var partner = (string?)Assert.Single(origins["origins"]!.AsArray(), origin => !(bool)origin!["isInternal"]! && (long)origin["blocked"]! > 0)!["origin"];

        // With nothing asked every origin matches; the counts are what a filter bar shows, each the rows its filter gives.
        Assert.Equal((long)origins["total"]!, (long)origins["matching"]!);
        Assert.Equal($$"""{"all":{{(long)origins["total"]!}},"blocked":2,"notBlocked":{{(long)origins["total"]! - 2}}}""", origins["counts"]!.ToJsonString());
        var blocking = await PostOkAsync($"api/crawls/{id}/robots", JsonNode.Parse("""{"blocked":true}"""));
        Assert.Equal([site, partner], blocking["origins"]!.AsArray().Select(origin => (string?)origin!["origin"]));
        Assert.Equal((2L, (long)origins["total"]!, origins["counts"]!.ToJsonString()),
            ((long)blocking["matching"]!, (long)blocking["total"]!, blocking["counts"]!.ToJsonString()));
        var clear = await PostOkAsync($"api/crawls/{id}/robots", JsonNode.Parse("""{"blocked":false}"""));
        Assert.Equal((long)origins["total"]! - 2, (long)clear["matching"]!);
        Assert.DoesNotContain(clear["origins"]!.AsArray(), origin => (string?)origin!["origin"] == site);

        // Found by a text in the name, whatever its case; the counts are then of what the text finds.
        var found = await PostOkAsync($"api/crawls/{id}/robots", new JsonObject { ["search"] = "HTTPS://" });
        Assert.NotEmpty(found["origins"]!.AsArray());
        Assert.All(found["origins"]!.AsArray(), origin => Assert.StartsWith("https://", (string?)origin!["origin"], StringComparison.Ordinal));
        Assert.Equal(((long)found["matching"]!, 0L, (long)found["matching"]!, (long)origins["total"]!),
            ((long)found["counts"]!["all"]!, (long)found["counts"]!["blocked"]!, (long)found["counts"]!["notBlocked"]!, (long)found["total"]!));
        Assert.True((long)found["matching"]! < (long)origins["total"]!);
        Assert.Equal(0, (long)(await PostOkAsync($"api/crawls/{id}/robots", new JsonObject { ["search"] = "HTTPS://", ["blocked"] = true }))["matching"]!);
        Assert.Empty((await PostOkAsync($"api/crawls/{id}/robots", new JsonObject { ["search"] = "no origin has this" }))["origins"]!.AsArray());
        Assert.Contains("search", TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{id}/robots", new JsonObject { ["search"] = new string('a', 1_001) }),
            HttpStatusCode.UnprocessableEntity, "invalid-query")["errors"]!.AsObject().Select(error => error.Key));
        TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{id}/robots", JsonNode.Parse("""{"blocked":"yes"}""")), HttpStatusCode.BadRequest, "bad-request");
        TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{id}/robots", JsonNode.Parse("""{"search":7}""")), HttpStatusCode.BadRequest, "bad-request");
        TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{id}/robots", JsonNode.Parse("""{"filter":"blocked"}""")), HttpStatusCode.BadRequest, "bad-request");
        Assert.Equal((true, "ShoutingIguana", 2L, 0), ((bool)origins["respected"]!, (string?)origins["userAgent"], (long)origins["blocked"]!, (int)origins["offset"]!));
        var first = origins["origins"]![0]!;
        Assert.Equal(["origin", "isInternal", "state", "statusCode", "fetchedAt", "error", "bytes", "blocked"], first.AsObject().Select(property => property.Key));
        Assert.Equal((site, true, "read", 200, 1L), ((string?)first["origin"], (bool)first["isInternal"]!, (string?)first["state"], (int)first["statusCode"]!, (long)first["blocked"]!));
        Assert.EndsWith("Z", (string?)first["fetchedAt"]);
        Assert.Contains(origins["origins"]!.AsArray(), origin => (string?)origin!["origin"] == partner && (string?)origin["state"] == "read" && !(bool)origin["isInternal"]!);

        // Another site's robots.txt that answers 404 is missing.
        var missing = grid.Copy(copy => Change(copy, $"UPDATE robots SET status_code = 404, content = NULL WHERE origin = '{partner}'"));
        Assert.Contains((await PostOkAsync($"api/crawls/{missing}/robots"))["origins"]!.AsArray(),
            origin => (string?)origin!["origin"] == partner && (string?)origin["state"] == "missing" && !(bool)origin["isInternal"]!);

        // The site's https twin never answered, and isn't the start URL's origin: the crawl gave no verdict and went ahead.
        var twin = Assert.Single(origins["origins"]!.AsArray(), origin => (string?)origin!["origin"] == "https" + site[4..])!;
        Assert.Equal((true, "unanswered", null, 0L), ((bool)twin["isInternal"]!, (string?)twin["state"], (int?)twin["statusCode"], (long)twin["blocked"]!));
        Assert.NotNull((string?)twin["error"]);
        Assert.Equal((long)origins["total"]!, origins["origins"]!.AsArray().Count);
        var paged = await PostOkAsync($"api/crawls/{id}/robots", JsonNode.Parse("""{"offset":1,"limit":1}"""));
        Assert.Equal(origins["origins"]![1]!.ToJsonString(), Assert.Single(paged["origins"]!.AsArray())!.ToJsonString());

        // The file, line by line, with what its rule blocked.
        var file = await PostOkAsync($"api/crawls/{id}/robots/file", new JsonObject { ["origin"] = site });
        Assert.Equal(["origin", "isInternal", "state", "statusCode", "fetchedAt", "error", "bytes", "blocked", "userAgent", "lines", "unmatched", "respected"],
            file.AsObject().Select(property => property.Key));

        // The file says whether the crawl obeyed robots.txt, as the list does, and a crawl told to ignore it says so.
        Assert.Equal(((bool)origins["respected"]!, true), ((bool)file["respected"]!, (bool)file["respected"]!));
        var ignoring = grid.Copy(copy => Change(copy, "UPDATE crawl SET options_json = json_set(options_json, '$.respectRobotsTxt', json('false'))"));
        Assert.False((bool)(await PostOkAsync($"api/crawls/{ignoring}/robots/file", new JsonObject { ["origin"] = site }))["respected"]!);
        Assert.False((bool)(await PostOkAsync($"api/crawls/{ignoring}/robots"))["respected"]!);
        Assert.Equal(first.ToJsonString(), new JsonObject(first.AsObject().Select(property => KeyValuePair.Create(property.Key, file[property.Key]?.DeepClone()))).ToJsonString());
        Assert.Equal("""{"number":1,"text":"User-agent: *","kind":"userAgent","applies":true,"blocked":null,"problem":null}""", file["lines"]![0]!.ToJsonString());
        Assert.Equal("""{"number":2,"text":"Disallow: /private/","kind":"disallow","applies":true,"blocked":1,"problem":null}""", file["lines"]![1]!.ToJsonString());
        Assert.Equal(["blank", "sitemap"], file["lines"]!.AsArray().Skip(2).Select(line => (string?)line!["kind"]));
        Assert.Equal("""{"unreachable":0,"unknown":0}""", file["unmatched"]!.ToJsonString());

        // The blocked URLs, each with the line that refused it; narrowed by line and by reason.
        var blocked = await PostOkAsync($"api/crawls/{id}/robots/blocked", new JsonObject { ["origin"] = site });
        Assert.Equal(["origin", "total", "matching", "offset", "rows", "conflicts"], blocked.AsObject().Select(property => property.Key));
        var row = Assert.Single(blocked["rows"]!.AsArray())!;
        Assert.Equal(["id", "url", "reason", "line", "rule", "error", "linkingPages", "inSitemap", "sitemaps", "sitemap", "inMenu", "conflict"],
            row.AsObject().Select(property => property.Key));
        Assert.Equal((grid.Url("/private/listed"), "rule", 2, "Disallow: /private/"), ((string?)row["url"], (string?)row["reason"], (int)row["line"]!, (string?)row["rule"]));
        Assert.Equal((1L, 1L, 1L), ((long)blocked["total"]!, (long)blocked["matching"]!, (long)blocked["conflicts"]!));
        Assert.Equal((1L, 0L), await MatchingAsync(new JsonObject { ["origin"] = site, ["line"] = 1 }));
        Assert.Equal((1L, 1L), await MatchingAsync(new JsonObject { ["origin"] = site, ["line"] = 2, ["reason"] = "rule" }));
        Assert.Equal((1L, 0L), await MatchingAsync(new JsonObject { ["origin"] = site, ["reason"] = "unknown" }));

        // What blocking it costs: no page links to it, but the site's sitemap lists it, so it's a conflict; the
        // conflicts can be listed alone.
        var listing = (await Sidecar.GetJsonAsync($"api/crawls/{id}/sitemaps"))["sitemaps"]!.AsArray()
            .Single(sitemap => ((string)sitemap!["url"]!).EndsWith("/sitemaps/extra.xml", StringComparison.Ordinal))!;
        Assert.Equal($$"""{"linkingPages":0,"inSitemap":true,"sitemaps":1,"sitemap":{"id":{{(int)listing["id"]!}},"url":"{{(string)listing["url"]!}}"},"inMenu":false,"conflict":true}""",
            new JsonObject(row.AsObject().Skip(6).Select(property => KeyValuePair.Create(property.Key, property.Value?.DeepClone()))).ToJsonString());
        Assert.Equal((1L, 1L), await MatchingAsync(new JsonObject { ["origin"] = site, ["conflict"] = true }));
        Assert.Equal((1L, 0L), await MatchingAsync(new JsonObject { ["origin"] = site, ["conflict"] = false }));
        TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{id}/robots/blocked", JsonNode.Parse($$"""{"origin":"{{site}}","conflict":"yes"}""")),
            HttpStatusCode.BadRequest, "bad-request");

        // The URL the inspector shows for it says the same: refused, with no response to show.
        var inspected = await PostOkAsync($"api/crawls/{id}/urls/inspect", new JsonObject { ["urlId"] = (int)row["id"]! });
        Assert.Equal(("robotsDisallowed", true), ((string?)inspected["response"]!["outcome"], (bool)inspected["response"]!["fetched"]!));
        Assert.Null(inspected["response"]!["statusCode"]);

        // And names the origin and the line that refused it, as the row does, so its page opens that file at that line;
        // and the sitemap that lists it, by the id the Sitemaps screen opens.
        var refused = Assert.Single(inspected["indexability"]!["tests"]!.AsArray(), test => (string?)test!["test"] == "robotsTxt")!;
        Assert.Equal("""{"test":"robotsTxt","outcome":"fail","unreachable":false,"origin":"SITE","reason":"rule","line":2,"rule":"Disallow: /private/"}""".Replace("SITE", site, StringComparison.Ordinal),
            refused.ToJsonString());
        var sitemaps = (await Sidecar.GetJsonAsync($"api/crawls/{id}/sitemaps"))["sitemaps"]!.AsArray();
        var lists = Assert.Single(sitemaps, sitemap => ((string)sitemap!["url"]!).EndsWith("/sitemaps/extra.xml", StringComparison.Ordinal))!;
        Assert.Equal($$"""{"total":1,"sitemaps":[{"id":{{(int)lists["id"]!}},"url":"{{(string)lists["url"]!}}"}]}""", inspected["listedIn"]!.ToJsonString());
        Assert.Equal("listedIn", inspected.AsObject().Last().Key);

        // The list answers for that URL alone: its row, or none for a URL the origin didn't block.
        var one = await PostOkAsync($"api/crawls/{id}/robots/blocked", new JsonObject { ["origin"] = site, ["urlId"] = (int)row["id"]! });
        Assert.Equal((row.ToJsonString(), 1L, 1L), (Assert.Single(one["rows"]!.AsArray())!.ToJsonString(), (long)one["total"]!, (long)one["matching"]!));
        var home = (int)(await PostOkAsync($"api/crawls/{id}/urls/lookup", new JsonObject { ["url"] = "/" }))["id"]!;
        Assert.Equal((1L, 0L), await MatchingAsync(new JsonObject { ["origin"] = site, ["urlId"] = home }));
        Assert.Equal((1L, 0L), await MatchingAsync(new JsonObject { ["origin"] = site, ["urlId"] = 0 }));
        Assert.Equal((1L, 0L), await MatchingAsync(new JsonObject { ["origin"] = site, ["urlId"] = 99_999_999_999 }));
        TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{id}/robots/blocked", JsonNode.Parse($$"""{"origin":"{{site}}","urlId":"one"}""")), HttpStatusCode.BadRequest, "bad-request");

        // A page the crawl fetched names its origin, whose robots.txt allowed it, and nothing else; a page no sitemap lists, none.
        var allowed = Assert.Single((await PostOkAsync($"api/crawls/{id}/urls/inspect", new JsonObject { ["urlId"] = home }))["indexability"]!["tests"]!.AsArray(),
            test => (string?)test!["test"] == "robotsTxt")!;
        Assert.Equal("""{"test":"robotsTxt","outcome":"pass","origin":"SITE"}""".Replace("SITE", site, StringComparison.Ordinal), allowed.ToJsonString());
        var unlisted = (int)(await PostOkAsync($"api/crawls/{id}/urls", JsonNode.Parse("""{"filters":[{"column":"type","op":"in","values":["stylesheet"]}],"limit":1}""")))["rows"]![0]!["id"]!;
        Assert.Equal("""{"total":0,"sitemaps":[]}""", (await PostOkAsync($"api/crawls/{id}/urls/inspect", new JsonObject { ["urlId"] = unlisted }))["listedIn"]!.ToJsonString());

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

    private static void Change(string path, string sql)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static JsonNode With(JsonNode query, string name, string value)
    {
        var copy = query.DeepClone();
        copy[name] = value;
        return copy;
    }
}
