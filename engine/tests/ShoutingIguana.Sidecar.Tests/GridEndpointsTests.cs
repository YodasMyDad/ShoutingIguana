using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Sidecar.Tests.Support;

namespace ShoutingIguana.Sidecar.Tests;

/// <summary>
/// The grids' endpoints as the UI calls them: the JSON each gives, every count opening its URLs, paging by offset and by
/// cursor alike, the problems for a query that can't run, and what changes without a URL summary.
/// </summary>
public sealed class GridEndpointsTests(GridSidecar grid) : IClassFixture<GridSidecar>
{
    private TestSidecar Sidecar => grid.Sidecar;

    /// <summary>
    /// Never a count over an empty list (round 4): every check that found something, in the whole catalog, lists its URLs.
    /// Its first page of rows isn't empty and matches its count; a grouped check has groups, as many URLs as its count
    /// in them (a check grouped by the other URL has a page in a group per URL it names, so at least its count).
    /// </summary>
    [Fact]
    public async Task EveryCheckThatFoundSomethingListsItsUrls()
    {
        var counts = (await Sidecar.GetJsonAsync($"api/crawls/{grid.Id}/analysis"))["counts"]!.AsArray()
            .Select(count => ((string)count!["checkId"]!, (long)count["count"]!)).Where(count => count.Item2 > 0).ToList();
        Assert.True(counts.Count >= 40, $"only {counts.Count} checks found something on the synthetic site");
        foreach (var (checkId, count) in counts)
        {
            var rows = await PostOkAsync($"api/crawls/{grid.Id}/issues/{checkId}/rows", JsonNode.Parse("""{"limit":5}"""));
            Assert.True(rows["rows"]!.AsArray().Count > 0, $"{checkId}: {count} flagged, no rows");
            Assert.True((long)rows["matching"]! == count, $"{checkId}: {count} flagged, {rows["matching"]} rows");

            var grouping = IssueCatalog.Find(checkId)!.Grouping;
            if (grouping == IssueGrouping.None)
            {
                continue;
            }

            long pages = 0;
            long groupCount = 0;
            for (var offset = 0; ; offset += 1_000)
            {
                var groups = await PostOkAsync($"api/crawls/{grid.Id}/issues/{checkId}/groups", JsonNode.Parse($$"""{"offset":{{offset}},"limit":1000}"""));
                Assert.Equal(count, (long)groups["total"]!);
                groupCount = (long)groups["groupCount"]!;
                pages += groups["groups"]!.AsArray().Sum(group => (long)group!["pages"]!);
                if (offset + 1_000 >= groupCount)
                {
                    break;
                }
            }

            Assert.True(groupCount > 0, $"{checkId}: {count} flagged, no groups");
            Assert.True(grouping == IssueGrouping.RelatedUrl ? pages >= count : pages == count, $"{checkId}: {count} flagged, {pages} in its groups");
        }
    }

    /// <summary>
    /// The analysis says what each check comes to as work, and what a fix of it clears, as the engine counts them; the
    /// catalog says which checks a fix can clear.
    /// </summary>
    [Fact]
    public async Task TheAnalysisSaysTheThingsToChangeAndWhatAFixClears()
    {
        var engine = IssueCauseReader.Read(grid.Path)!.Checks;
        var counts = (await Sidecar.GetJsonAsync($"api/crawls/{grid.Id}/analysis"))["counts"]!.AsArray().Select(count => count!).ToList();
        Assert.All(counts, count => Assert.Equal(["checkId", "count", "likelyTemplate", "causes", "templateCauses", "clears", "files", "platform"],
            count.AsObject().Select(property => property.Key)));
        foreach (var count in counts)
        {
            var id = (string)count["checkId"]!;
            if ((long)count["count"]! == 0)
            {
                Assert.Equal((null, null, "[]"), ((long?)count["causes"], (long?)count["templateCauses"], count["clears"]!.ToJsonString()));
                continue;
            }

            Assert.Equal((engine[id].Causes, engine[id].InTemplate), ((long)count["causes"]!, (long)count["templateCauses"]!));
            Assert.Equal(engine[id].Clears.Select(cleared => (cleared.CheckId, cleared.Pages, cleared.All)),
                count["clears"]!.AsArray().Select(cleared => ((string)cleared!["checkId"]!, (long)cleared["pages"]!, (bool)cleared["all"]!)));
        }

        var cleared = counts.Single(count => (string?)count["checkId"] == "internal-client-error")["clears"]!.AsArray();
        Assert.Contains(cleared, entry => (string?)entry!["checkId"] == "broken-internal-links" && (long)entry["pages"]! > 0);

        var checks = (await Sidecar.GetJsonAsync("api/checks"))["checks"]!.AsArray();
        Assert.Equal("""["links-to-redirects","pagination-broken"]""", checks.Single(check => (string?)check!["id"] == "redirect-chain")!["clears"]!.ToJsonString());
        Assert.Equal("[]", checks.Single(check => (string?)check!["id"] == "title-too-long")!["clears"]!.ToJsonString());
    }

    /// <summary>
    /// The fix-it pass's contract: a check of the site's robots.txt or sitemaps says so (<c>about</c>), and its count names
    /// the files with each sitemap's id, for a view to show instead of the home page; every other check's count names none.
    /// And a check says what order its rows come in (<c>rowOrder</c>).
    /// </summary>
    [Fact]
    public async Task AFileChecksCountNamesItsFilesAndEachCheckSaysItsOrder()
    {
        var checks = (await Sidecar.GetJsonAsync("api/checks"))["checks"]!.AsArray().Select(check => check!).ToDictionary(check => (string)check["id"]!);
        Assert.Equal("sitemap", (string?)checks["sitemap-error"]["about"]);
        Assert.Equal("robotsTxt", (string?)checks["robots-txt-no-sitemap"]["about"]);
        Assert.Null(checks["title-too-long"]["about"]);
        Assert.Equal("fewestLinksFirst", (string?)checks["not-in-sitemap"]["rowOrder"]);
        Assert.Equal("importance", (string?)checks["title-too-long"]["rowOrder"]);

        var counts = (await Sidecar.GetJsonAsync($"api/crawls/{grid.Id}/analysis"))["counts"]!.AsArray().Select(count => count!).ToList();
        var stored = IssueFileReader.Read(grid.Path)!.Checks;
        foreach (var count in counts)
        {
            var id = (string)count["checkId"]!;
            if (stored.TryGetValue(id, out var files))
            {
                Assert.Equal(files.Select(file => (file.Url, file.SitemapId)),
                    count["files"]!.AsArray().Select(file => ((string)file!["url"]!, (long?)file["sitemapId"])));
            }
            else
            {
                Assert.Null(count["files"]);
            }
        }

        Assert.Contains(counts, count => (string?)count["checkId"] == "sitemap-error" && count["files"]!.AsArray().Count == 1);
    }

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
        Assert.All(columns, column => Assert.Contains((string?)column["type"], new[] { "number", "text", "enum", "boolean", "object" }));

        // Evidence: a redirect's hops are a record, shown only; where a file is used needs the analysis; an image's pixels are its own.
        var chain = columns.Single(column => (string?)column["id"] == "redirectChain");
        Assert.Equal(("object", "Redirect chain"), ((string?)chain["type"], (string?)chain["title"]));
        Assert.Equal((false, false, false, false), Flags(chain));
        Assert.Equal((true, true, true, false), Flags(columns.Single(column => (string?)column["id"] == "usedOn")));
        var width = columns.Single(column => (string?)column["id"] == "pixelWidth");
        Assert.Equal(("px", false), ((string?)width["unit"], (bool)width["needsAnalysis"]!));

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
        { """{"after":"not a cursor"}""", "after" },
        { $$"""{"hiddenChecks":["title-too-long","{{new string('x', 201)}}"]}""", "hiddenChecks" },
        { """{"hiddenChecks":[null]}""", "hiddenChecks" }
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
            ["id", "url", "urlLength", "isInternal", "type", "discovery", "response", "redirects", "indexability", "content", "limits", "links", "linkTotals",
             "headers", "structuredData", "hreflang", "issues", "namedBy", "summary", "source", "listedIn"],
            product.AsObject().Select(property => property.Key));
        Assert.Equal("page", (string?)product["type"]);
        Assert.Equal("success", (string?)product["response"]!["outcome"]);
        Assert.EndsWith("Z", (string?)product["response"]!["crawledAt"]);
        Assert.Equal("""{"stored":true}""", product["source"]!.ToJsonString());
        Assert.True((bool?)product["limits"]!["fromAnalysis"]);
        Assert.Equal(580, (int?)product["limits"]!["titleMaxPixels"]);

        // The checklist: each test with only the facts that go with it.
        var indexability = product["indexability"]!;
        Assert.True((bool?)indexability["indexable"]);
        Assert.Equal("none", (string?)indexability["reason"]);
        Assert.Equal("analysis", (string?)indexability["judgedBy"]);
        var tests = indexability["tests"]!.AsArray();
        Assert.Equal("""{"test":"answered","outcome":"pass","statusCode":200}""", tests[0]!.ToJsonString());
        Assert.Equal($$"""{"test":"robotsTxt","outcome":"pass","origin":"{{grid.Url(string.Empty)}}"}""", tests[1]!.ToJsonString());
        Assert.Equal("""{"test":"noindex","outcome":"pass","meta":false,"header":false}""", tests[2]!.ToJsonString());
        Assert.Equal(grid.Url("/p/7"), (string?)tests[3]!["canonical"]!["url"]);

        // A URL it names carries what became of the request for it beside its status; the URL itself, its length as the
        // check for long URLs measures it; and its lists of links, their totals of every kind.
        Assert.Equal(["id", "url", "statusCode", "outcome"], tests[3]!["canonical"]!.AsObject().Select(property => property.Key));
        Assert.Equal("success", (string?)tests[3]!["canonical"]!["outcome"]);
        Assert.Equal("success", (string?)product["redirects"]!["final"]!["outcome"]);
        Assert.Equal(grid.Url("/p/7").Length, (int?)product["urlLength"]);
        var inlinks = await PostOkAsync($"api/crawls/{grid.Id}/urls/inlinks", new JsonObject { ["urlId"] = IdOf("/p/7"), ["limit"] = 1 });
        var outlinks = await PostOkAsync($"api/crawls/{grid.Id}/urls/outlinks", new JsonObject { ["urlId"] = IdOf("/p/7"), ["limit"] = 1 });
        var hyperlinks = await PostOkAsync($"api/crawls/{grid.Id}/urls/inlinks", JsonNode.Parse($$"""{"urlId":{{IdOf("/p/7")}},"kinds":["hyperlink"],"limit":1}"""));
        Assert.Equal($$"""{"inlinks":{{inlinks["total"]}},"outlinks":{{outlinks["total"]}},"hyperlinkInlinks":{{hyperlinks["total"]}}}""", product["linkTotals"]!.ToJsonString());

        // The hyperlinks to it land in the links grid: the count there is the inspector's. For the home page, which the
        // site's Cloudflare page links to with every other, it's more than the analysis's inlinks, which leave that page out.
        foreach (var path in new[] { "/p/7", "/" })
        {
            var inspected = path == "/p/7" ? product : await InspectAsync(IdOf(path));
            var listed = await PostOkAsync($"api/crawls/{grid.Id}/links", JsonNode.Parse(
                $$"""{"filters":[{"column":"to.id","op":"eq","value":{{IdOf(path)}}},{"column":"kind","op":"in","values":["hyperlink"]},{"column":"selfLink","op":"is","value":false}],"limit":1}"""));
            Assert.Equal((long)listed["matching"]!, (long)inspected["linkTotals"]!["hyperlinkInlinks"]!);
            Assert.True((long)inspected["linkTotals"]!["hyperlinkInlinks"]! >= (long)inspected["links"]!["inlinks"]!);
        }

        Assert.True((long)(await InspectAsync(IdOf("/")))["linkTotals"]!["hyperlinkInlinks"]! > (long)(await InspectAsync(IdOf("/")))["links"]!["inlinks"]!);
        Assert.True((long)outlinks["total"]! > (long)product["links"]!["outlinks"]!, "A product page links to more than its hyperlinks: its images, its stylesheet.");

        var gone = await InspectAsync(IdOf("/plants/gone"));
        Assert.Equal("clientError", (string?)gone["redirects"]!["final"]!["outcome"]);
        Assert.Equal("clientError", (string?)gone["indexability"]!["reason"]);
        Assert.Equal("""{"test":"answered","outcome":"fail","statusCode":404}""", gone["indexability"]!["tests"]![0]!.ToJsonString());
        Assert.Null(gone["content"]);

        var chain = await InspectAsync(IdOf("/plants/chain-a"));
        Assert.Equal([grid.Url("/plants/chain-b"), grid.Url("/plants/chain-c"), grid.Url("/plants/thin")],
            chain["redirects"]!["hops"]!.AsArray().Select(hop => (string?)hop!["url"]));
        Assert.Equal("http", (string?)chain["redirects"]!["hops"]![0]!["via"]);

        // Each issue carries the key of its group, which opens that group's rows: for a check grouped by a fault, the fault,
        // where the issue's own detail says where in the page it is.
        var invalid = IdOf("/plants/jsonld-invalid");
        var issue = (await InspectAsync(invalid))["issues"]!.AsArray().Single(issue => (string?)issue!["checkId"] == "structured-data-invalid")!;
        Assert.Equal(["checkId", "severity", "detail", "related", "group"], issue.AsObject().Select(property => property.Key));
        Assert.Equal(["detail", "relatedUrlId"], issue["group"]!.AsObject().Select(property => property.Key));
        Assert.NotEqual((string?)issue["detail"], (string?)issue["group"]!["detail"]);
        var groups = await PostOkAsync($"api/crawls/{grid.Id}/issues/structured-data-invalid/groups");
        Assert.Contains(groups["groups"]!.AsArray(), group => (string?)group!["detail"] == (string?)issue["group"]!["detail"] && group["relatedUrlId"] is null);
        var groupRows = await PostOkAsync($"api/crawls/{grid.Id}/issues/structured-data-invalid/rows", new JsonObject { ["group"] = issue["group"]!.DeepClone() });
        Assert.Contains(groupRows["rows"]!.AsArray(), row => (int?)row!["id"] == invalid);

        // A check that isn't grouped has no group to open.
        var measured = (await InspectAsync(IdOf("/plants/title-long")))["issues"]!.AsArray();
        Assert.Contains(measured, found => (string?)found!["checkId"] == "title-too-long");
        Assert.All(measured, found => Assert.Equal(IssueCatalog.Find((string)found!["checkId"]!)!.Grouping == IssueGrouping.None, found["group"] is null));

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
        Assert.Equal(["id", "url", "kind", "text", "position", "flags", "followed", "fragment", "statusCode", "indexable", "fetched", "outcome"],
            link.AsObject().Select(property => property.Key));
        Assert.Equal("success", (string?)link["outcome"]);
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

        // Each link says what became of the request for its other end, not only its status: a page that's gone, and a
        // URL the crawl found and never asked for, which has neither.
        var gone = IdOf("/plants/gone");
        var linking = (int)(await PostOkAsync($"api/crawls/{grid.Id}/urls/inlinks", new JsonObject { ["urlId"] = gone, ["limit"] = 1 }))["rows"]![0]!["id"]!;
        var from = await PostOkAsync($"api/crawls/{grid.Id}/urls/outlinks", new JsonObject { ["urlId"] = linking, ["limit"] = 1_000 });
        var broken = from["rows"]!.AsArray().First(row => (int?)row!["id"] == gone)!;
        Assert.Equal(("clientError", 404, true), ((string?)broken["outcome"], (int?)broken["statusCode"], (bool?)broken["fetched"]));
        var unfetched = (int)(await PostOkAsync($"api/crawls/{grid.Id}/urls", JsonNode.Parse(
            """{"filters":[{"column":"fetched","op":"is","value":false},{"column":"linkingPages","op":"gte","value":1}],"limit":1}""")))["rows"]![0]!["id"]!;
        linking = (int)(await PostOkAsync($"api/crawls/{grid.Id}/urls/inlinks", new JsonObject { ["urlId"] = unfetched, ["limit"] = 1 }))["rows"]![0]!["id"]!;
        from = await PostOkAsync($"api/crawls/{grid.Id}/urls/outlinks", new JsonObject { ["urlId"] = linking, ["limit"] = 1_000 });
        var never = from["rows"]!.AsArray().First(row => (int?)row!["id"] == unfetched)!.AsObject();
        Assert.True(never.ContainsKey("outcome") && never["outcome"] is null && never["statusCode"] is null && (bool?)never["fetched"] == false, never.ToJsonString());

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

        // What each check's flagged URLs are: pages, or any URL, as a missing image is flagged for answering 404.
        Assert.All(checks, check => Assert.Equal(IssueCatalog.Find((string)check!["id"]!)!.Subject == IssueSubject.Page ? "page" : "url", (string?)check["subject"]));
        Assert.Equal("url", (string?)checks.Single(check => (string?)check!["id"] == "internal-client-error")!["subject"]);
        Assert.Equal("page", (string?)checks.Single(check => (string?)check!["id"] == "title-too-long")!["subject"]);

        var counts = IssueReader.ReadCounts(grid.Path);
        var related = counts.Where(count => count.Value > 0).Select(count => IssueCatalog.Find(count.Key)!)
            .First(check => check.Grouping == IssueGrouping.RelatedUrl).Id;
        var groups = await PostOkAsync($"api/crawls/{grid.Id}/issues/{related}/groups", JsonNode.Parse("""{"limit":1000}"""));
        Assert.Equal(["checkId", "grouping", "total", "groupCount", "offset", "groups", "sort", "likelyTemplate"], groups.AsObject().Select(property => property.Key));
        Assert.Equal("relatedUrl", (string?)groups["grouping"]);
        Assert.Equal(counts[related], (long?)groups["total"]);
        foreach (var group in groups["groups"]!.AsArray())
        {
            Assert.Equal(["detail", "relatedUrlId", "relatedUrl", "pages", "share", "likelyTemplate", "related", "sharedText", "urls"],
                group!.AsObject().Select(property => property.Key));
            Assert.Equal(group["relatedUrlId"] is null, group["related"] is null);
            Assert.Equal((long?)group["pages"], (long?)group["urls"]);
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

        // A check that can flag any URL counts its groups in URLs: a missing image's group is no page. The count has that
        // name, and its old one beside it.
        var anyUrl = await PostOkAsync($"api/crawls/{grid.Id}/issues/internal-client-error/groups");
        Assert.NotEmpty(anyUrl["groups"]!.AsArray());
        Assert.All(anyUrl["groups"]!.AsArray(), group => Assert.True((long?)group!["urls"] > 0 && (long?)group["urls"] == (long?)group["pages"]));
        Assert.Equal((long?)anyUrl["total"], anyUrl["groups"]!.AsArray().Sum(group => (long)group!["urls"]!));

        foreach (var path in new[] { "groups", "rows" })
        {
            TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/issues/no-such-check/{path}"), HttpStatusCode.NotFound, "check-not-found");
            TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.FreshId}/issues/{IssueCatalog.ORPHAN_PAGE.Id}/{path}"), HttpStatusCode.NotFound, "not-analyzed");
        }

        TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/issues/{IssueCatalog.ORPHAN_PAGE.Id}/groups", JsonNode.Parse("""{"limit":0}""")),
            HttpStatusCode.UnprocessableEntity, "invalid-query");
    }

    /// <summary>
    /// Where a page of flagged URLs is found, in one call: each URL's links and pages, its first sources as inlinks name
    /// them, its sitemaps; and what a page of a check's flagged pages each names, with the page's link to it. A check says
    /// which of the two it wants.
    /// </summary>
    [Fact]
    public async Task FlaggedUrlsSayWhereTheyAreFoundAndPagesWhatTheyName()
    {
        var checks = (await Sidecar.GetJsonAsync("api/checks"))["checks"]!.AsArray();
        string? References(string id) => (string?)checks.Single(check => (string?)check!["id"] == id)!["references"];
        Assert.Equal("sources", References("internal-client-error"));
        Assert.Equal("targets", References("broken-internal-links"));
        Assert.Equal("none", References("title-too-long"));

        // The 404 an hreflang names, found on the page declaring it.
        var gone = IdOf("/plants/gone");
        var found = await PostOkAsync($"api/crawls/{grid.Id}/urls/found-on", new JsonObject { ["urlIds"] = new JsonArray(gone, 999_999_999), ["limit"] = 2 });
        var url = found["urls"]!.AsArray().Single()!;
        Assert.Equal(["urlId", "links", "pages", "pagesExact", "sources", "sitemaps", "start"], url.AsObject().Select(property => property.Key));
        Assert.Equal(gone, (int?)url["urlId"]);
        Assert.True((bool)url["pagesExact"]!);
        Assert.False((bool)url["start"]!);
        var inlinks = await PostOkAsync($"api/crawls/{grid.Id}/urls/inlinks", new JsonObject { ["urlId"] = gone, ["limit"] = 2 });
        Assert.Equal(inlinks["rows"]!.ToJsonString(), url["sources"]!.ToJsonString());
        Assert.Equal((long?)inlinks["total"], (long?)url["links"]);
        Assert.Contains(url["sources"]!.AsArray(), source => (string?)source!["kind"] == "hreflang" && (string?)source["url"] == grid.Url("/plants/hreflang-broken"));
        Assert.Equal("""{"total":0,"sitemaps":[]}""", url["sitemaps"]!.ToJsonString());

        var pages = await PostOkAsync($"api/crawls/{grid.Id}/issues/broken-internal-links/rows", JsonNode.Parse("""{"columns":["id"],"limit":3}"""));
        var ids = pages["rows"]!.AsArray().Select(row => (int)row!["id"]!).ToList();
        var named = await PostOkAsync($"api/crawls/{grid.Id}/issues/broken-internal-links/targets", new JsonObject { ["urlIds"] = new JsonArray([.. ids.Select(id => JsonValue.Create(id))]) });
        Assert.Equal("broken-internal-links", (string?)named["checkId"]);
        Assert.Equal(ids, named["urls"]!.AsArray().Select(page => (int)page!["urlId"]!));
        var target = named["urls"]![0]!["targets"]![0]!;
        Assert.Equal(
            ["id", "url", "statusCode", "fetched", "outcome", "contentType", "sizeBytes", "pixelWidth", "pixelHeight", "responseMs", "redirectKind", "final", "indexability", "canonical",
             "kind", "text", "position", "fragment", "flags", "links", "img"],
            target.AsObject().Select(property => property.Key));
        Assert.True((int?)target["statusCode"] >= 400);
        Assert.Equal("hyperlink", (string?)target["kind"]);
        Assert.Equal(JsonValueKind.Array, target["flags"]!.GetValueKind());
        Assert.True((int)target["links"]! >= 1);

        // An oversized image says what it is, the largest first, and how the page shows it (evidence).
        var large = await PostOkAsync($"api/crawls/{grid.Id}/issues/image-too-large/targets", new JsonObject { ["urlIds"] = new JsonArray(IdOf("/plants/image-large")) });
        var image = large["urls"]![0]!["targets"]![0]!;
        Assert.Equal(("image/jpeg", 300 * 1024, 4000, 3000), ((string?)image["contentType"], (int)image["sizeBytes"]!, (int)image["pixelWidth"]!, (int)image["pixelHeight"]!));
        Assert.Equal("""{"width":"400","height":"300","alt":"A very large photo","hasAlt":true,"altLength":18,"srcset":false,"loading":null}""", image["img"]!.ToJsonString());
        Assert.Equal("none", (string?)image["indexability"]);

        // What each check shows, and a group's URL and order.
        var tooLong = checks.Single(check => (string?)check!["id"] == "title-too-long")!;
        Assert.Equal("""["title","titleInResults"]""", tooLong["evidence"]!.ToJsonString());
        Assert.Equal("[]", tooLong["targetFacts"]!.ToJsonString());
        var groups = await PostOkAsync($"api/crawls/{grid.Id}/issues/image-too-large/groups");
        Assert.Equal(("size", false), ((string?)groups["sort"], (bool)groups["likelyTemplate"]!));
        Assert.Equal(4000, (int)groups["groups"]![0]!["related"]!["pixelWidth"]!);
        Assert.Equal("pages", (string?)(await PostOkAsync($"api/crawls/{grid.Id}/issues/image-too-large/groups", JsonNode.Parse("""{"sort":"pages"}""")))["sort"]);
        TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/issues/image-too-large/groups", JsonNode.Parse("""{"sort":"colour"}""")),
            HttpStatusCode.UnprocessableEntity, "invalid-query");
        TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/issues/internal-client-error/groups", JsonNode.Parse("""{"sort":"size"}""")),
            HttpStatusCode.UnprocessableEntity, "invalid-query");

        // A redirect's hops and robots.txt's rule are records in a row.
        var loops = await PostOkAsync($"api/crawls/{grid.Id}/issues/redirect-loop/rows", JsonNode.Parse("""{"columns":["url","redirectChain"]}"""));
        var chain = loops["rows"]![0]!["redirectChain"]!;
        Assert.Equal(["hops", "final", "loop", "tooLong"], chain.AsObject().Select(property => property.Key));
        Assert.True((bool)chain["loop"]!);
        Assert.Equal(["id", "url", "statusCode", "outcome", "via"], chain["hops"]![0]!.AsObject().Select(property => property.Key));
        var robots = await PostOkAsync($"api/crawls/{grid.Id}/issues/blocked-by-robots/rows", JsonNode.Parse("""{"columns":["robotsRule"]}"""));
        Assert.Equal(["origin", "reason", "line", "rule"], robots["rows"]![0]!["robotsRule"]!.AsObject().Select(property => property.Key));
        Assert.Equal("rule", (string?)robots["rows"]![0]!["robotsRule"]!["reason"]);

        // Bounded, and refused where the request can't be read.
        foreach (var body in new[] { "{}", """{"urlIds":[]}""", $$"""{"urlIds":[{{string.Join(",", Enumerable.Range(1, 101))}}]}""", """{"urlIds":[1],"limit":51}""" })
        {
            TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/urls/found-on", JsonNode.Parse(body)), HttpStatusCode.UnprocessableEntity, "invalid-query");
            TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/issues/broken-internal-links/targets", JsonNode.Parse(body)),
                HttpStatusCode.UnprocessableEntity, "invalid-query");
        }

        TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/issues/no-such-check/targets", JsonNode.Parse("""{"urlIds":[1]}""")),
            HttpStatusCode.NotFound, "check-not-found");
        TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.FreshId}/issues/broken-internal-links/targets", JsonNode.Parse("""{"urlIds":[1]}""")),
            HttpStatusCode.NotFound, "not-analyzed");
    }

    /// <summary>
    /// The checks the user hid are left out of the issue counts the URL grid reads, sorts and filters by, of the severity
    /// facet, and of a links view's target counts, as the engine leaves them out (round 5 review).
    /// </summary>
    [Fact]
    public async Task HiddenChecksAreLeftOutOfTheIssueCounts()
    {
        string[] hidden = ["broken-internal-links", "internal-client-error", "a-check-gone"];
        var query = JsonNode.Parse("""{"columns":["url","issues","errors"],"filters":[{"column":"errors","op":"gte","value":1}],"sort":[{"column":"errors","desc":true}],"limit":1000}""")!;
        var plain = await PostOkAsync($"api/crawls/{grid.Id}/urls", query.DeepClone());
        var hiding = query.DeepClone();
        hiding["hiddenChecks"] = new JsonArray([.. hidden.Select(id => JsonValue.Create(id))]);
        var page = await PostOkAsync($"api/crawls/{grid.Id}/urls", hiding);

        var expected = UrlGridReader.Query(grid.Path, new UrlQuery
        {
            Columns = ["url", "issues", "errors"], Filters = [new UrlFilter("errors", FilterOperator.Gte) { Value = FilterValue.Of(1) }],
            Sort = [new UrlSort("errors", true)], Limit = 1_000, HiddenChecks = hidden
        });
        Assert.Equal(expected.Matching, (long)page["matching"]!);
        Assert.True((long)page["matching"]! < (long)plain["matching"]!);
        Assert.Equal(expected.Rows.Select(row => (string)row.Values[1]!), page["rows"]!.AsArray().Select(row => (string)row!["url"]!));
        Assert.Equal(expected.Rows.Select(row => (long)row.Values[3]!), page["rows"]!.AsArray().Select(row => (long)row!["errors"]!));

        // The severity facet counts as the columns do; its error bucket opens the grid's count.
        var facets = await PostOkAsync($"api/crawls/{grid.Id}/urls/facets", new JsonObject { ["facets"] = new JsonArray("severity"), ["hiddenChecks"] = hiding["hiddenChecks"]!.DeepClone() });
        var errors = facets["facets"]!["severity"]!.AsArray().Single(bucket => (string?)bucket!["value"] == "error")!;
        Assert.Equal(expected.Matching, (long)errors["count"]!);

        // A links view's targets, filtered by their issue count, the same.
        var links = JsonNode.Parse("""{"columns":["to.url","to.errors"],"filters":[{"column":"to.errors","op":"gte","value":1}],"limit":1}""")!;
        links["hiddenChecks"] = hiding["hiddenChecks"]!.DeepClone();
        var expectedLinks = LinkGridReader.Query(grid.Path, new UrlQuery
        {
            Columns = ["to.url", "to.errors"], Filters = [new UrlFilter("to.errors", FilterOperator.Gte) { Value = FilterValue.Of(1) }], Limit = 1, HiddenChecks = hidden
        });
        Assert.Equal(expectedLinks.Matching, (long)(await PostOkAsync($"api/crawls/{grid.Id}/links", links))["matching"]!);
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
