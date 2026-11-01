using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Sidecar.Library;
using ShoutingIguana.Sidecar.Tests.Support;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Sidecar.Tests;

/// <summary>The library: the data folder's crawls, files opened from elsewhere, removal, restore and deletion.</summary>
public sealed class LibraryTests : IAsyncLifetime
{
    private readonly string _elsewhere = Path.Combine(Path.GetTempPath(), $"si-elsewhere-{Guid.NewGuid():N}");
    private SyntheticSiteServer _site = null!;
    private TestSidecar _sidecar = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_elsewhere);
        _site = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 60 });
        _sidecar = await TestSidecar.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _sidecar.DisposeAsync();
        await _site.DisposeAsync();
        TestSidecar.DeleteRoot(_elsewhere);
    }

    [Fact]
    public async Task ListsTheDataFolderAndOpenedFilesButNotRemovedOnes()
    {
        var inData = await TestCrawlFiles.CrawlAsync(_site, Path.Combine(_sidecar.DataDirectory, "a.sicrawl"), analyse: true);
        var outside = await TestCrawlFiles.CrawlAsync(_site, Path.Combine(_elsewhere, "b.sicrawl"));
        await TestCrawlFiles.CrawlAsync(_site, Path.Combine(_sidecar.DataDirectory, "nested", "c.sicrawl"));
        await File.WriteAllTextAsync(Path.Combine(_sidecar.DataDirectory, "notes.sicrawl"), "not a crawl");

        // Only the top of the data folder, and only crawls.
        var a = Assert.Single(await ListAsync());
        var stats = CrawlStatsReader.Read(inData);
        Assert.Equal(CrawlIds.For(inData), (string?)a["id"]);
        Assert.Equal(16, ((string?)a["id"])!.Length);
        Assert.Equal(inData, (string?)a["path"]);
        Assert.Equal("a.sicrawl", (string?)a["fileName"]);
        Assert.Equal("127.0.0.1", (string?)a["site"]);
        Assert.Equal(_site.BaseUri.AbsoluteUri, (string?)a["startUrl"]);
        Assert.Equal("completed", (string?)a["status"]);
        Assert.Equal(stats.Outcomes.Values.Sum(), (long?)a["urlCount"]);
        Assert.Equal(stats.HtmlPageCount, (long?)a["pageCount"]);
        Assert.Equal(new FileInfo(inData).Length, (long?)a["sizeBytes"]);
        Assert.NotNull(a["startedAt"]);
        Assert.NotNull(a["finishedAt"]);
        Assert.EndsWith("Z", (string?)a["analyzedAt"]);
        Assert.False((bool?)a["outOfDate"]);
        Assert.False((bool?)a["missing"]);
        AssertIssueCounts(inData, a["issues"]!);

        // A file from anywhere can be added; it isn't analysed, so it has no issue counts rather than zero.
        var opened = await _sidecar.PostAsync("api/crawls/open", new JsonObject { ["path"] = outside });
        Assert.Equal(HttpStatusCode.OK, opened.Status);
        Assert.Equal(outside, (string?)opened.Body!["path"]);
        Assert.Null(opened.Body["issues"]);
        Assert.Null(opened.Body["analyzedAt"]);
        var b = (string)opened.Body["id"]!;
        Assert.Equal(new[] { CrawlIds.For(inData), b }.Order(StringComparer.Ordinal), (await ListAsync()).Select(item => (string)item["id"]!).Order(StringComparer.Ordinal));

        // Removed from the library, not from the disk, and back again.
        var aId = (string)a["id"]!;
        Assert.Equal(HttpStatusCode.NoContent, (await _sidecar.SendAsync(HttpMethod.Delete, $"api/crawls/{aId}")).Status);
        Assert.Equal([b], (await ListAsync()).Select(item => (string?)item["id"]));
        Assert.True(File.Exists(inData));
        var restored = await _sidecar.PostAsync($"api/crawls/{aId}/restore");
        Assert.Equal(HttpStatusCode.OK, restored.Status);
        Assert.Equal(aId, (string?)restored.Body!["id"]);
        Assert.Equal(2, (await ListAsync()).Count);

        TestSidecar.AssertProblem(await _sidecar.PostAsync("api/crawls/0123456789abcdef/restore"), HttpStatusCode.NotFound, "crawl-not-found");
        TestSidecar.AssertProblem(await _sidecar.SendAsync(HttpMethod.Get, "api/crawls/not-an-id"), HttpStatusCode.NotFound, "crawl-not-found");
    }

    [Fact]
    public async Task TheLibraryOutlivesTheSidecarAndAMissingFileStaysListed()
    {
        var root = TestSidecar.NewRoot();
        try
        {
            var outside = await TestCrawlFiles.CrawlAsync(_site, Path.Combine(_elsewhere, "kept.sicrawl"));
            var gone = await TestCrawlFiles.CrawlAsync(_site, Path.Combine(_elsewhere, "gone.sicrawl"));
            var removed = await TestCrawlFiles.CrawlAsync(_site, Path.Combine(root, "data", "removed.sicrawl"));
            await using (var first = await TestSidecar.StartAsync(root))
            {
                Assert.Equal(HttpStatusCode.OK, (await first.PostAsync("api/crawls/open", new JsonObject { ["path"] = outside })).Status);
                Assert.Equal(HttpStatusCode.OK, (await first.PostAsync("api/crawls/open", new JsonObject { ["path"] = gone })).Status);
                Assert.Equal(HttpStatusCode.NoContent, (await first.SendAsync(HttpMethod.Delete, $"api/crawls/{CrawlIds.For(removed)}")).Status);
            }

            File.Delete(gone);
            await using var second = await TestSidecar.StartAsync(root);
            var listed = (await second.GetJsonAsync("api/crawls")).AsArray();

            Assert.Equal([gone, outside], listed.Select(item => (string?)item!["path"]).Order(StringComparer.Ordinal));
            var missing = listed.Single(item => (string?)item!["path"] == gone)!;
            Assert.True((bool?)missing["missing"]);
            Assert.Equal("127.0.0.1", (string?)missing["site"]);
            Assert.False((bool?)listed.Single(item => (string?)item!["path"] == outside)!["missing"]);

            // Opening a removed crawl brings it back.
            Assert.Equal(HttpStatusCode.OK, (await second.PostAsync("api/crawls/open", new JsonObject { ["path"] = removed })).Status);
            Assert.Equal(3, (await second.GetJsonAsync("api/crawls")).AsArray().Count);
        }
        finally
        {
            TestSidecar.DeleteRoot(root);
        }
    }

    [Fact]
    public async Task DeletingAFileTakesItsCompanionsAndIsRefusedWhileAnotherProgramWritesIt()
    {
        var path = await TestCrawlFiles.CrawlAsync(_site, Path.Combine(_sidecar.DataDirectory, "delete-me.sicrawl"));
        var id = CrawlIds.For(path);

        using (TestCrawlFiles.HoldLock(path))
        {
            Assert.Equal("inUse", (string?)(await _sidecar.GetJsonAsync($"api/crawls/{id}"))["status"]);
            TestSidecar.AssertProblem(await _sidecar.PostAsync($"api/crawls/{id}/delete-file"), HttpStatusCode.Conflict, "crawl-busy");
            Assert.True(File.Exists(path));
        }

        // What SQLite and a lock on Linux or macOS can leave beside a crawl.
        await File.WriteAllTextAsync(path + "-shm", string.Empty);
        await File.WriteAllTextAsync(path + "-lock", string.Empty);
        Assert.Equal("completed", (string?)(await _sidecar.GetJsonAsync($"api/crawls/{id}"))["status"]);

        Assert.Equal(HttpStatusCode.NoContent, (await _sidecar.PostAsync($"api/crawls/{id}/delete-file")).Status);

        Assert.All(new[] { path, path + "-wal", path + "-shm", path + "-lock" }, file => Assert.False(File.Exists(file), file));
        Assert.Empty(await ListAsync());
        TestSidecar.AssertProblem(await _sidecar.SendAsync(HttpMethod.Get, $"api/crawls/{id}"), HttpStatusCode.NotFound, "crawl-not-found");
    }

    [Fact]
    public async Task ACrawlThatSaysItIsRunningIsInterruptedUnlessSomethingHoldsIt()
    {
        var path = await TestCrawlFiles.CrawlAsync(_site, Path.Combine(_sidecar.DataDirectory, "crashed.sicrawl"));
        var id = CrawlIds.For(path);
        TestCrawlFiles.SetStoredStatus(path, CrawlStatus.Running);

        Assert.Equal("interrupted", (string?)(await _sidecar.GetJsonAsync($"api/crawls/{id}"))["status"]);
        TestSidecar.AssertProblem(await _sidecar.PostAsync($"api/crawls/{id}/analyze"), HttpStatusCode.Conflict, "not-analysable");

        using (TestCrawlFiles.HoldLock(path))
        {
            Assert.Equal("inUse", (string?)(await _sidecar.GetJsonAsync($"api/crawls/{id}"))["status"]);
            TestSidecar.AssertProblem(await _sidecar.PostAsync($"api/crawls/{id}/analyze"), HttpStatusCode.Conflict, "not-analysable");
            TestSidecar.AssertProblem(await _sidecar.PostAsync($"api/crawls/{id}/continue"), HttpStatusCode.Conflict, "crawl-busy");
        }

        // Continued from the file, it finishes (its URL limit is already reached) and is analysed.
        await using var events = await EventStreamReader.ConnectAsync(_sidecar.Client);
        var continued = await _sidecar.PostAsync($"api/crawls/{id}/continue");
        Assert.Equal(HttpStatusCode.OK, continued.Status);
        await events.CrawlStatusAsync(id, "completed");
        await events.CrawlStatusAsync(id, "analyzing");
        await events.CrawlStatusAsync(id, "completed");
        var item = await _sidecar.GetJsonAsync($"api/crawls/{id}");
        Assert.Equal("completed", (string?)item["status"]);
        Assert.NotNull(item["analyzedAt"]);
    }

    [Fact]
    public async Task EveryChangeToTheLibraryIsAnnounced()
    {
        var path = await TestCrawlFiles.CrawlAsync(_site, Path.Combine(_elsewhere, "announced.sicrawl"));
        var id = CrawlIds.For(path);
        await using var events = await EventStreamReader.ConnectAsync(_sidecar.Client);
        await events.NextAsync("hello");

        Assert.Equal(HttpStatusCode.OK, (await Open(path)).Status);
        await events.NextAsync("library");
        Assert.Equal(HttpStatusCode.NoContent, (await _sidecar.SendAsync(HttpMethod.Delete, $"api/crawls/{id}")).Status);
        await events.NextAsync("library");
        Assert.Equal(HttpStatusCode.OK, (await _sidecar.PostAsync($"api/crawls/{id}/restore")).Status);
        await events.NextAsync("library");
        Assert.Equal(HttpStatusCode.NoContent, (await _sidecar.PostAsync($"api/crawls/{id}/delete-file")).Status);
        await events.NextAsync("library");
        Assert.Equal("{}", events.Seen()[^1].Data.ToJsonString());
    }

    [Fact]
    public async Task OnlyCrawlFilesCanBeOpened()
    {
        var text = Path.Combine(_elsewhere, "notes.sicrawl");
        await File.WriteAllTextAsync(text, "not a crawl");
        var notACrawl = TestSidecar.AssertProblem(await Open(text), HttpStatusCode.BadRequest, "not-a-crawl");
        Assert.Equal("That file isn't a Shouting Iguana crawl.", (string?)notACrawl["title"]);

        var database = Path.Combine(_elsewhere, "other.sicrawl");
        using (var connection = new SqliteConnection($"Data Source={database};Pooling=False"))
        {
            connection.Open();
            using var create = connection.CreateCommand();
            create.CommandText = "CREATE TABLE notes (text TEXT)";
            create.ExecuteNonQuery();
        }

        Assert.Contains("not a crawl database", (string?)TestSidecar.AssertProblem(await Open(database), HttpStatusCode.BadRequest, "not-a-crawl")["title"]);
        TestSidecar.AssertProblem(await Open(Path.Combine(_elsewhere, "crawl.txt")), HttpStatusCode.BadRequest, "not-a-crawl");
        TestSidecar.AssertProblem(await Open("relative.sicrawl"), HttpStatusCode.BadRequest, "bad-request");
        TestSidecar.AssertProblem(await Open(Path.Combine(_elsewhere, "missing.sicrawl")), HttpStatusCode.BadRequest, "file-error");
        TestSidecar.AssertProblem(await _sidecar.PostAsync("api/crawls/open", new JsonObject()), HttpStatusCode.BadRequest, "bad-request");
        TestSidecar.AssertProblem(await _sidecar.PostAsync("api/crawls/open", JsonValue.Create("not an object")), HttpStatusCode.BadRequest, "bad-request");
        Assert.Empty(await ListAsync());
    }

    private Task<(HttpStatusCode Status, JsonNode? Body)> Open(string path) => _sidecar.PostAsync("api/crawls/open", new JsonObject { ["path"] = path });

    private async Task<List<JsonNode>> ListAsync() => [.. (await _sidecar.GetJsonAsync("api/crawls")).AsArray().Select(item => item!)];

    /// <summary>One per check that found something, by severity, as the issues screen's rows are.</summary>
    private static void AssertIssueCounts(string path, JsonNode issues)
    {
        var flagged = IssueReader.ReadCounts(path).Where(count => count.Value > 0)
            .GroupBy(count => Engine.Analysis.IssueCatalog.Find(count.Key)!.Severity)
            .ToDictionary(group => group.Key, group => (long)group.Count());
        Assert.Equal(flagged.GetValueOrDefault(Engine.Analysis.Models.IssueSeverity.Error), (long?)issues["error"]);
        Assert.Equal(flagged.GetValueOrDefault(Engine.Analysis.Models.IssueSeverity.Warning), (long?)issues["warning"]);
        Assert.Equal(flagged.GetValueOrDefault(Engine.Analysis.Models.IssueSeverity.Notice), (long?)issues["notice"]);
        Assert.True(flagged.Values.Sum() > 0);
    }
}
