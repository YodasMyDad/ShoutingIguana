using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Sidecar.Exports;
using ShoutingIguana.Sidecar.Tests.Support;

namespace ShoutingIguana.Sidecar.Tests;

/// <summary>
/// Export as the UI calls it: a call starts a job and is answered at once, events and <c>GET /api/exports/{id}</c> tell
/// how far it has got and how it ended, a cancelled or failed one leaves no file, and what can be told before it starts
/// is refused before it starts. The report's document is served as text.
/// </summary>
public sealed class ExportEndpointsTests(GridSidecar grid) : IClassFixture<GridSidecar>, IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("si-export-api-").FullName;

    private TestSidecar Sidecar => grid.Sidecar;

    private ExportManager Exports => Sidecar.Host.Services.GetRequiredService<ExportManager>();

    public void Dispose()
    {
        Exports.Starting = null;
        TestSidecar.DeleteRoot(_folder);
    }

    [Fact]
    public async Task AnExportIsAJobToldByEventsAndReadByItsId()
    {
        await using var events = await EventStreamReader.ConnectAsync(Sidecar.Client);
        Assert.Empty((await events.NextAsync("hello"))["exports"]!.AsArray());

        var path = Path.Combine(_folder, "urls.csv");
        var request = JsonNode.Parse("""{"kind":"urls","format":"csv","columns":["url","statusCode"],"filters":[{"column":"type","op":"in","values":["page"]}],"sort":[{"column":"url"}]}""")!;
        request["path"] = path;
        var started = await Sidecar.PostAsync($"api/crawls/{grid.Id}/export", request);
        Assert.Equal(HttpStatusCode.Created, started.Status);
        var export = started.Body!;
        var id = (string)export["exportId"]!;
        Assert.Matches("^[0-9a-f]{16}$", id);
        Assert.Equal(
            ["exportId", "crawlId", "kind", "format", "path", "status", "rowsWritten", "rowsExpected", "startedAt", "finishedAt", "result", "error"],
            export.AsObject().Select(property => property.Key));
        Assert.Equal((grid.Id, "urls", "csv", path, "running"), ((string?)export["crawlId"], (string?)export["kind"], (string?)export["format"], (string?)export["path"], (string?)export["status"]));
        Assert.Null(export["result"]);
        Assert.Null(export["error"]);

        // Its events: running from the first, done at the last, each the whole export as GET gives it.
        var first = await events.NextAsync("export", data => (string?)data["exportId"] == id);
        Assert.Equal("running", (string?)first["status"]);
        var done = (string?)first["status"] == "done" ? first : await events.NextAsync("export", data => (string?)data["exportId"] == id && (string?)data["status"] != "running");
        Assert.Equal("done", (string?)done["status"]);
        var read = await Sidecar.GetJsonAsync($"api/exports/{id}");
        Assert.Equal(done.ToJsonString(), read.ToJsonString());

        // What it wrote is what the grid shows: the same rows, in the same order.
        var query = request.DeepClone().AsObject();
        foreach (var field in new[] { "kind", "format", "path" })
        {
            query.Remove(field);
        }

        query["limit"] = 1_000;
        var page = (await Sidecar.PostAsync($"api/crawls/{grid.Id}/urls", query)).Body!;
        var rows = page["rows"]!.AsArray();
        Assert.Equal((long)page["matching"]!, rows.Count);
        var lines = (await File.ReadAllTextAsync(path, Encoding.UTF8)).TrimStart('\uFEFF').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("URL,Status", lines[0]);
        Assert.Equal(rows.Select(row => $"{(string?)row!["url"]},{(long?)row["statusCode"]}"), lines.Skip(1));

        var result = done["result"]!;
        Assert.Equal(["rows", "bytes", "sheets", "unavailableColumns", "truncatedCells", "crawlWasRunning"], result.AsObject().Select(property => property.Key));
        Assert.Equal(rows.Count, (long)result["rows"]!);
        Assert.Equal(new FileInfo(path).Length, (long)result["bytes"]!);
        Assert.Equal((0, 0L, false), ((int)result["sheets"]!, (long)result["truncatedCells"]!, (bool)result["crawlWasRunning"]!));
        Assert.Equal((rows.Count, rows.Count), ((long)done["rowsWritten"]!, (long)done["rowsExpected"]!));
        Assert.NotNull((string?)done["finishedAt"]);

        // An export that has ended isn't among those a stream that connects now is told of.
        await using var late = await EventStreamReader.ConnectAsync(Sidecar.Client);
        Assert.Empty((await late.NextAsync("hello"))["exports"]!.AsArray());
    }

    [Theory]
    [InlineData("""{"kind":"links","format":"xlsx","columns":["from.url","to.url","kind"],"filters":[{"column":"kind","op":"in","values":["hyperlink"]}]}""", "links.xlsx")]
    [InlineData("""{"kind":"links","format":"csv"}""", "links.csv")]
    [InlineData("""{"kind":"sitemapUrls","format":"csv","sitemapId":1}""", "sitemap.csv")]
    [InlineData("""{"kind":"issueRows","format":"xlsx","checkId":"title-too-long","columns":["url","detail","title"]}""", "rows.xlsx")]
    [InlineData("""{"kind":"check","format":"csv","checkId":"title-too-long"}""", "check.csv")]
    [InlineData("""{"kind":"check","format":"xlsx","checkId":"title-too-long"}""", "check.xlsx")]
    [InlineData("""{"kind":"workbook","format":"xlsx"}""", "everything.xlsx")]
    [InlineData("""{"kind":"report","format":"html"}""", "report.html")]
    [InlineData("""{"kind":"report","format":"html","cut":"summary","examples":2,"lookExamples":0}""", "summary.html")]
    public async Task EveryKindIsWrittenToItsFile(string body, string name)
    {
        var path = Path.Combine(_folder, name);
        var done = await ExportAsync(grid.Id, body, path);
        Assert.True((string?)done["status"] == "done", done.ToJsonString());
        var bytes = await File.ReadAllBytesAsync(path);
        Assert.Equal(bytes.Length, (long)done["result"]!["bytes"]!);
        Assert.Equal([name], Directory.GetFiles(_folder).Select(Path.GetFileName));

        var magic = Path.GetExtension(name) switch
        {
            ".xlsx" => "PK"u8.ToArray(),
            ".csv" => [0xEF, 0xBB, 0xBF],
            _ => "<!doctype html>"u8.ToArray()
        };
        Assert.Equal(magic, bytes[..magic.Length]);
        if (name == "check.csv")
        {
            Assert.Equal(IssueReader.ReadCounts(grid.Path)["title-too-long"], (long)done["result"]!["rows"]!);
        }

        if (name == "everything.xlsx")
        {
            var counts = IssueReader.ReadCounts(grid.Path);
            Assert.Equal(counts.Values.Sum(), (long)done["result"]!["rows"]!);
            Assert.Equal(counts.Count(count => count.Value > 0) + 1, (int)done["result"]!["sheets"]!);
        }
    }

    [Fact]
    public async Task AnOriginsBlockedUrlsAreExported()
    {
        var origins = (await Sidecar.PostAsync($"api/crawls/{grid.Id}/robots", new JsonObject { ["limit"] = 100 })).Body!["origins"]!.AsArray();
        // The origin that blocked the most, which the list puts first among the site's own: few or none, on a crawl this small.
        var origin = origins.MaxBy(candidate => (long)candidate!["blocked"]!)!;
        var path = Path.Combine(_folder, "blocked.csv");
        var done = await ExportAsync(grid.Id, new JsonObject { ["kind"] = "robotsBlocked", ["format"] = "csv", ["origin"] = (string)origin["origin"]! }.ToJsonString(), path);
        Assert.Equal((long)origin["blocked"]!, (long)done["result"]!["rows"]!);
        Assert.StartsWith("Blocked URL,Blocked by,Line,Rule,Error\r\n", (await File.ReadAllTextAsync(path)).TrimStart('\uFEFF'), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheReportIsServedAsItsDocument()
    {
        using var response = await Sidecar.Client.GetAsync($"api/crawls/{grid.Id}/report");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);

        // Like every answer, it's no page a browser could open at this address: nothing may frame or sniff it.
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("default-src 'none'; frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        var full = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("<!doctype html>", full, StringComparison.Ordinal);
        Assert.Contains("<h2>What to fix</h2>", full, StringComparison.Ordinal);
        Assert.Contains("http-equiv=\"Content-Security-Policy\" content=\"default-src &#39;none&#39;; style-src &#39;sha256-", full, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", full, StringComparison.OrdinalIgnoreCase);

        var summary = await Sidecar.Client.GetStringAsync($"api/crawls/{grid.Id}/report?cut=summary");
        Assert.DoesNotContain("<h2>What to fix</h2>", summary, StringComparison.Ordinal);
        Assert.Contains("<h2>How the score is worked out</h2>", summary, StringComparison.Ordinal);
        Assert.True(summary.Length < full.Length / 2);

        // The examples asked for: with none, no problem lists a page.
        var bare = await Sidecar.Client.GetStringAsync($"api/crawls/{grid.Id}/report?cut=full&examples=0&lookExamples=0");
        Assert.DoesNotContain("<table class=\"examples", bare, StringComparison.Ordinal);
        Assert.Contains("<table class=\"examples", full, StringComparison.Ordinal);

        // The file an export saves is this document.
        var path = Path.Combine(_folder, "report.html");
        await ExportAsync(grid.Id, """{"kind":"report","format":"html"}""", path);
        Assert.Equal(WithoutMadeOn(full), WithoutMadeOn(await File.ReadAllTextAsync(path)));

        // A crawl never analysed has no report; nor is one made with numbers that aren't.
        TestSidecar.AssertProblem(await Sidecar.SendAsync(HttpMethod.Get, $"api/crawls/{grid.FreshId}/report"), HttpStatusCode.NotFound, "not-analyzed");
        foreach (var (query, field) in new[] { ("cut=half", "cut"), ("examples=51", "examples"), ("examples=x", "examples"), ("lookExamples=-1", "lookExamples"), ("pages=2", "pages") })
        {
            var problem = TestSidecar.AssertProblem(await Sidecar.SendAsync(HttpMethod.Get, $"api/crawls/{grid.Id}/report?{query}"), HttpStatusCode.UnprocessableEntity, "invalid-export");
            Assert.Equal([field], problem["errors"]!.AsObject().Select(error => error.Key));
        }

        TestSidecar.AssertProblem(await Sidecar.SendAsync(HttpMethod.Get, "api/crawls/0000000000000000/report"), HttpStatusCode.NotFound, "crawl-not-found");

        static string WithoutMadeOn(string html) => html[..html.IndexOf("<footer", StringComparison.Ordinal)];
    }

    [Theory]
    [InlineData("""{"format":"csv"}""", "kind")]
    [InlineData("""{"kind":"everything","format":"csv"}""", "kind")]
    [InlineData("""{"kind":"urls"}""", "format")]
    [InlineData("""{"kind":"urls","format":"pdf"}""", "format")]
    [InlineData("""{"kind":"urls","format":"html"}""", "format")]
    [InlineData("""{"kind":"workbook","format":"csv"}""", "format")]
    [InlineData("""{"kind":"sitemapUrls","format":"csv"}""", "sitemapId")]
    [InlineData("""{"kind":"robotsBlocked","format":"csv"}""", "origin")]
    [InlineData("""{"kind":"robotsBlocked","format":"csv","origin":"https://a.example","reason":"because"}""", "reason")]
    [InlineData("""{"kind":"issueRows","format":"csv"}""", "checkId")]
    [InlineData("""{"kind":"urls","format":"csv","group":{"detail":null,"relatedUrlId":1}}""", "group")]
    [InlineData("""{"kind":"urls","format":"csv","checkId":"title-missing"}""", "checkId")]
    [InlineData("""{"kind":"links","format":"csv","sitemapId":1}""", "sitemapId")]
    [InlineData("""{"kind":"workbook","format":"xlsx","columns":["url"]}""", "columns")]
    [InlineData("""{"kind":"check","format":"xlsx","checkId":"title-missing","sort":[{"column":"url"}]}""", "sort")]
    [InlineData("""{"kind":"urls","format":"csv","cut":"summary"}""", "cut")]
    [InlineData("""{"kind":"report","format":"html","cut":"half"}""", "cut")]
    [InlineData("""{"kind":"report","format":"html","examples":51}""", "examples")]
    [InlineData("""{"kind":"report","format":"html","lookExamples":-1}""", "lookExamples")]
    public async Task ARequestThatCannotBeAnExportIsRefusedByField(string body, string field)
    {
        var request = JsonNode.Parse(body)!;
        var extension = (string?)request["format"] switch { "xlsx" => "xlsx", "html" => "html", _ => "csv" };
        request["path"] = Path.Combine(_folder, $"out.{extension}");
        var problem = TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/export", request), HttpStatusCode.UnprocessableEntity, "invalid-export");
        Assert.Equal([field], problem["errors"]!.AsObject().Select(error => error.Key));
        Assert.Equal((string?)problem["errors"]![field]![0], (string?)problem["detail"]);
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task APathThatCannotBeWrittenIsRefusedBeforeTheExportStarts()
    {
        async Task<JsonNode> RefusedAsync(string? path, HttpStatusCode status, string type, string format = "csv")
        {
            var request = new JsonObject { ["kind"] = "urls", ["format"] = format };
            if (path is not null)
            {
                request["path"] = path;
            }

            return TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/export", request), status, type);
        }

        foreach (var path in new[] { null, "", "urls.csv", Path.Combine(_folder, "urls.txt"), Path.Combine(_folder, "urls.xlsx"), grid.Path })
        {
            var problem = await RefusedAsync(path, HttpStatusCode.UnprocessableEntity, "invalid-export");
            Assert.Equal(["path"], problem["errors"]!.AsObject().Select(error => error.Key));
        }

        // The folder isn't there.
        var missing = await RefusedAsync(Path.Combine(_folder, "nowhere", "urls.csv"), HttpStatusCode.BadRequest, "export-not-writable");
        Assert.Contains("folder", (string?)missing["title"], StringComparison.Ordinal);

        if (OperatingSystem.IsWindows())
        {
            // Another computer: refused before anything touches it.
            foreach (var path in new[] { @"\\server\share\urls.csv", @"\\?\UNC\server\share\urls.csv", @"\\.\pipe\urls.csv" })
            {
                var network = await RefusedAsync(path, HttpStatusCode.UnprocessableEntity, "invalid-export");
                Assert.Contains("another computer", (string?)network["errors"]!["path"]![0], StringComparison.Ordinal);
            }

            // A file of that name is open in another program, as Excel holds a workbook it's showing.
            var open = Path.Combine(_folder, "open.xlsx");
            await File.WriteAllTextAsync(open, "the workbook as it was");
            await using (new FileStream(open, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var held = await RefusedAsync(open, HttpStatusCode.Conflict, "export-file-in-use", "xlsx");
                Assert.StartsWith("open.xlsx is open in another program, such as Excel.", (string?)held["title"], StringComparison.Ordinal);
            }

            Assert.Equal("the workbook as it was", await File.ReadAllTextAsync(open));
        }

        // A query the grid would refuse is refused as the grid refuses it.
        var query = JsonNode.Parse("""{"kind":"urls","format":"csv","columns":["url","nope"],"filters":[{"column":"statusCode","op":"contains","value":"4"}]}""")!;
        query["path"] = Path.Combine(_folder, "urls.csv");
        var invalid = TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/export", query), HttpStatusCode.UnprocessableEntity, "invalid-query");
        Assert.Equal(["columns[1]", "filters[0].op"], invalid["errors"]!.AsObject().Select(error => error.Key).Order());

        var unknown = new JsonObject { ["kind"] = "check", ["format"] = "csv", ["checkId"] = "no-such-check", ["path"] = Path.Combine(_folder, "check.csv") };
        TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/export", unknown), HttpStatusCode.NotFound, "check-not-found");
        TestSidecar.AssertProblem(await Sidecar.PostAsync("api/crawls/0000000000000000/export", unknown), HttpStatusCode.NotFound, "crawl-not-found");
        TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/export", JsonNode.Parse("""{"kind":"urls","format":"csv","limit":5}""")), HttpStatusCode.BadRequest,
            "bad-request");
        Assert.DoesNotContain(Directory.GetFiles(_folder), file => !file.EndsWith("open.xlsx", StringComparison.Ordinal));
    }

    /// <summary>What only the crawl can say ends the export, with the problem a grid's call would have been answered with.</summary>
    [Theory]
    [InlineData("fresh", """{"kind":"issueRows","format":"csv","checkId":"title-too-long"}""", "not-analyzed", 404)]
    [InlineData("fresh", """{"kind":"workbook","format":"xlsx"}""", "not-analyzed", 404)]
    [InlineData("fresh", """{"kind":"report","format":"html"}""", "not-analyzed", 404)]
    [InlineData("fresh", """{"kind":"urls","format":"csv","sort":[{"column":"inlinks","desc":true}]}""", "needs-analysis", 409)]
    [InlineData("limited", """{"kind":"check","format":"csv","checkId":"orphan-page"}""", "check-skipped", 409)]
    [InlineData("site", """{"kind":"sitemapUrls","format":"csv","sitemapId":9999}""", "sitemap-not-found", 404)]
    [InlineData("site", """{"kind":"robotsBlocked","format":"csv","origin":"https://nowhere.example"}""", "origin-not-found", 404)]
    public async Task AnExportThatFailsSaysWhyAndLeavesNoFile(string crawl, string body, string type, int status)
    {
        var id = crawl switch { "fresh" => grid.FreshId, "limited" => grid.LimitedId, _ => grid.Id };
        var extension = (string?)JsonNode.Parse(body)!["format"];
        var failed = await ExportAsync(id, body, Path.Combine(_folder, $"out.{extension}"));
        Assert.Equal("failed", (string?)failed["status"]);
        Assert.Null(failed["result"]);
        Assert.Equal((type, status), ((string?)failed["error"]!["type"], (int)failed["error"]!["status"]!));
        Assert.False(string.IsNullOrWhiteSpace((string?)failed["error"]!["title"]));
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task ACancelledExportLeavesNothingAndARunningOneIsToldToAStreamThatConnects()
    {
        // The export waits here as it starts, so the test meets it running.
        using var release = new ManualResetEventSlim();
        Exports.Starting = _ => release.Wait(EventStreamReader.TIMEOUT);
        var path = Path.Combine(_folder, "links.csv");
        var started = await Sidecar.PostAsync($"api/crawls/{grid.Id}/export", new JsonObject { ["kind"] = "links", ["format"] = "csv", ["path"] = path });
        var id = (string)started.Body!["exportId"]!;

        await using var events = await EventStreamReader.ConnectAsync(Sidecar.Client);
        Assert.Equal([id], (await events.NextAsync("hello"))["exports"]!.AsArray().Select(export => (string?)export));
        Assert.Equal("running", (string?)(await events.NextAsync("export", data => (string?)data["exportId"] == id))["status"]);
        Assert.Equal("running", (string?)(await Sidecar.GetJsonAsync($"api/exports/{id}"))["status"]);

        // Another export can't write the same file meanwhile, however the path is spelt.
        var same = new JsonObject { ["kind"] = "urls", ["format"] = "csv", ["path"] = OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path };
        TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.LimitedId}/export", same), HttpStatusCode.Conflict, "export-busy");

        Assert.Equal(HttpStatusCode.Accepted, (await Sidecar.PostAsync($"api/exports/{id}/cancel")).Status);
        release.Set();
        var ended = await events.NextAsync("export", data => (string?)data["exportId"] == id && (string?)data["status"] != "running");
        Assert.Equal("cancelled", (string?)ended["status"]);
        Assert.Null(ended["result"]);
        Assert.Null(ended["error"]);
        Assert.Empty(Directory.GetFiles(_folder));

        // Cancelling it again changes nothing; an export nobody started isn't there to cancel or to read.
        Assert.Equal(HttpStatusCode.Accepted, (await Sidecar.PostAsync($"api/exports/{id}/cancel")).Status);
        Assert.Equal("cancelled", (string?)(await Sidecar.GetJsonAsync($"api/exports/{id}"))["status"]);
        TestSidecar.AssertProblem(await Sidecar.PostAsync("api/exports/0123456789abcdef/cancel"), HttpStatusCode.NotFound, "export-not-found");
        TestSidecar.AssertProblem(await Sidecar.SendAsync(HttpMethod.Get, "api/exports/0123456789abcdef"), HttpStatusCode.NotFound, "export-not-found");
    }

    /// <summary>
    /// A crawl running here is exported as it stood when the export began, while it goes on; and shutting down cancels an
    /// export, which leaves no file, and lets no other start.
    /// </summary>
    [Fact]
    public async Task ACrawlRunningHereIsExportedAndShutdownCancelsExports()
    {
        await using var sidecar = await TestSidecar.StartAsync();
        await using var events = await EventStreamReader.ConnectAsync(sidecar.Client);
        var options = new JsonObject { ["startUrl"] = grid.Site.BaseUri.AbsoluteUri, ["maxRequestsPerSecond"] = 20, ["concurrency"] = 2 };
        var crawl = (await sidecar.PostAsync("api/crawls", new JsonObject { ["options"] = options })).Body!;
        var id = (string)crawl["id"]!;
        await events.NextAsync("progress", data => (string?)data["crawlId"] == id && (long?)data["htmlPages"] >= 5);

        var path = Path.Combine(_folder, "running.csv");
        var started = await sidecar.PostAsync($"api/crawls/{id}/export", new JsonObject { ["kind"] = "links", ["format"] = "csv", ["path"] = path, ["columns"] = new JsonArray("id", "to.url") });
        Assert.Equal(HttpStatusCode.Created, started.Status);
        var done = await events.NextAsync("export", data => (string?)data["status"] != "running");
        Assert.True((string?)done["status"] == "done", done.ToJsonString());
        Assert.True((bool)done["result"]!["crawlWasRunning"]!);
        var rows = (long)done["result"]!["rows"]!;
        Assert.True(rows > 0);
        Assert.Equal(rows + 1, (await File.ReadAllLinesAsync(path)).Length);
        Assert.Contains((string?)(await sidecar.GetJsonAsync($"api/crawls/{id}"))["status"], new[] { "running", "analyzing", "completed" });

        // An export waiting to start when the sidecar shuts down is cancelled, and nothing new starts.
        using var release = new ManualResetEventSlim();
        var exports = sidecar.Host.Services.GetRequiredService<ExportManager>();
        exports.Starting = _ => release.Wait(EventStreamReader.TIMEOUT);
        var waiting = Path.Combine(_folder, "waiting.xlsx");
        var second = await sidecar.PostAsync($"api/crawls/{id}/export", new JsonObject { ["kind"] = "links", ["format"] = "xlsx", ["path"] = waiting });
        var stopping = exports.StopAllAsync(CancellationToken.None);
        TestSidecar.AssertProblem(await sidecar.PostAsync($"api/crawls/{id}/export", new JsonObject { ["kind"] = "urls", ["format"] = "csv", ["path"] = Path.Combine(_folder, "late.csv") }),
            HttpStatusCode.Conflict, "export-busy");
        release.Set();
        await stopping.WaitAsync(EventStreamReader.TIMEOUT);
        Assert.Equal("cancelled", (string?)(await sidecar.GetJsonAsync($"api/exports/{(string)second.Body!["exportId"]!}"))["status"]);
        Assert.Equal(["running.csv"], Directory.GetFiles(_folder).Select(Path.GetFileName));
        await sidecar.PostAsync($"api/crawls/{id}/stop");
    }

    [Fact]
    public async Task OnlySoManyExportsRunAtOnce()
    {
        using var release = new ManualResetEventSlim();
        Exports.Starting = _ => release.Wait(EventStreamReader.TIMEOUT);
        List<string> ids = [];
        try
        {
            for (var index = 0; index < ExportManager.MAX_RUNNING; index++)
            {
                var started = await Sidecar.PostAsync($"api/crawls/{grid.Id}/export",
                    new JsonObject { ["kind"] = "urls", ["format"] = "csv", ["path"] = Path.Combine(_folder, $"urls-{index}.csv") });
                Assert.Equal(HttpStatusCode.Created, started.Status);
                ids.Add((string)started.Body!["exportId"]!);
            }

            var onceMore = new JsonObject { ["kind"] = "urls", ["format"] = "csv", ["path"] = Path.Combine(_folder, "one-more.csv") };
            TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/export", onceMore), HttpStatusCode.Conflict, "export-busy");
        }
        finally
        {
            release.Set();
        }

        foreach (var id in ids)
        {
            Assert.Equal("done", (string?)(await EndedAsync(id))["status"]);
        }
    }

    [Fact]
    public async Task TheColumnsHaveTheirWords()
    {
        var urls = (await Sidecar.GetJsonAsync("api/url-columns")).AsArray().ToDictionary(column => (string)column!["id"]!, column => column!);
        Assert.Equal(("Status", null), ((string?)urls["statusCode"]["title"], (string?)urls["statusCode"]["unit"]));
        Assert.Equal(("Response time", "ms"), ((string?)urls["responseMs"]["title"], (string?)urls["responseMs"]["unit"]));
        Assert.Equal(("Size", "bytes"), ((string?)urls["sizeBytes"]["title"], (string?)urls["sizeBytes"]["unit"]));
        Assert.Equal("""{"none":"Indexable","noindex":"Noindex","canonicalised":"Canonicalised","redirected":"Redirected","clientError":"Client error","serverError":"Server error","blockedByRobots":"Blocked by robots.txt","blocked":"Blocked","unreachable":"Unreachable"}""",
            urls["indexability"]["valueTitles"]!.ToJsonString(new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        Assert.Equal("{}", urls["title"]["valueTitles"]!.ToJsonString());
        Assert.All(urls.Values, column => Assert.False(string.IsNullOrWhiteSpace((string?)column["title"])));
        Assert.All(urls.Values, column => Assert.Equal(column["values"]!.AsArray().Select(value => (string?)value), column["valueTitles"]!.AsObject().Select(title => title.Key)));

        var links = (await Sidecar.GetJsonAsync("api/link-columns")).AsArray().ToDictionary(column => (string)column!["id"]!, column => column!);
        Assert.Equal("Anchor or alt text", (string?)links["text"]["title"]);
        Assert.Equal("Source URL", (string?)links["from.url"]["title"]);
        Assert.Equal("Target status", (string?)links["to.statusCode"]["title"]);
        Assert.Equal(("Target response time", "ms"), ((string?)links["to.responseMs"]["title"], (string?)links["to.responseMs"]["unit"]));
        Assert.Equal("Hyperlink", (string?)links["kind"]["valueTitles"]!["hyperlink"]);
        Assert.Equal(links.Count, links.Values.Select(column => (string?)column["title"]).Distinct().Count());
        Assert.Equal(urls.Count, urls.Values.Select(column => (string?)column["title"]).Distinct().Count());
    }

    /// <summary>Starts an export and waits for it to end: the export as its last event and <c>GET</c> have it.</summary>
    private async Task<JsonNode> ExportAsync(string crawlId, string body, string path)
    {
        var request = JsonNode.Parse(body)!;
        request["path"] = path;
        var started = await Sidecar.PostAsync($"api/crawls/{crawlId}/export", request);
        Assert.True(started.Status == HttpStatusCode.Created, $"{(int)started.Status} {started.Body?.ToJsonString()}");
        return await EndedAsync((string)started.Body!["exportId"]!);
    }

    private async Task<JsonNode> EndedAsync(string exportId)
    {
        var deadline = DateTime.UtcNow + EventStreamReader.TIMEOUT;
        while (true)
        {
            var export = await Sidecar.GetJsonAsync($"api/exports/{exportId}");
            if ((string?)export["status"] != "running")
            {
                return export;
            }

            Assert.True(DateTime.UtcNow < deadline, "The export never ended.");
            await Task.Delay(20);
        }
    }
}
