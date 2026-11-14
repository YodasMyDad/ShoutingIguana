using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Sidecar.Crawls;
using ShoutingIguana.Sidecar.Library;
using ShoutingIguana.Sidecar.Tests.Support;

namespace ShoutingIguana.Sidecar.Tests;

/// <summary>
/// How the sidecar serves the grids under load and to clients it can't trust to behave: a request given up stops its
/// read, reads beyond their slots queue without holding the rest of the API up, a list with a null in it or a value an
/// operator doesn't take is a field error, a stored value this engine doesn't know is null, a big page's source is sent
/// without being held whole, and no answer can be taken for a page.
/// </summary>
[Collection(SoloCollection.NAME)]
public sealed class GridServingTests(GridSidecar grid) : IClassFixture<GridSidecar>
{
    private TestSidecar Sidecar => grid.Sidecar;

    [Fact]
    public async Task ARequestGivenUpStopsItsRead()
    {
        var data = Sidecar.Host.Services.GetRequiredService<GridData>();
        var stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        data.ReadStarting = token => stopped.TrySetResult(token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)));
        try
        {
            using var giveUp = new CancellationTokenSource();
            var request = Sidecar.Client.PostAsync($"api/crawls/{grid.Id}/urls", null, giveUp.Token);
            await Task.Delay(300);
            giveUp.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
            Assert.True(await stopped.Task.WaitAsync(TimeSpan.FromSeconds(15)), "The read's token wasn't cancelled when its request was given up.");
        }
        finally
        {
            data.ReadStarting = null;
        }
    }

    /// <summary>
    /// A burst of grid reads beyond their slots queues holding no thread: the rest of the API answers at once, and a read
    /// given up while it queues never runs.
    /// </summary>
    [Fact]
    public async Task GridReadsQueueBeyondTheirSlotsWithoutHoldingUpTheRest()
    {
        await using var sidecar = await TestSidecar.StartAsync(configure: options => options with { GridReads = 1 });
        Directory.CreateDirectory(sidecar.DataDirectory);
        var path = Path.Combine(sidecar.DataDirectory, "site.sicrawl");
        File.Copy(grid.Path, path);
        var id = CrawlIds.For(path);
        var data = sidecar.Host.Services.GetRequiredService<GridData>();
        using var holding = new ManualResetEventSlim();
        using var held = new SemaphoreSlim(0);
        var started = 0;
        data.ReadStarting = _ =>
        {
            if (Interlocked.Increment(ref started) == 1)
            {
                held.Release();
                holding.Wait(TimeSpan.FromSeconds(30));
            }
        };

        var first = sidecar.PostAsync($"api/crawls/{id}/urls");
        Assert.True(await held.WaitAsync(TimeSpan.FromSeconds(10)));
        var health = Stopwatch.StartNew();
        await sidecar.GetJsonAsync("api/health");
        Assert.InRange(health.ElapsedMilliseconds, 0, 1_000);

        using var giveUp = new CancellationTokenSource();
        var queued = sidecar.Client.PostAsync($"api/crawls/{id}/urls", null, giveUp.Token);
        await Task.Delay(300);
        Assert.Equal(1, Volatile.Read(ref started));
        giveUp.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        await Task.Delay(300);
        holding.Set();
        Assert.Equal(HttpStatusCode.OK, (await first).Status);
        Assert.Equal(HttpStatusCode.OK, (await sidecar.PostAsync($"api/crawls/{id}/urls")).Status);
        Assert.Equal(2, Volatile.Read(ref started));
    }

    /// <summary>
    /// The crawl's headline numbers, once kept, take no read slot: with every slot held by another crawl's read (a pass
    /// over its links, on a machine with few cores), Overview's numbers still answer at once. Numbers not yet read are
    /// a pass of their own, and queue as any read does.
    /// </summary>
    [Fact]
    public async Task KeptStatsTakeNoReadSlot()
    {
        await using var sidecar = await TestSidecar.StartAsync(configure: options => options with { GridReads = 1 });
        Directory.CreateDirectory(sidecar.DataDirectory);
        var (kept, unread) = (Path.Combine(sidecar.DataDirectory, "kept.sicrawl"), Path.Combine(sidecar.DataDirectory, "unread.sicrawl"));
        File.Copy(grid.Path, kept);
        File.Copy(grid.Path, unread);
        var first = await sidecar.GetJsonAsync($"api/crawls/{CrawlIds.For(kept)}/stats");

        var data = sidecar.Host.Services.GetRequiredService<GridData>();
        using var holding = new ManualResetEventSlim();
        using var held = new SemaphoreSlim(0);
        var started = 0;
        data.ReadStarting = _ =>
        {
            if (Interlocked.Increment(ref started) == 1)
            {
                held.Release();
                holding.Wait(TimeSpan.FromSeconds(30));
            }
        };

        // The one slot is held by a read of the other crawl.
        var holder = sidecar.PostAsync($"api/crawls/{CrawlIds.For(unread)}/urls");
        Assert.True(await held.WaitAsync(TimeSpan.FromSeconds(10)));
        try
        {
            var again = await sidecar.GetJsonAsync($"api/crawls/{CrawlIds.For(kept)}/stats").WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(first.ToJsonString(), again.ToJsonString());
            Assert.Equal(1, Volatile.Read(ref started));

            // The other crawl's numbers were never read: they wait for the slot.
            var queued = sidecar.GetJsonAsync($"api/crawls/{CrawlIds.For(unread)}/stats");
            await Task.Delay(300);
            Assert.False(queued.IsCompleted);
            holding.Set();
            Assert.Equal((long?)first["urlCount"], (long?)(await queued.WaitAsync(TimeSpan.FromSeconds(10)))["urlCount"]);
        }
        finally
        {
            holding.Set();
            await holder;
        }
    }

    [Theory]
    [InlineData("urls", """{"columns":[null]}""", "columns[0]")]
    [InlineData("urls", """{"filters":[null]}""", "filters[0]")]
    [InlineData("urls", """{"sort":[null]}""", "sort[0]")]
    [InlineData("urls/facets", """{"filters":[null]}""", "filters[0]")]
    [InlineData("urls/facets", """{"facets":[null]}""", "facets[0]")]
    [InlineData("issues/title-too-long/rows", """{"filters":[null]}""", "filters[0]")]
    [InlineData("urls/inlinks", """{"urlId":1,"kinds":[null]}""", "kinds[0]")]
    [InlineData("urls/inlinks", """{"urlId":1,"kinds":["hyperlink","image","hyperlink"]}""", "kinds[2]")]
    [InlineData("urls", """{"filters":[{"column":"statusCode","op":"eq","value":200,"values":[200]}]}""", "filters[0].values")]
    [InlineData("urls", """{"filters":[{"column":"type","op":"in","values":["page"],"value":"page"}]}""", "filters[0].value")]
    [InlineData("urls", """{"filters":[{"column":"depth","op":"eq","value":1,"min":0}]}""", "filters[0].min")]
    [InlineData("urls", """{"filters":[{"column":"title","op":"isEmpty","max":3}]}""", "filters[0].max")]
    [InlineData("urls", """{"filters":[{"column":"check","op":"in","values":["title-too-long"],"value":"x"}]}""", "filters[0].value")]
    public async Task WhatAQueryCantUseIsAFieldError(string path, string body, string field)
    {
        var refused = TestSidecar.AssertProblem(await Sidecar.PostAsync($"api/crawls/{grid.Id}/{path}", JsonNode.Parse(body)),
            HttpStatusCode.UnprocessableEntity, "invalid-query");
        Assert.Contains(field, refused["errors"]!.AsObject().Select(error => error.Key));
    }

    /// <summary>
    /// The library's pages are one number with its analysis's (round 5, gap 3): <c>pageCount</c>, the internal HTML pages
    /// the crawl parsed (live and once it has ended), and <c>pagesAnalyzed</c>, the pages the last analysis audited, which
    /// its score covers, are the same while the crawl hasn't changed since. Cloudflare's own <c>/cdn-cgi/</c> endpoints,
    /// of which the synthetic site serves one, are in neither: they made the 100k crawl's 101,107 against 101,106.
    /// </summary>
    [Fact]
    public async Task TheLibrarysPagesAreThePagesItsAnalysisAudited()
    {
        var item = await Sidecar.GetJsonAsync($"api/crawls/{grid.Id}");
        var analysis = await Sidecar.GetJsonAsync($"api/crawls/{grid.Id}/analysis");
        var expected = grid.Site.Site.GetExpectations();
        Assert.Equal(expected.HtmlPages, (long)item["pageCount"]!);
        Assert.Equal(expected.AnalysedPages, (long)analysis["pagesAnalyzed"]!);
        Assert.Equal((long)analysis["pagesAnalyzed"]!, (long?)item["pagesAnalyzed"]);
        Assert.Equal((long)analysis["score"]!["covers"]!["pages"]!, (long?)item["pagesAnalyzed"]);
        Assert.Equal((long)item["pageCount"]!, (long?)item["pagesAnalyzed"]);

        // The crawl did read Cloudflare's page: it's left out of the count, not unfetched.
        long cloudflare;
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = grid.Path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM urls u JOIN responses r ON r.url_id = u.id WHERE {PARSED_HTML} AND instr(u.url, '/cdn-cgi/') > 0";
            cloudflare = (long)command.ExecuteScalar()!;
        }

        Assert.Equal(1, cloudflare);

        // A crawl never analysed has no analysis's count: null, never 0.
        Assert.Null((await Sidecar.GetJsonAsync($"api/crawls/{grid.FreshId}"))["pagesAnalyzed"]);
    }

    /// <summary>An internal URL that answered with HTML the crawler parsed: a page, but for Cloudflare's endpoints.</summary>
    private const string PARSED_HTML = "u.is_internal = 1 AND r.is_html = 1 AND r.outcome = 0 AND r.word_count IS NOT NULL";

    /// <summary>
    /// The crawl's headline numbers are read as a grid is, in a read slot with its request's token, and kept with the
    /// grids' counts: read once while the crawl stands still, and again once a run has stamped it.
    /// </summary>
    [Fact]
    public async Task TheStatsAreReadAsAGridIsAndKept()
    {
        var path = string.Empty;
        var id = grid.Copy(copy => path = copy);
        var data = Sidecar.Host.Services.GetRequiredService<GridData>();
        var reads = 0;
        data.ReadStarting = _ => Interlocked.Increment(ref reads);
        try
        {
            var expected = CrawlStatsReader.Read(path);
            var stats = await Sidecar.GetJsonAsync($"api/crawls/{id}/stats");
            Assert.Equal(1, Volatile.Read(ref reads));
            Assert.Equal((expected.UrlCount, expected.LinkCount, expected.HtmlPageCount, expected.UnlinkedPageCount, expected.IndexablePageCount),
                ((long)stats["urlCount"]!, (long)stats["linkCount"]!, (long)stats["htmlPageCount"]!, (long)stats["unlinkedPageCount"]!, (long)stats["indexablePageCount"]!));
            Assert.True(expected.UnlinkedPageCount > 0 && expected.NonIndexableReasons.Count > 0);
            Assert.Equal(expected.NonIndexableReasons.Values.Sum(), stats["nonIndexableReasons"]!.AsObject().Sum(reason => (long)reason.Value!));
            Assert.Equal(expected.Outcomes.Values.Sum(), stats["outcomes"]!.AsObject().Sum(outcome => (long)outcome.Value!));

            // Changed behind the crawl's back, its stamp as it was: the numbers kept are given again.
            Change(path, "DELETE FROM urls WHERE id = (SELECT MAX(id) FROM urls)");
            Assert.Equal(expected.UrlCount, (long?)(await Sidecar.GetJsonAsync($"api/crawls/{id}/stats"))["urlCount"]);
            Change(path, "UPDATE crawl SET updated_at = updated_at + 1000");
            Assert.Equal(expected.UrlCount - 1, (long?)(await Sidecar.GetJsonAsync($"api/crawls/{id}/stats"))["urlCount"]);
        }
        finally
        {
            data.ReadStarting = null;
        }
    }

    [Fact]
    public async Task StoredValuesThisEngineDoesntKnowAreNull()
    {
        var home = UrlGridReader.Lookup(grid.Path, grid.Url("/"))!.Id;
        var id = grid.Copy(copy =>
        {
            Change(copy, $"UPDATE links SET kind = 99, position = 42 WHERE rowid = (SELECT MIN(rowid) FROM links WHERE from_id = {home} AND to_id <> {home})");
            Change(copy, $"UPDATE urls SET source = 9 WHERE id = {home}");
            Change(copy, $"UPDATE responses SET outcome = 50, response_ms = 9e999 WHERE url_id = {home}");
        });

        var inspected = await PostOkAsync($"api/crawls/{id}/urls/inspect", new JsonObject { ["urlId"] = home });
        Assert.Null(inspected["discovery"]!["source"]);
        Assert.Null(inspected["response"]!["outcome"]);
        Assert.Null(inspected["response"]!["responseMs"]);

        var outlinks = await PostOkAsync($"api/crawls/{id}/urls/outlinks", new JsonObject { ["urlId"] = home, ["limit"] = 1_000 });
        var unknown = Assert.Single(outlinks["rows"]!.AsArray(), row => row!["kind"] is null);
        Assert.Null(unknown!["position"]);
        var grid0 = await PostOkAsync($"api/crawls/{id}/urls", JsonNode.Parse($$"""{"columns":["outcome","source"],"filters":[{"column":"id","op":"eq","value":{{home}}}]}"""));
        Assert.Null(grid0["rows"]![0]!["outcome"]);
    }

    [Fact]
    public async Task NoAnswerCanBeTakenForAPage()
    {
        foreach (var (method, path) in new[] { (HttpMethod.Get, "api/url-columns"), (HttpMethod.Post, $"api/crawls/{grid.Id}/urls"), (HttpMethod.Post, "api/crawls/0123456789abcdef/urls") })
        {
            using var response = await Sidecar.Client.SendAsync(new HttpRequestMessage(method, path));
            Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
            Assert.Contains("default-src 'none'", Assert.Single(response.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A big page's source is written as it's unpacked: one view of 16 MB of HTML the JSON escapes most of (markup,
    /// ampersands, CJK) allocates a few megabytes (4 MB), not the page and its escaped copies (hundreds), and gives the
    /// page exactly.
    /// </summary>
    [Fact]
    public async Task TheSourceOfABigPageIsSentWithoutBeingHeldWhole()
    {
        var home = UrlGridReader.Lookup(grid.Path, grid.Url("/"))!.Id;
        const string PIECE = "<p class=\"x\">Rattan &amp; teak, 籐の家具 — \"sets\" & 'chairs'</p>\n";
        var bytes = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(PIECE, UrlInspectorReader.MAX_SOURCE_BYTES / Encoding.UTF8.GetByteCount(PIECE) + 1_000)));
        Assert.True(bytes.Length > UrlInspectorReader.MAX_SOURCE_BYTES);

        // Cut at the limit, the character the cut falls inside left out.
        var kept = Encoding.UTF8.GetString(bytes, 0, UrlInspectorReader.MAX_SOURCE_BYTES).TrimEnd('�');
        var id = grid.Copy(copy => StoreHtml(copy, home, bytes));

        // Read as the webview reads it, a piece at a time, and nothing else allocating meanwhile.
        GC.Collect();
        var before = GC.GetTotalAllocatedBytes(precise: true);
        using (var request = new HttpRequestMessage(HttpMethod.Post, $"api/crawls/{id}/urls/source") { Content = Json(new JsonObject { ["urlId"] = home }) })
        using (var response = await Sidecar.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await using var body = await response.Content.ReadAsStreamAsync();
            await body.CopyToAsync(Stream.Null);
        }

        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        Assert.True(allocated < UrlInspectorReader.MAX_SOURCE_BYTES, $"One view of a 16 MB page allocated {allocated / 1024 / 1024} MB");

        var source = await PostOkAsync($"api/crawls/{id}/urls/source", new JsonObject { ["urlId"] = home });
        Assert.Equal(["stored", "html", "bytes", "truncated"], source.AsObject().Select(property => property.Key));
        Assert.Equal(bytes.Length, (long?)source["bytes"]);
        Assert.True((bool)source["truncated"]!);
        Assert.Equal(kept, (string)source["html"]!);
    }

    private static StringContent Json(JsonNode body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");

    private async Task<JsonNode> PostOkAsync(string path, JsonNode? body = null)
    {
        var (status, answer) = await Sidecar.PostAsync(path, body);
        Assert.True(status == HttpStatusCode.OK, $"POST {path}: {(int)status} {answer?.ToJsonString()}");
        return answer!;
    }

    private static void Change(string path, string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }

    private static void StoreHtml(string path, int id, byte[] html)
    {
        using var packed = new MemoryStream();
        using (var brotli = new BrotliStream(packed, CompressionLevel.Fastest, leaveOpen: true))
        {
            brotli.Write(html);
        }

        Change(path, "UPDATE response_html SET html = $html WHERE url_id = $id", ("$html", packed.ToArray()), ("$id", id));
    }
}
