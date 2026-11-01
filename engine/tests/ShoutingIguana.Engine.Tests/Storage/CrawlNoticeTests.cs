using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Tests.Crawling;
using ShoutingIguana.Engine.Tests.Fetching;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>
/// A crawl's notices are kept in its file, under the ids the host heard them with, every run's, so any program that opens
/// the crawl can show them. A file from before they were kept reads as having none, and gets them when it is continued.
/// </summary>
public sealed class CrawlNoticeTests : IAsyncLifetime
{
    private readonly CrawlFiles _files = new();
    private SyntheticSiteServer _site = null!;

    public async Task InitializeAsync() => _site = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 60 });

    public async Task DisposeAsync()
    {
        await _site.DisposeAsync();
        _files.Dispose();
    }

    /// <summary>The synthetic site plants a firewall block, and lists its pages in sitemaps: a warning and a notice to keep.</summary>
    [Fact]
    public async Task NoticesAreKeptWithTheCrawlAsTheyWereRaised()
    {
        var path = _files.NewPath();
        var observer = new RecordingObserver();
        await using (var job = new CrawlEngine(NullLoggerFactory.Instance).Start(new CrawlRequest(Options(), path), observer))
        {
            Assert.Equal(CrawlStatus.Completed, (await job.Completion.WaitAsync(TestCrawls.TIMEOUT)).Status);
        }

        // Numbered from 1 in the order raised, with when, and heard live with the id and time the file keeps.
        var stored = CrawlNoticeReader.Read(path);
        var heard = observer.Notices.OrderBy(notice => notice.Id).ToList();
        Assert.Equal(Enumerable.Range(1, heard.Count).Select(id => (long)id), heard.Select(notice => notice.Id));
        Assert.Equal(heard.Select(notice => (notice.Id, notice.Level, notice.Code, notice.Message, notice.At.ToUnixTimeMilliseconds())),
            stored.Select(notice => (notice.Id, notice.Level, notice.Code, notice.Message, notice.At.ToUnixTimeMilliseconds())));
        Assert.All(stored, notice => Assert.NotNull(notice.Data));

        // What a UI offers its action with: which host, why, and how many responses it blocked as the file has them now.
        var blocked = Assert.Single(stored, notice => notice.Code == "blocked");
        var live = Assert.Single(heard, notice => notice.Code == "blocked");
        Assert.Equal("127.0.0.1", blocked.Data!.Host);
        Assert.False(string.IsNullOrEmpty(blocked.Data.Reason));
        Assert.Equal(1, live.Data!.Count);
        Assert.Equal(TestCrawls.Count(path, $"SELECT COUNT(*) FROM responses WHERE outcome = {(int)FetchOutcome.Blocked}"), blocked.Data.Count);
        Assert.Equal(live.Data with { Count = blocked.Data.Count }, blocked.Data);

        // The numbers its message gives, for the UI to use without reading them out of the text.
        var sitemaps = Assert.Single(stored, notice => notice.Code == "sitemaps");
        Assert.Equal(CrawlNoticeLevel.Info, sitemaps.Level);
        Assert.True(sitemaps.Data!.UrlCount > 0);
        Assert.Equal($"Read {sitemaps.Data.Count} sitemap(s) listing {sitemaps.Data.UrlCount:N0} URLs.", sitemaps.Message);
    }

    /// <summary>
    /// A firewall blocks every page but the home page: one notice says so, as the first block is met, and read back from
    /// the file it counts every response the host blocked.
    /// </summary>
    [Fact]
    public async Task ABlockedNoticeReadBackCountsEveryResponseTheHostBlocked()
    {
        await using var server = await LoopbackServer.StartAsync(context =>
        {
            switch (context.Request.Path.Value)
            {
                case "/robots.txt":
                    context.Response.StatusCode = 404;
                    return Task.CompletedTask;
                case "/":
                    context.Response.ContentType = "text/html";
                    return context.Response.WriteAsync(
                        "<!doctype html><html><head><title>Home</title></head><body><a href=\"/a\">A</a> <a href=\"/b\">B</a> <a href=\"/c\">C</a></body></html>");
                default:
                    context.Response.StatusCode = 403;
                    context.Response.Headers["cf-mitigated"] = "challenge";
                    return Task.CompletedTask;
            }
        });
        var path = _files.NewPath();
        var observer = new RecordingObserver();
        await using (var job = new CrawlEngine(NullLoggerFactory.Instance)
                         .Start(new CrawlRequest(new CrawlOptions { StartUrl = server.BaseUri.AbsoluteUri, MaxRetries = 0 }, path), observer))
        {
            Assert.Equal(CrawlStatus.Completed, (await job.Completion.WaitAsync(TestCrawls.TIMEOUT)).Status);
        }

        var live = Assert.Single(observer.Notices, notice => notice.Code == "blocked");
        var stored = Assert.Single(CrawlNoticeReader.Read(path), notice => notice.Code == "blocked");
        Assert.Equal(new CrawlNoticeData(Host: "127.0.0.1", Reason: "Cloudflare challenge", Count: 1), live.Data);
        Assert.Equal(live.Data! with { Count = 3 }, stored.Data);
        Assert.Equal(3, TestCrawls.Count(path, $"SELECT COUNT(*) FROM responses WHERE outcome = {(int)FetchOutcome.Blocked}"));
    }

    /// <summary>Another program reading the crawl while it runs (a UI that opens it, si stats) finds a notice within moments.</summary>
    [Fact]
    public async Task ANoticeIsStoredWhileTheCrawlRuns()
    {
        var path = _files.NewPath();
        var observer = new RecordingObserver();
        await using var job = new CrawlEngine(NullLoggerFactory.Instance)
            .Start(new CrawlRequest(Options() with { MaxRequestsPerSecond = 4, Concurrency = 1 }, path), observer);

        await TestCrawls.WaitUntilAsync(path, "SELECT COUNT(*) FROM responses");
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (observer.Notices.Count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        var heard = Assert.Single(observer.Notices.Take(1));
        while (CrawlNoticeReader.Read(path).All(notice => notice.Id != heard.Id) && DateTime.UtcNow < deadline + TimeSpan.FromSeconds(5))
        {
            await Task.Delay(10);
        }

        Assert.False(job.Completion.IsCompleted);
        Assert.Contains(CrawlNoticeReader.Read(path), notice => notice.Id == heard.Id && notice.Code == heard.Code);
        await job.StopAsync();
    }

    [Fact]
    public async Task TheIdsCarryOnAcrossRunsAndTheLastOnesAreStoredAsTheFileCloses()
    {
        var path = _files.NewPath();
        await using (var writer = CrawlWriter.Open(path, Options(), resume: false, new CrawlCounters(), NullLogger.Instance))
        {
            Assert.Equal(1, writer.RecordNotice(Notice("first")).Id);
            Assert.Equal(2, writer.RecordNotice(Notice("second")).Id);
        }

        await using (var writer = CrawlWriter.Open(path, Options(), resume: true, new CrawlCounters(), NullLogger.Instance))
        {
            Assert.Equal(3, writer.RecordNotice(Notice("third") with { Data = new CrawlNoticeData(Origin: "https://example.com", Duration: TimeSpan.FromMinutes(2)) }).Id);
        }

        var stored = CrawlNoticeReader.Read(path);
        Assert.Equal(["first", "second", "third"], stored.Select(notice => notice.Code));
        Assert.Equal([1L, 2L, 3L], stored.Select(notice => notice.Id));
        Assert.Equal(new CrawlNoticeData(Origin: "https://example.com", Duration: TimeSpan.FromMinutes(2)), stored[2].Data);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 10, 15, 0, TimeSpan.Zero), stored[0].At);

        // The last ones, oldest first.
        Assert.Equal(["second", "third"], CrawlNoticeReader.Read(path, limit: 2).Select(notice => notice.Code));
    }

    /// <summary>What an engine from before notices were kept leaves: no table. Read as none; continuing the crawl adds it.</summary>
    [Fact]
    public async Task AFileFromBeforeNoticesWereKeptHasNoneUntilItIsContinued()
    {
        var path = _files.NewPath();
        await using (var writer = CrawlWriter.Open(path, Options(), resume: false, new CrawlCounters(), NullLogger.Instance))
        {
        }

        Execute(path, "DROP TABLE notices");
        Assert.Empty(CrawlNoticeReader.Read(path));

        // Continued: the crawl reaches the firewall block and reads the sitemaps, and keeps what it says, numbered from 1.
        var resumed = await TestCrawls.RunAsync(Options(), path, resume: true);
        Assert.Equal(CrawlStatus.Completed, resumed.Status);
        var stored = CrawlNoticeReader.Read(path);
        Assert.Contains(stored, notice => notice.Code == "blocked");
        Assert.Equal(Enumerable.Range(1, stored.Count).Select(id => (long)id), stored.Select(notice => notice.Id));
    }

    /// <summary>Crawl files are shared, so a notice another program wrote oddly is read as well as it can be, never refused.</summary>
    [Fact]
    public async Task ANoticeWrittenOddlyIsReadAsWellAsItCanBe()
    {
        var path = _files.NewPath();
        await using (var writer = CrawlWriter.Open(path, Options(), resume: false, new CrawlCounters(), NullLogger.Instance))
        {
            writer.RecordNotice(Notice("ok") with { Level = CrawlNoticeLevel.Error });
        }

        Execute(path, """
            INSERT INTO notices (id, at, level, code, message, data) VALUES (2, 0, 'shout', 'odd', 'An odd one', 'not json');
            INSERT INTO notices (id, at, level, code, message, data) VALUES (3, 0, 'info', 'blocked', 'No host', '{"count":"lots"}');
            """);

        var stored = CrawlNoticeReader.Read(path);
        Assert.Equal(CrawlNoticeLevel.Error, stored[0].Level);
        Assert.Equal((CrawlNoticeLevel.Warning, (CrawlNoticeData?)null), (stored[1].Level, stored[1].Data));
        Assert.Equal((CrawlNoticeLevel.Info, (CrawlNoticeData?)null), (stored[2].Level, stored[2].Data));
    }

    private CrawlOptions Options() => new() { StartUrl = _site.BaseUri.AbsoluteUri };

    private static CrawlNotice Notice(string code) =>
        new(CrawlNoticeLevel.Warning, code, $"The {code} notice.") { At = new DateTimeOffset(2026, 11, 1, 10, 15, 0, TimeSpan.Zero) };

    private static void Execute(string path, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
