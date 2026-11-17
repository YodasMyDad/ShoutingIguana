using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Sidecar.Library;
using ShoutingIguana.Sidecar.Tests.Support;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Sidecar.Tests;

/// <summary>The library: the data folder's crawls, files opened from elsewhere, removal, restore and deletion.</summary>
public sealed class LibraryTests : IAsyncLifetime
{
    private readonly string _elsewhere = Path.Combine(Path.GetTempPath(), $"si-elsewhere-{Guid.NewGuid():N}");
    private readonly string _root = TestSidecar.NewRoot();
    private SyntheticSiteServer _site = null!;
    private TestSidecar _sidecar = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_elsewhere);
        _site = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 60 });
        _sidecar = await TestSidecar.StartAsync(_root);
    }

    public async Task DisposeAsync()
    {
        await _sidecar.DisposeAsync();
        await _site.DisposeAsync();
        TestSidecar.DeleteRoot(_root);
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
        Assert.Equal(stats.Outcomes.Values.Sum(), (long?)a["fetchedUrlCount"]);
        Assert.Equal(stats.Outcomes.Values.Sum() - stats.NotCheckedCount, (long?)a["crawledUrlCount"]);
        Assert.Equal(stats.HtmlPageCount, (long?)a["pageCount"]);

        Assert.Equal(IssueReader.ReadAnalysis(inData)!.PagesAnalyzed, (long?)a["pagesAnalyzed"]);
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
        Assert.Null(opened.Body["pagesAnalyzed"]);
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

        // The crawl and what SQLite keeps beside it go. The lock file stays on Linux and macOS, where deleting it would
        // revive the two-writers race; on Windows it is held delete-on-close, so it goes with the delete.
        Assert.All(new[] { path, path + "-wal", path + "-shm" }, file => Assert.False(File.Exists(file), file));
        if (OperatingSystem.IsWindows())
        {
            Assert.False(File.Exists(path + "-lock"));
        }

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

    /// <summary>A path to another computer is refused before anything opens it: opening one would authenticate to it.</summary>
    [Fact]
    public async Task NetworkAndDevicePathsAreRefused()
    {
        TestSidecar.AssertProblem(await Open(@"\\server\share\crawl.sicrawl"), HttpStatusCode.BadRequest, "file-error");
        TestSidecar.AssertProblem(await Open(@"\\?\UNC\server\share\crawl.sicrawl"), HttpStatusCode.BadRequest, "file-error");
        TestSidecar.AssertProblem(await Open(@"\\.\pipe\crawl.sicrawl"), HttpStatusCode.BadRequest, "file-error");
        var refused = TestSidecar.AssertProblem(await Open(@"\\nas\backups\crawl.sicrawl"), HttpStatusCode.BadRequest, "file-error");
        Assert.Contains("another computer", (string?)refused["title"]);
        Assert.Empty(await ListAsync());
    }

    /// <summary>
    /// A momentary read error (another program's lock) isn't remembered until the file changes: the crawl is listed as
    /// unreadable, not dropped and not hung, and shows up again once the lock is gone, without the file having changed.
    /// Windows only: there a shared handle blocks SQLite; .NET's FileShare maps to a lock SQLite ignores on Unix.
    /// </summary>
    [Fact]
    public async Task ATransientReadErrorIsNotCachedUntilTheFileChanges()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var path = await TestCrawlFiles.CrawlAsync(_site, Path.Combine(_sidecar.DataDirectory, "locked.sicrawl"));
        var id = CrawlIds.For(path);

        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var item = (await ListAsync()).Single();
            Assert.Equal(id, (string?)item["id"]);
            Assert.Equal("unreadable", (string?)item["status"]);
            Assert.False(string.IsNullOrEmpty((string?)item["error"]));
        }

        // Once the lock is gone it reads normally, without the file having had to change.
        Assert.Equal("completed", (string?)(await _sidecar.GetJsonAsync($"api/crawls/{id}"))["status"]);
    }

    /// <summary>The summary cache is kept in the state folder, keyed by the file's stamp, so a new sidecar doesn't re-read every crawl.</summary>
    [Fact]
    public async Task SummariesAreKeptInTheStateFolderAcrossRestarts()
    {
        var path = await TestCrawlFiles.CrawlAsync(_site, Path.Combine(_sidecar.DataDirectory, "kept.sicrawl"), analyse: true);
        var id = CrawlIds.For(path);
        var listed = await _sidecar.GetJsonAsync($"api/crawls/{id}");
        Assert.Equal("completed", (string?)listed["status"]);
        Assert.NotNull(listed["score"]);
        await _sidecar.DisposeAsync();

        // Kept on disk, under this engine's version and this crawl's id, the analysis's score with it.
        var cache = await File.ReadAllTextAsync(Path.Combine(_root, "state", "summaries.json"));
        Assert.Contains(id, cache);
        Assert.Contains(Engine.EngineInfo.Version, cache);
        var kept = JsonNode.Parse(cache)!["summaries"]!.AsArray().Single()!["summary"]!["analysis"]!;
        Assert.Equal(listed["score"]!.ToJsonString(), kept["score"]!.ToJsonString());
        Assert.Equal((Engine.Analysis.ScoreCalculator.VERSION, (long)listed["crawledUrlCount"]!), ((int)kept["scoreVersion"]!, (long)kept["crawledUrlCount"]!));

        // A fresh sidecar on the same folder shows the crawl at once; the stamp is unchanged, so the summary is reused.
        _sidecar = await TestSidecar.StartAsync(_root);
        var item = await _sidecar.GetJsonAsync($"api/crawls/{id}");
        Assert.Equal("completed", (string?)item["status"]);
        Assert.NotNull(item["analyzedAt"]);
        Assert.Equal(listed["score"]!.ToJsonString(), item["score"]!.ToJsonString());
        Assert.False((bool?)item["outOfDate"]);
    }

    /// <summary>
    /// Summaries kept by a build from before the site's icon was (the format before this one) aren't used: one would say
    /// a crawl that has its icon has none. The crawl is read again.
    /// </summary>
    [Fact]
    public async Task SummariesKeptBeforeTheIconAreReadAgain()
    {
        var path = await TestCrawlFiles.CrawlAsync(_site, Path.Combine(_sidecar.DataDirectory, "before-the-icon.sicrawl"));
        var id = CrawlIds.For(path);
        Assert.True((bool)(await _sidecar.GetJsonAsync($"api/crawls/{id}"))["hasFavicon"]!);
        await _sidecar.DisposeAsync();

        // As the build before it left the file: the format before, and nothing said of an icon.
        var file = Path.Combine(_root, "state", "summaries.json");
        var cache = JsonNode.Parse(await File.ReadAllTextAsync(file))!.AsObject();
        Assert.Equal(7, (int)cache["format"]!);
        cache["format"] = 4;
        Assert.All(cache["summaries"]!.AsArray(), summary => Assert.True(summary!["summary"]!.AsObject().Remove("hasFavicon")));
        await File.WriteAllTextAsync(file, cache.ToJsonString());

        _sidecar = await TestSidecar.StartAsync(_root);
        Assert.True((bool)(await _sidecar.GetJsonAsync($"api/crawls/{id}"))["hasFavicon"]!);
    }

    /// <summary>
    /// Summaries kept by a build from before the score (two formats before this one) aren't used: one would say a scored
    /// crawl has no score, and that its analysis is out of date. The crawl is read again.
    /// </summary>
    [Fact]
    public async Task SummariesKeptBeforeTheScoreAreReadAgain()
    {
        var path = await TestCrawlFiles.CrawlAsync(_site, Path.Combine(_sidecar.DataDirectory, "before-the-score.sicrawl"), analyse: true);
        var id = CrawlIds.For(path);
        var score = (await _sidecar.GetJsonAsync($"api/crawls/{id}"))["score"]!.ToJsonString();
        await _sidecar.DisposeAsync();

        // As the build before it left the file: the format it kept, and no score with the analysis.
        var file = Path.Combine(_root, "state", "summaries.json");
        var cache = JsonNode.Parse(await File.ReadAllTextAsync(file))!.AsObject();
        Assert.Equal(7, (int)cache["format"]!);
        cache["format"] = 3;
        foreach (var summary in cache["summaries"]!.AsArray())
        {
            var analysis = summary!["summary"]!["analysis"]!.AsObject();
            Assert.True(analysis.Remove("score") && analysis.Remove("scoreVersion") && analysis.Remove("crawledUrlCount"));
        }

        await File.WriteAllTextAsync(file, cache.ToJsonString());

        _sidecar = await TestSidecar.StartAsync(_root);
        var item = await _sidecar.GetJsonAsync($"api/crawls/{id}");
        Assert.Equal((score, false), (item["score"]!.ToJsonString(), (bool)item["outOfDate"]!));
    }

    /// <summary>
    /// Summaries kept by a build from before the fix loop (the format before this one) aren't used: one has no check counts
    /// and no options fingerprint, so it would say a crawl has no previous crawl to compare with. The crawls are read again.
    /// </summary>
    [Fact]
    public async Task SummariesKeptBeforeTheFixLoopAreReadAgain()
    {
        var then = CrawlIds.For(await TestCrawlFiles.CrawlAsync(_site, Path.Combine(_sidecar.DataDirectory, "before-the-fix-loop-1.sicrawl"), analyse: true));
        var now = CrawlIds.For(await TestCrawlFiles.CrawlAsync(_site, Path.Combine(_sidecar.DataDirectory, "before-the-fix-loop-2.sicrawl"), analyse: true));
        Assert.Equal(then, (string?)(await _sidecar.GetJsonAsync($"api/crawls/{now}"))["previous"]!["id"]);
        await _sidecar.DisposeAsync();

        // As the build before it left the file: the format it kept, and neither the counts nor the fingerprint.
        var file = Path.Combine(_root, "state", "summaries.json");
        var cache = JsonNode.Parse(await File.ReadAllTextAsync(file))!.AsObject();
        Assert.Equal(7, (int)cache["format"]!);
        cache["format"] = 5;
        foreach (var summary in cache["summaries"]!.AsArray())
        {
            Assert.True(summary!["summary"]!.AsObject().Remove("checkCounts") && summary["summary"]!.AsObject().Remove("optionsFingerprint"));
        }

        await File.WriteAllTextAsync(file, cache.ToJsonString());

        _sidecar = await TestSidecar.StartAsync(_root);
        var item = await _sidecar.GetJsonAsync($"api/crawls/{now}");
        Assert.Equal(then, (string?)item["previous"]!["id"]);
        Assert.NotNull(item["changes"]);
    }

    /// <summary>
    /// Summaries kept by a build from before rechecks were kept with them (the format before this one) aren't used: one
    /// would say a rechecked crawl never was. The crawl is read again.
    /// </summary>
    [Fact]
    public async Task SummariesKeptBeforeTheRechecksAreReadAgain()
    {
        var path = await TestCrawlFiles.CrawlAsync(_site, Path.Combine(_sidecar.DataDirectory, "before-the-rechecks.sicrawl"), analyse: true);
        var plan = RecheckPlanReader.Plan(path, new RecheckAsk(null, UrlIds: [UrlGridReader.Lookup(path, "/")!.Id]), new AnalysisSettings());
        await using (var job = new CrawlEngine(NullLoggerFactory.Instance).Recheck(new RecheckRequest(path, plan, CrawlDatabase.ReadOptions(path))))
        {
            await job.Completion.WaitAsync(EventStreamReader.TIMEOUT);
        }

        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        var id = CrawlIds.For(path);
        var rechecks = (await _sidecar.GetJsonAsync($"api/crawls/{id}"))["rechecks"]!.ToJsonString();
        Assert.Contains("\"count\":1", rechecks, StringComparison.Ordinal);
        await _sidecar.DisposeAsync();

        // As the build before it left the file: the format it kept, and nothing said of rechecks.
        var file = Path.Combine(_root, "state", "summaries.json");
        var cache = JsonNode.Parse(await File.ReadAllTextAsync(file))!.AsObject();
        Assert.Equal(7, (int)cache["format"]!);
        cache["format"] = 6;
        foreach (var summary in cache["summaries"]!.AsArray())
        {
            Assert.True(summary!["summary"]!.AsObject().Remove("recheckCount") && summary["summary"]!.AsObject().Remove("lastRecheck"));
        }

        await File.WriteAllTextAsync(file, cache.ToJsonString());

        _sidecar = await TestSidecar.StartAsync(_root);
        Assert.Equal(rechecks, (await _sidecar.GetJsonAsync($"api/crawls/{id}"))["rechecks"]!.ToJsonString());
    }

    /// <summary>
    /// Summaries kept by a build that stored less of each one (the same engine version, before the analysis was kept with
    /// them) aren't used: such a summary would say an earlier engine's analysis still stands. The crawl is read again.
    /// </summary>
    [Fact]
    public async Task SummariesKeptInAnEarlierFormatAreReadAgain()
    {
        var path = await TestCrawlFiles.CrawlAsync(_site, Path.Combine(_sidecar.DataDirectory, "earlier-format.sicrawl"), analyse: true);
        TestCrawlFiles.AsAnalysedByAnEarlierEngine(path);
        var id = CrawlIds.For(path);
        Assert.True((bool?)(await _sidecar.GetJsonAsync($"api/crawls/{id}"))["outOfDate"]);
        await _sidecar.DisposeAsync();

        // As the build before it left the file: no format, and no analysis in the summaries.
        var file = Path.Combine(_root, "state", "summaries.json");
        var cache = JsonNode.Parse(await File.ReadAllTextAsync(file))!.AsObject();
        Assert.NotNull(cache["format"]);
        cache.Remove("format");
        foreach (var summary in cache["summaries"]!.AsArray())
        {
            Assert.NotNull(summary!["summary"]!["analysis"]);
            summary["summary"]!.AsObject().Remove("analysis");
        }

        await File.WriteAllTextAsync(file, cache.ToJsonString());

        _sidecar = await TestSidecar.StartAsync(_root);
        Assert.True((bool?)(await _sidecar.GetJsonAsync($"api/crawls/{id}"))["outOfDate"]);
    }

    /// <summary>
    /// A library change takes effect only once it is saved: a save that fails leaves the library as it was, so a retry
    /// really retries rather than returning success for a change that was never written.
    /// </summary>
    [Fact]
    public async Task AChangeThatCantBeSavedIsNotKept()
    {
        var path = await TestCrawlFiles.CrawlAsync(_site, Path.Combine(_elsewhere, "unsavable.sicrawl"));

        // Make the library file unwritable by putting a directory where it belongs: the move over it fails.
        var libraryFile = Path.Combine(_root, "state", "library.json");
        if (File.Exists(libraryFile))
        {
            File.Delete(libraryFile);
        }

        Directory.CreateDirectory(libraryFile);
        try
        {
            // Opening from elsewhere must save the library; the save fails, so the open fails and nothing is listed.
            TestSidecar.AssertProblem(await Open(path), HttpStatusCode.BadRequest, "file-error");
            Assert.Empty(await ListAsync());
        }
        finally
        {
            Directory.Delete(libraryFile);
        }

        // With the obstruction gone, opening the same crawl works and it is listed: the earlier failure left no trace.
        Assert.Equal(HttpStatusCode.OK, (await Open(path)).Status);
        Assert.Single(await ListAsync());
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
