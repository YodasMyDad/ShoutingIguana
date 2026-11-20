using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ShoutingIguana.Engine.Export;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Sidecar.Exports;
using ShoutingIguana.Sidecar.Library;
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
        Exports.Writing = null;
        Exports.MaxXlsxRows = null;
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
    [InlineData("""{"kind":"sitemaps","format":"csv"}""", "sitemaps.csv")]
    [InlineData("""{"kind":"sitemaps","format":"xlsx"}""", "sitemaps.xlsx")]
    [InlineData("""{"kind":"robotsOrigins","format":"csv"}""", "origins.csv")]
    [InlineData("""{"kind":"robotsOrigins","format":"xlsx","search":"127","blocked":false}""", "origins.xlsx")]
    [InlineData("""{"kind":"urls","format":"csv","linkEnds":{"end":"to","filters":[{"column":"kind","op":"in","values":["hyperlink"]}]}}""", "targets.csv")]
    [InlineData("""{"kind":"linkGroups","format":"csv","filters":[{"column":"kind","op":"in","values":["hyperlink"]}],"sort":[{"column":"pages","desc":true}]}""", "groups.csv")]
    [InlineData("""{"kind":"linkGroups","format":"xlsx","search":"about","hiddenChecks":["title-too-long"]}""", "groups.xlsx")]
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
        Assert.StartsWith("Blocked URL,Blocked by,Line,Rule,Error,Linked from (pages),In a sitemap,Sitemap,In a menu,Conflict\r\n",
            (await File.ReadAllTextAsync(path)).TrimStart('\uFEFF'), StringComparison.Ordinal);

        // The conflicts alone: the one URL the site's sitemap lists though robots.txt refuses it.
        var conflicts = await ExportAsync(grid.Id,
            new JsonObject { ["kind"] = "robotsBlocked", ["format"] = "csv", ["origin"] = grid.Url(string.Empty), ["conflict"] = true }.ToJsonString(), path = Path.Combine(_folder, "conflicts.csv"));
        Assert.Equal(1L, (long)conflicts["result"]!["rows"]!);
        Assert.EndsWith(",0,Yes," + grid.Url("/sitemaps/extra.xml") + ",No,In your sitemap but blocked", (await File.ReadAllLinesAsync(path))[1], StringComparison.Ordinal);
    }

    /// <summary>The lists that aren't grids, and the URL grid narrowed to a links view's ends: each file is what its screen's call lists, row for row.</summary>
    [Fact]
    public async Task TheListsAndALinksViewsEndsAreExportedAsTheirCallsListThem()
    {
        static List<string> Column(string path) =>
            [.. File.ReadAllText(path).TrimStart('\uFEFF').Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(line => line.Split(',')[0])];

        // The origins, searched and filtered as the list is.
        foreach (var filter in new[] { new JsonObject(), new JsonObject { ["blocked"] = true }, new JsonObject { ["blocked"] = false, ["search"] = "www" } })
        {
            var asked = filter.DeepClone().AsObject();
            asked["limit"] = 1_000;
            var listed = (await Sidecar.PostAsync($"api/crawls/{grid.Id}/robots", asked)).Body!;
            var body = filter.DeepClone().AsObject();
            body["kind"] = "robotsOrigins";
            body["format"] = "csv";
            var path = Path.Combine(_folder, "origins.csv");
            var done = await ExportAsync(grid.Id, body.ToJsonString(), path);
            Assert.True((string?)done["status"] == "done", done.ToJsonString());
            Assert.Equal(listed["origins"]!.AsArray().Select(origin => (string)origin!["origin"]!), Column(path));
            Assert.Equal((long)listed["matching"]!, (long)done["result"]!["rows"]!);
        }

        // The sitemaps, in the list's order.
        var sitemaps = await Sidecar.GetJsonAsync($"api/crawls/{grid.Id}/sitemaps");
        var sitemapsPath = Path.Combine(_folder, "sitemaps.csv");
        await ExportAsync(grid.Id, """{"kind":"sitemaps","format":"csv"}""", sitemapsPath);
        Assert.Equal(sitemaps["sitemaps"]!.AsArray().Select(sitemap => (string)sitemap!["url"]!), Column(sitemapsPath));

        // A links view's targets, beside a filter of the URL grid's own.
        var query = JsonNode.Parse("""{"columns":["url"],"filters":[{"column":"type","op":"in","values":["page"]}],"linkEnds":{"end":"to","filters":[{"column":"kind","op":"in","values":["hyperlink"]}]}}""")!.AsObject();
        var read = query.DeepClone().AsObject();
        read["limit"] = 1_000;
        var page = (await Sidecar.PostAsync($"api/crawls/{grid.Id}/urls", read)).Body!;
        var urlsPath = Path.Combine(_folder, "targets.csv");
        var request = query.DeepClone().AsObject();
        request["kind"] = "urls";
        request["format"] = "csv";
        var urls = await ExportAsync(grid.Id, request.ToJsonString(), urlsPath);
        Assert.True((long)page["matching"]! > 0);
        Assert.Equal((long)page["matching"]!, (long)urls["result"]!["rows"]!);
        Assert.Equal(page["rows"]!.AsArray().Select(row => (string)row!["url"]!), Column(urlsPath));

        // linkEnds that the grid would refuse is refused as the grid's call refuses it.
        var bad = JsonNode.Parse("""{"kind":"urls","format":"csv","linkEnds":{"end":"sideways"}}""")!;
        bad["path"] = Path.Combine(_folder, "bad.csv");
        var problem = TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/export", bad), HttpStatusCode.UnprocessableEntity, "invalid-query");
        Assert.Equal(["linkEnds.end"], problem["errors"]!.AsObject().Select(error => error.Key));

        // The checks the user hid are left out of the issue counts in the file, as the grid leaves them out (round 5 review).
        var hiding = JsonNode.Parse("""{"columns":["errors","url"],"sort":[{"column":"url"}],"hiddenChecks":["broken-internal-links","internal-client-error","a-check-gone"],"limit":1000}""")!;
        var shown = (await Sidecar.PostAsync($"api/crawls/{grid.Id}/urls", hiding.DeepClone())).Body!;
        var plainShown = (await Sidecar.PostAsync($"api/crawls/{grid.Id}/urls", new JsonObject { ["columns"] = new JsonArray("errors", "url"), ["sort"] = hiding["sort"]!.DeepClone(), ["limit"] = 1000 })).Body!;
        var hiddenPath = Path.Combine(_folder, "hidden.csv");
        var hidingExport = hiding.DeepClone().AsObject();
        hidingExport.Remove("limit");
        hidingExport["kind"] = "urls";
        hidingExport["format"] = "csv";
        var exported = await ExportAsync(grid.Id, hidingExport.ToJsonString(), hiddenPath);
        Assert.Equal((long)shown["matching"]!, (long)exported["result"]!["rows"]!);
        List<string> errors = [.. shown["rows"]!.AsArray().Select(row => ((long)row!["errors"]!).ToString(System.Globalization.CultureInfo.InvariantCulture))];
        Assert.Equal(errors, Column(hiddenPath).Take(errors.Count));
        Assert.NotEqual(plainShown["rows"]!.AsArray().Select(row => (long)row!["errors"]!), shown["rows"]!.AsArray().Select(row => (long)row!["errors"]!));

        // Only a stale id hidden (a check another version had): taken, and every count is the grid's own.
        var stalePath = Path.Combine(_folder, "stale.csv");
        var stale = await ExportAsync(grid.Id, """{"kind":"urls","format":"csv","columns":["errors","url"],"sort":[{"column":"url"}],"hiddenChecks":["no-such-check"]}""", stalePath);
        Assert.Equal((long)plainShown["matching"]!, (long)stale["result"]!["rows"]!);
        List<string> plainErrors = [.. plainShown["rows"]!.AsArray().Select(row => ((long)row!["errors"]!).ToString(System.Globalization.CultureInfo.InvariantCulture))];
        Assert.Equal(plainErrors, Column(stalePath).Take(plainErrors.Count));

        // An id too long to be one is refused as the grid's call refuses it, past the report's own limits.
        var tooLong = JsonNode.Parse($$"""{"kind":"urls","format":"csv","hiddenChecks":["{{new string('x', 201)}}"]}""")!;
        tooLong["path"] = Path.Combine(_folder, "unknown.csv");
        var refused = TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/export", tooLong), HttpStatusCode.UnprocessableEntity, "invalid-query");
        Assert.Equal(["hiddenChecks"], refused["errors"]!.AsObject().Select(error => error.Key));
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
        Assert.Contains("<h2 id=\"what-to-fix\">What to fix</h2>", full, StringComparison.Ordinal);
        Assert.Contains("http-equiv=\"Content-Security-Policy\" content=\"default-src &#39;none&#39;; style-src &#39;sha256-", full, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", full, StringComparison.OrdinalIgnoreCase);

        var summary = await Sidecar.Client.GetStringAsync($"api/crawls/{grid.Id}/report?cut=summary");
        Assert.DoesNotContain("<h2 id=\"what-to-fix\">What to fix</h2>", summary, StringComparison.Ordinal);
        Assert.Contains("<h2 id=\"score\">How the score is worked out</h2>", summary, StringComparison.Ordinal);
        Assert.True(summary.Length < full.Length / 2);

        // Whose words its rows are in (the UX audit's S-11): a site owner's unless asked, as Simple shows it, with no check's
        // technical name beside its title; a working SEO's when asked, as Advanced does. The file an export saves is the same.
        Assert.DoesNotContain("class=\"problem__tech\"", full, StringComparison.Ordinal);
        Assert.Equal(WithoutMadeOn(full), WithoutMadeOn(await Sidecar.Client.GetStringAsync($"api/crawls/{grid.Id}/report?words=plain")));
        var technical = await Sidecar.Client.GetStringAsync($"api/crawls/{grid.Id}/report?words=technical");
        Assert.Contains("class=\"problem__tech\"", technical, StringComparison.Ordinal);
        var technicalPath = Path.Combine(_folder, "technical.html");
        await ExportAsync(grid.Id, """{"kind":"report","format":"html","words":"technical"}""", technicalPath);
        Assert.Equal(WithoutMadeOn(technical), WithoutMadeOn(await File.ReadAllTextAsync(technicalPath)));

        // The examples asked for: with none, no problem lists a page.
        var bare = await Sidecar.Client.GetStringAsync($"api/crawls/{grid.Id}/report?cut=full&examples=0&lookExamples=0");
        Assert.DoesNotContain("<table class=\"examples", bare, StringComparison.Ordinal);
        Assert.Contains("<table class=\"examples", full, StringComparison.Ordinal);

        // The checks the user hid are left out of the report shown, as of the one saved (discovery decision 2).
        var hidden = await Sidecar.Client.GetStringAsync($"api/crawls/{grid.Id}/report?hiddenChecks=title-missing,{Uri.EscapeDataString("a-check-gone")}");
        Assert.True(hidden.Length < full.Length);
        var hiddenPath = Path.Combine(_folder, "hidden.html");
        await ExportAsync(grid.Id, """{"kind":"report","format":"html","hiddenChecks":["title-missing","a-check-gone"]}""", hiddenPath);
        Assert.Equal(WithoutMadeOn(hidden), WithoutMadeOn(await File.ReadAllTextAsync(hiddenPath)));

        // The file an export saves is this document.
        var path = Path.Combine(_folder, "report.html");
        await ExportAsync(grid.Id, """{"kind":"report","format":"html"}""", path);
        Assert.Equal(WithoutMadeOn(full), WithoutMadeOn(await File.ReadAllTextAsync(path)));

        // A crawl never analysed has no report; nor is one made with numbers that aren't.
        TestSidecar.AssertProblem(await Sidecar.SendAsync(HttpMethod.Get, $"api/crawls/{grid.FreshId}/report"), HttpStatusCode.NotFound, "not-analyzed");
        foreach (var (query, field) in new[] { ("cut=half", "cut"), ("examples=51", "examples"), ("examples=x", "examples"), ("lookExamples=-1", "lookExamples"), ("words=simple", "words"), ("pages=2", "pages") })
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
    [InlineData("""{"kind":"report","format":"html","words":"simple"}""", "words")]
    [InlineData("""{"kind":"workbook","format":"xlsx","words":"plain"}""", "words")]
    [InlineData("""{"kind":"links","format":"csv","linkEnds":{"end":"to"}}""", "linkEnds")]
    [InlineData("""{"kind":"sitemapUrls","format":"csv","sitemapId":1,"linkEnds":{"end":"to"}}""", "linkEnds")]
    [InlineData("""{"kind":"urls","format":"csv","blocked":true}""", "blocked")]
    [InlineData("""{"kind":"robotsBlocked","format":"csv","origin":"https://a.example","blocked":true}""", "blocked")]
    [InlineData("""{"kind":"urls","format":"csv","conflict":true}""", "conflict")]
    [InlineData("""{"kind":"sitemaps","format":"csv","search":"x"}""", "search")]
    [InlineData("""{"kind":"sitemaps","format":"html"}""", "format")]
    [InlineData("""{"kind":"robotsOrigins","format":"csv","columns":["origin"]}""", "columns")]
    [InlineData("""{"kind":"linkGroups","format":"csv","columns":["to.url"]}""", "columns")]
    [InlineData("""{"kind":"robotsOrigins","format":"csv","origin":"https://a.example"}""", "origin")]
    [InlineData("""{"kind":"robotsOrigins","format":"csv","hiddenChecks":["title-missing"]}""", "hiddenChecks")]
    [InlineData("""{"kind":"report","format":"html","checks":["title-missing"]}""", "checks")]
    [InlineData("""{"kind":"workbook","format":"xlsx","checks":[""]}""", "checks")]
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

    /// <summary>
    /// A sidecar that is killed mid-export leaves the export's part files in the user's folder, and its note of them in
    /// its own. The next sidecar to start with that state folder deletes the files the note names, and those alone, and
    /// starts with an empty note. Nothing is killed here: the start state is written by hand.
    /// </summary>
    [Fact]
    public async Task AStartDeletesThePartFilesAnEarlierSidecarLeftNotedAndEmptiesTheNote()
    {
        var root = TestSidecar.NewRoot();
        try
        {
            var state = Path.Combine(root, "state");
            Directory.CreateDirectory(state);
            var part = Path.Combine(_folder, $".links.xlsx.{Guid.NewGuid():N}.tmp");
            var strings = part + ".strings";
            var gone = Path.Combine(_folder, $".urls.csv.{Guid.NewGuid():N}.tmp");
            var notOne = Path.Combine(_folder, "notes.tmp");
            var notNoted = Path.Combine(_folder, $".mine.csv.{Guid.NewGuid():N}.tmp");
            var whole = Path.Combine(_folder, "links.xlsx");
            foreach (var file in new[] { part, strings, notOne, notNoted, whole })
            {
                await File.WriteAllTextAsync(file, "written before the sidecar was killed");
            }

            // The note names the two part files, one that has gone since, a file that isn't a part file's name (a note
            // changed by hand), and nothing of the folder's other files.
            var note = Path.Combine(state, ExportJournal.FILE_NAME);
            await File.WriteAllTextAsync(note, new JsonArray(part, strings, gone, notOne, whole).ToJsonString());

            await using var sidecar = await TestSidecar.StartAsync(root);
            Assert.False(File.Exists(part), "The part file is still there.");
            Assert.False(File.Exists(strings), "The file set aside beside it is still there.");
            Assert.Equal(new[] { "links.xlsx", "notes.tmp", Path.GetFileName(notNoted) }.Order(StringComparer.Ordinal), Directory.GetFiles(_folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
            Assert.Empty(JsonNode.Parse(await File.ReadAllTextAsync(note))!.AsArray());
            Assert.Empty(sidecar.Host.Services.GetRequiredService<ExportJournal>().Parts);
        }
        finally
        {
            TestSidecar.DeleteRoot(root);
        }
    }

    /// <summary>While an export writes, the note in the state folder names its part files; once it has ended, it names none.</summary>
    [Fact]
    public async Task AnExportsPartFilesAreNotedWhileItWritesAndNotOnceItHasEnded()
    {
        var journal = Sidecar.Host.Services.GetRequiredService<ExportJournal>();
        var note = Path.Combine(Sidecar.Options.StateDirectory, ExportJournal.FILE_NAME);
        List<string> noted = [];
        List<string?> written = [];
        List<bool> there = [];
        Exports.Writing = (_, _) =>
        {
            if (noted.Count == 0)
            {
                noted.AddRange(journal.Parts);
                written.AddRange(JsonNode.Parse(File.ReadAllText(note))!.AsArray().Select(entry => (string?)entry));
                there.AddRange(noted.Select(File.Exists));
            }
        };
        var path = Path.Combine(_folder, "noted.xlsx");
        var done = await ExportAsync(grid.Id, """{"kind":"links","format":"xlsx"}""", path);
        Assert.Equal("done", (string?)done["status"]);

        Assert.Equal(2, noted.Count);
        Assert.All(noted, part => Assert.StartsWith(Path.Combine(_folder, ".noted.xlsx."), part, StringComparison.Ordinal));
        Assert.Equal([true, true], there);
        Assert.Equal(noted.Order(StringComparer.Ordinal), written.Order(StringComparer.Ordinal));
        Assert.Empty(journal.Parts);
        Assert.Empty(JsonNode.Parse(await File.ReadAllTextAsync(note))!.AsArray());
        Assert.Equal([path], Directory.GetFiles(_folder));
    }

    /// <summary>
    /// Shutdown cancels an export that is writing: its file is open and part-written when the sidecar is told to stop,
    /// and nothing of it is left, in the folder or in the note.
    /// </summary>
    [Fact]
    public async Task ShutdownCancelsAnExportThatIsWritingItsFileAndLeavesNoPartOfIt()
    {
        await using var sidecar = await TestSidecar.StartAsync();
        var crawl = Path.Combine(sidecar.DataDirectory, "grid.sicrawl");
        Directory.CreateDirectory(sidecar.DataDirectory);
        File.Copy(grid.Path, crawl);
        var exports = sidecar.Host.Services.GetRequiredService<ExportManager>();
        var journal = sidecar.Host.Services.GetRequiredService<ExportJournal>();

        // The export has read pages and written rows when it waits here, until it is cancelled.
        using var writing = new ManualResetEventSlim();
        exports.Writing = (job, progress) =>
        {
            if (progress.RowsWritten > 5_000)
            {
                writing.Set();
                job.Cancel.Token.WaitHandle.WaitOne(EventStreamReader.TIMEOUT);
            }
        };
        var path = Path.Combine(_folder, "stopped.csv");
        var started = await sidecar.PostAsync($"api/crawls/{CrawlIds.For(crawl)}/export", new JsonObject { ["kind"] = "links", ["format"] = "csv", ["path"] = path });
        Assert.True(started.Status == HttpStatusCode.Created, started.Body?.ToJsonString());
        Assert.True(writing.Wait(EventStreamReader.TIMEOUT), "The export never wrote 5,000 rows.");

        // Its file is open, and part-written (5,000 links are more than its buffer holds): there, beside where the file
        // would be, held by the export, and noted.
        var part = Assert.Single(Directory.GetFiles(_folder));
        Assert.StartsWith(".stopped.csv.", Path.GetFileName(part), StringComparison.Ordinal);
        Assert.True(new FileInfo(part).Length > 64 * 1_024, $"{new FileInfo(part).Length} bytes written yet.");
        Assert.ThrowsAny<IOException>(() => new FileStream(part, FileMode.Open, FileAccess.ReadWrite, FileShare.None).Dispose());
        var parts = new[] { part };
        Assert.Equal(parts.Order(StringComparer.Ordinal), journal.Parts.Order(StringComparer.Ordinal));

        await exports.StopAllAsync(CancellationToken.None).WaitAsync(EventStreamReader.TIMEOUT);
        Assert.Equal("cancelled", (string?)(await sidecar.GetJsonAsync($"api/exports/{(string)started.Body!["exportId"]!}"))["status"]);
        Assert.Empty(Directory.GetFiles(_folder));
        Assert.Empty(journal.Parts);
    }

    /// <summary>A crawl analysed again while it's exported ends the export with its own problem, and no file.</summary>
    [Fact]
    public async Task ACrawlAnalysedAgainUnderAnExportEndsItAsAProblemOfItsOwn()
    {
        var id = grid.Copy(_ => { });
        using var analysed = new ManualResetEventSlim();
        using var reading = new ManualResetEventSlim();
        var pages = 0;
        Exports.Writing = (_, progress) =>
        {
            if (progress.RowsWritten > 0 && ++pages == 1)
            {
                reading.Set();
                analysed.Wait(EventStreamReader.TIMEOUT);
            }
        };
        await using var events = await EventStreamReader.ConnectAsync(Sidecar.Client);
        var started = await Sidecar.PostAsync($"api/crawls/{id}/export", new JsonObject { ["kind"] = "links", ["format"] = "csv", ["path"] = Path.Combine(_folder, "changed.csv") });
        Assert.True(started.Status == HttpStatusCode.Created, started.Body?.ToJsonString());
        Assert.True(reading.Wait(EventStreamReader.TIMEOUT), "The export never wrote its first page.");

        // Between two of its pages the export holds nothing of the file: the analysis runs, and ends.
        Assert.Equal(HttpStatusCode.Accepted, (await Sidecar.PostAsync($"api/crawls/{id}/analyze")).Status);
        await events.CrawlStatusAsync(id, "analyzing");
        await events.CrawlStatusAsync(id, "completed");
        analysed.Set();

        var ended = await EndedAsync((string)started.Body!["exportId"]!);
        Assert.Equal("failed", (string?)ended["status"]);
        Assert.Null(ended["result"]);
        Assert.Equal(("export-crawl-changed", 409), ((string?)ended["error"]!["type"], (int)ended["error"]!["status"]!));
        Assert.Equal("The crawl changed while it was being exported: it was analysed again, continued or it finished. Export it again.", (string?)ended["error"]!["title"]);
        Assert.Empty(Directory.GetFiles(_folder));
    }

    /// <summary>A disk that fills up under an export ends it with its own problem, and nothing is left of the file.</summary>
    [Theory]
    [InlineData("csv")]
    [InlineData("xlsx")]
    public async Task ADiskThatFillsUpEndsAnExportAsAProblemOfItsOwn(string format)
    {
        // On the export's own thread, before it opens its file: every file it writes has room for 4,000 bytes more.
        Exports.Starting = _ => ExportTarget.Wrap = file => new FullDisk(file, 4_000);
        var failed = await ExportAsync(grid.Id, $$"""{"kind":"links","format":"{{format}}"}""", Path.Combine(_folder, $"full.{format}"));
        Assert.Equal("failed", (string?)failed["status"]);
        Assert.Null(failed["result"]);
        Assert.Equal(("export-disk-full", 507), ((string?)failed["error"]!["type"], (int)failed["error"]!["status"]!));
        Assert.StartsWith("The disk is full", (string?)failed["error"]!["title"], StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(_folder));
        Assert.Empty(Sidecar.Host.Services.GetRequiredService<ExportJournal>().Parts);
    }

    /// <summary>More rows than a workbook holds end the export with its own problem before anything is written; CSV takes them.</summary>
    [Fact]
    public async Task MoreRowsThanAWorkbookHoldsEndAnExportAsAProblemOfItsOwn()
    {
        Exports.MaxXlsxRows = 100;
        var failed = await ExportAsync(grid.Id, """{"kind":"links","format":"xlsx"}""", Path.Combine(_folder, "large.xlsx"));
        Assert.Equal("failed", (string?)failed["status"]);
        Assert.Equal(("export-too-large", 422), ((string?)failed["error"]!["type"], (int)failed["error"]!["status"]!));
        Assert.Contains("more than an Excel file can hold here (100)", (string?)failed["error"]!["title"], StringComparison.Ordinal);
        Assert.Contains("CSV, which has no limit", (string?)failed["error"]!["title"], StringComparison.Ordinal);
        Assert.Equal(0, (long)failed["rowsWritten"]!);
        Assert.Empty(Directory.GetFiles(_folder));

        var csv = await ExportAsync(grid.Id, """{"kind":"links","format":"csv"}""", Path.Combine(_folder, "large.csv"));
        Assert.Equal("done", (string?)csv["status"]);
        Assert.True((long)csv["result"]!["rows"]! > 100);
    }

    /// <summary>
    /// An export's progress is told at most four times a second, however many pages it reads: its first event, one as
    /// its rows are counted, then no more than one for every 250 ms it runs, and its last. None is merged or dropped.
    /// </summary>
    [Fact]
    public async Task AnExportsProgressIsToldAtMostEveryQuarterSecond()
    {
        var rows = GridExportReader.LinkPageRows;
        try
        {
            // Many small pages, each a little slow: hundreds of steps of progress over a second or more.
            GridExportReader.LinkPageRows = 25;
            var pages = 0;
            Exports.Writing = (_, progress) =>
            {
                if (progress.RowsWritten > 0)
                {
                    pages++;
                    Thread.Sleep(5);
                }
            };
            await using var events = await EventStreamReader.ConnectAsync(Sidecar.Client);
            var started = await Sidecar.PostAsync($"api/crawls/{grid.Id}/export", new JsonObject { ["kind"] = "links", ["format"] = "csv", ["path"] = Path.Combine(_folder, "paced.csv") });
            var id = (string)started.Body!["exportId"]!;
            List<JsonNode> told = [];
            do
            {
                told.Add(await events.NextAsync("export", data => (string?)data["exportId"] == id));
            }
            while ((string?)told[^1]["status"] == "running");

            var last = told[^1];
            Assert.Equal("done", (string?)last["status"]);
            var took = DateTimeOffset.Parse((string)last["finishedAt"]!, System.Globalization.CultureInfo.InvariantCulture)
                       - DateTimeOffset.Parse((string)last["startedAt"]!, System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(pages > 100, $"{pages} pages.");
            Assert.True(took > TimeSpan.FromMilliseconds(600), $"It took {took.TotalMilliseconds:N0} ms.");

            // The first (nothing written, nothing counted), the count, one each 250 ms at most, and the end.
            Assert.Equal((0L, (long?)null), ((long)told[0]["rowsWritten"]!, (long?)told[0]["rowsExpected"]));
            Assert.Equal((long)last["result"]!["rows"]!, (long?)told[1]["rowsExpected"]);
            Assert.True(told.Count >= 4, $"{told.Count} events.");
            Assert.True(told.Count <= (took.TotalMilliseconds / 250) + 3, $"{told.Count} events in {took.TotalMilliseconds:N0} ms, for {pages} pages.");
            Assert.All(told.Zip(told.Skip(1)), pair => Assert.True((long)pair.First["rowsWritten"]! <= (long)pair.Second["rowsWritten"]!));
            Assert.Equal((long)last["result"]!["rows"]!, (long)last["rowsWritten"]!);
        }
        finally
        {
            GridExportReader.LinkPageRows = rows;
        }
    }

    /// <summary>The last 64 exports that ended can be read by their ids; the one before them is no longer there.</summary>
    [Fact]
    public async Task TheLastSixtyFourExportsThatEndedCanStillBeRead()
    {
        const string ONE_ROW = """{"kind":"urls","format":"csv","filters":[{"column":"id","op":"eq","value":1}]}""";
        var path = Path.Combine(_folder, "one.csv");
        List<string> ids = [];
        for (var export = 0; export < 66; export++)
        {
            var ended = await ExportAsync(grid.Id, ONE_ROW, path);
            Assert.Equal("done", (string?)ended["status"]);
            ids.Add((string)ended["exportId"]!);
        }

        // 66 ended: the first two have gone, and the 64 after them are all there, as they ended.
        TestSidecar.AssertProblem(await Sidecar.SendAsync(HttpMethod.Get, $"api/exports/{ids[0]}"), HttpStatusCode.NotFound, "export-not-found");
        TestSidecar.AssertProblem(await Sidecar.SendAsync(HttpMethod.Get, $"api/exports/{ids[1]}"), HttpStatusCode.NotFound, "export-not-found");
        TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/exports/{ids[1]}/cancel"), HttpStatusCode.NotFound, "export-not-found");
        foreach (var id in ids.Skip(2))
        {
            Assert.Equal("done", (string?)(await Sidecar.GetJsonAsync($"api/exports/{id}"))["status"]);
        }
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

    /// <summary>A file on a disk with room for only so many bytes more: writing past them fails as Windows, or Linux, fails it.</summary>
    private sealed class FullDisk(Stream file, long roomBytes) : Stream
    {
        private long _written;

        public override bool CanRead => file.CanRead;

        public override bool CanSeek => file.CanSeek;

        public override bool CanWrite => file.CanWrite;

        public override long Length => file.Length;

        public override long Position
        {
            get => file.Position;
            set => file.Position = value;
        }

        public override void Flush() => file.Flush();

        public override int Read(byte[] buffer, int offset, int count) => file.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => file.Seek(offset, origin);

        public override void SetLength(long value) => file.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count)
        {
            _written += count;
            if (_written > roomBytes)
            {
                throw OperatingSystem.IsWindows()
                    ? new IOException("There is not enough space on the disk.", unchecked((int)0x80070070))
                    : new IOException("No space left on device", 28);
            }

            file.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                file.Dispose();
            }

            base.Dispose(disposing);
        }
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
