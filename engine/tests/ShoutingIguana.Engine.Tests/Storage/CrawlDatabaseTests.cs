using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Tests.Support;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>Opening and closing crawl files: what is refused, and what is left behind.</summary>
public sealed class CrawlDatabaseTests : IDisposable
{
    // Nothing listens there, so nothing leaves the machine even if a crawl did start.
    private static readonly CrawlOptions NOWHERE = new() { StartUrl = "http://127.0.0.1:1/" };

    private readonly CrawlFiles _files = new();

    public void Dispose() => _files.Dispose();

    [Fact]
    public async Task AnalysingAMissingFileLeavesNothingBehind()
    {
        var path = _files.NewPath();

        await Assert.ThrowsAsync<FileNotFoundException>(() => new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path));

        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + "-lock"));
    }

    [Fact]
    public async Task RefusesAFileFromAnotherSchemaWithoutWritingToIt()
    {
        // A crawl file from step 1: schema 1, without columns this engine reads, such as sitemaps.is_probe.
        var path = _files.NewPath();
        using (var connection = Raw(path))
        {
            CrawlDatabase.Execute(connection, """
                CREATE TABLE crawl (id INTEGER PRIMARY KEY, start_url TEXT NOT NULL, options_json TEXT NOT NULL, status INTEGER NOT NULL);
                INSERT INTO crawl VALUES (1, 'http://127.0.0.1:1/', '{"StartUrl":"http://127.0.0.1:1/"}', 3);
                CREATE TABLE sitemaps (id INTEGER PRIMARY KEY, url TEXT NOT NULL);
                PRAGMA user_version = 1;
                """);
        }

        var analysis = await Assert.ThrowsAsync<InvalidOperationException>(() => new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path));
        Assert.Contains("schema 1", analysis.Message);
        Assert.Throws<InvalidOperationException>(() => CrawlStatsReader.Read(path));
        Assert.Throws<InvalidOperationException>(() => CrawlDatabase.ReadOptions(path));
        Assert.Throws<InvalidOperationException>(() => TestCrawls.Start(NOWHERE, path, resume: true));

        // Nothing touched the file: no analysis tables, still schema 1, still in rollback-journal mode.
        using var check = Raw(path);
        Assert.Equal(0L, CrawlDatabase.Scalar(check, "SELECT COUNT(*) FROM sqlite_master WHERE name IN ('analysis', 'issues', 'issue_types')"));
        Assert.Equal(1L, CrawlDatabase.Scalar(check, "PRAGMA user_version"));
        Assert.Equal("delete", CrawlDatabase.Scalar(check, "PRAGMA journal_mode"));
    }

    [Fact]
    public async Task RefusesAFileThatIsNotACrawl()
    {
        var path = _files.NewPath();
        using (var connection = Raw(path))
        {
            CrawlDatabase.Execute(connection, "CREATE TABLE notes (text TEXT); INSERT INTO notes VALUES ('not a crawl');");
        }

        var analysis = await Assert.ThrowsAsync<InvalidOperationException>(() => new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path));
        Assert.Contains("not a crawl database", analysis.Message);
        Assert.Throws<InvalidOperationException>(() => TestCrawls.Start(NOWHERE, path, resume: true));
        Assert.Throws<InvalidOperationException>(() => TestCrawls.Start(NOWHERE, path));

        using var check = Raw(path);
        Assert.Equal(1L, CrawlDatabase.Scalar(check, "SELECT COUNT(*) FROM sqlite_master"));
        Assert.Equal("delete", CrawlDatabase.Scalar(check, "PRAGMA journal_mode"));
    }

    [Fact]
    public async Task ClosingAWriterDoesNotWaitForReaders()
    {
        var path = _files.NewPath();
        var crawl = CrawlWriter.Open(path, NOWHERE, resume: false, new CrawlCounters(), NullLogger.Instance);

        // Another connection part-way through reading the file, as si stats, a SQLite browser or an export would be.
        using var reader = CrawlDatabase.OpenReader(path);
        using var query = reader.CreateCommand();
        query.CommandText = "SELECT start_url FROM crawl";
        using var rows = query.ExecuteReader();
        Assert.True(rows.Read());

        var stopwatch = Stopwatch.StartNew();
        await crawl.DisposeAsync();
        var crawlClose = stopwatch.Elapsed;

        using (var connection = CrawlDatabase.OpenWriter(path))
        {
            CrawlDatabase.Execute(connection, $"UPDATE crawl SET status = {(int)CrawlStatus.Completed}");
        }

        stopwatch.Restart();
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        var analysisClose = stopwatch.Elapsed;

        Assert.True(crawlClose < TimeSpan.FromSeconds(5), $"Closing the crawl took {crawlClose}");
        Assert.True(analysisClose < TimeSpan.FromSeconds(5), $"The analysis took {analysisClose}");
        Assert.Equal(NOWHERE.StartUrl, rows.GetString(0));
        Assert.Equal(1L, TestCrawls.Count(path, "SELECT COUNT(*) FROM analysis"));
    }

    [Theory]
    [InlineData("https://www.example.com/shop", "www.example.com-")]
    [InlineData("http://[::1]:8080/", "1-")]
    [InlineData("https://xn--bcher-kva.example/", "xn--bcher-kva.example-")]
    public void DefaultCrawlFileNamesAreValidOnEveryFileSystem(string startUrl, string prefix)
    {
        var name = CrawlDatabase.DefaultDatabasePath(startUrl);

        Assert.StartsWith(prefix, name);
        Assert.EndsWith(".sicrawl", name);
        Assert.DoesNotContain(name, character => character is ':' or '[' or ']' or '/' or '\\' || char.IsControl(character));
    }

    [Fact]
    public void DefaultCrawlFileNamesCarryTheHostAndTheLocalTime()
    {
        Assert.Equal("www.example.com-20261101-091505.sicrawl",
            CrawlDatabase.DefaultDatabasePath("https://www.example.com/shop", new DateTime(2026, 11, 1, 9, 15, 5, DateTimeKind.Local)));
        Assert.Equal("crawl-20261101-091505.sicrawl", CrawlDatabase.DefaultDatabasePath("not a url", new DateTime(2026, 11, 1, 9, 15, 5)));
    }

    /// <summary>
    /// A crawl or an analysis writing to the file holds its lock, and the probe sees both. It never takes the lock from
    /// a writer or keeps it, and never leaves a lock file where there was none.
    /// </summary>
    [Fact]
    public async Task TheLockProbeSeesEveryWriter()
    {
        var path = _files.NewPath();
        Assert.False(CrawlDatabase.IsInUse(path));
        Assert.False(File.Exists(path + "-lock"));

        var crawl = CrawlWriter.Open(path, NOWHERE, resume: false, new CrawlCounters(), NullLogger.Instance);
        Assert.True(CrawlDatabase.IsInUse(path));
        Assert.True(CrawlDatabase.IsInUse(path));
        Assert.Throws<CrawlFileInUseException>(() => CrawlFileLock.Acquire(path));
        await crawl.DisposeAsync();
        Assert.False(CrawlDatabase.IsInUse(path));

        // Stopped by a crash: the file says running, and nothing holds it.
        Assert.Equal(CrawlStatus.Running, CrawlSummaryReader.Read(path).Status);

        using (var connection = CrawlDatabase.OpenWriter(path))
        {
            CrawlDatabase.Execute(connection, $"UPDATE crawl SET status = {(int)CrawlStatus.Completed}");
        }

        using (IssueWriter.Open(path, NullLogger.Instance))
        {
            Assert.True(CrawlDatabase.IsInUse(path));
        }

        Assert.False(CrawlDatabase.IsInUse(path));
        CrawlFileLock.Acquire(path).Dispose();
    }

    /// <summary>
    /// A crawl file can be shared, so a crafted one with a view or a trigger (which the engine never creates) is refused
    /// by every reader before it reads anything: a <c>responses</c> view that never ends would otherwise hang a count.
    /// </summary>
    [Fact]
    public async Task ReadersRefuseAFileWithAViewOrATrigger()
    {
        var withView = _files.NewPath();
        await NewCrawlAsync(withView);
        using (var connection = CrawlDatabase.OpenWriter(withView))
        {
            CrawlDatabase.Execute(connection, """
                DROP TABLE responses;
                CREATE VIEW responses AS WITH RECURSIVE n(url_id) AS (SELECT 1 UNION ALL SELECT url_id + 1 FROM n) SELECT url_id FROM n;
                """);
        }

        // Every reader the shared file is reached through refuses it, and at once: the view is never counted.
        Assert.Contains("views or triggers", Assert.Throws<InvalidOperationException>(() => CrawlSummaryReader.Read(withView)).Message);
        Assert.Throws<InvalidOperationException>(() => CrawlStatsReader.Read(withView));
        Assert.Throws<InvalidOperationException>(() => CrawlDatabase.ReadOptions(withView));

        var withTrigger = _files.NewPath();
        await NewCrawlAsync(withTrigger);
        using (var connection = CrawlDatabase.OpenWriter(withTrigger))
        {
            CrawlDatabase.Execute(connection,
                "CREATE TRIGGER leak AFTER INSERT ON urls BEGIN SELECT 1; END;");
        }

        Assert.Contains("views or triggers", Assert.Throws<InvalidOperationException>(() => CrawlSummaryReader.Read(withTrigger)).Message);
    }

    /// <summary>
    /// A summary read can be given a time limit, so a file crafted to be slow to count can't stall a list: the limit's
    /// token stops it. A read whose token is already spent does nothing and says so at once.
    /// </summary>
    [Fact]
    public async Task ASummaryReadHonoursItsTimeLimit()
    {
        var path = _files.NewPath();
        await NewCrawlAsync(path);

        using var spent = new CancellationTokenSource();
        await spent.CancelAsync();
        Assert.Throws<OperationCanceledException>(() => CrawlSummaryReader.Read(path, spent.Token));

        // A token still live reads normally.
        Assert.Equal(CrawlStatus.Completed, CrawlSummaryReader.Read(path, CancellationToken.None).Status);
    }

    /// <summary>Deleting a crawl takes its write-ahead log and the rest with it, under the lock, so nothing can write to it meanwhile.</summary>
    [Fact]
    public async Task DeleteRemovesTheCrawlAndWhatSqliteKeepsBesideIt()
    {
        var path = _files.NewPath();
        await NewCrawlAsync(path);

        // A write-ahead log left beside the file: a new crawl of the same name would read it in, so the delete takes it.
        await File.WriteAllTextAsync(path + "-wal", "stale log");
        Assert.True(File.Exists(path));

        CrawlDatabase.Delete(path);

        foreach (var file in new[] { path, path + "-wal", path + "-shm", path + "-journal" })
        {
            Assert.False(File.Exists(file), file);
        }
    }

    /// <summary>A writer holds the file, so a delete is refused and nothing is removed.</summary>
    [Fact]
    public async Task DeleteIsRefusedWhileACrawlIsWritingAndKeepsEverything()
    {
        var path = _files.NewPath();
        await using (CrawlWriter.Open(path, NOWHERE with { MaxUrls = 5 }, resume: false, new CrawlCounters(), NullLogger.Instance))
        {
            Assert.Throws<CrawlFileInUseException>(() => CrawlDatabase.Delete(path));
            Assert.True(File.Exists(path));
        }
    }

    /// <summary>
    /// A lock file this user can't write (a read-only one on Windows, a root-owned one on Linux) isn't held by anyone who
    /// could be writing with us, so the probe reads it and says so, rather than throwing. The old probe asked for write
    /// access and threw <see cref="UnauthorizedAccessException"/> on such a file.
    /// </summary>
    [Fact]
    public void TheLockProbeReadsALockFileItCannotWrite()
    {
        var path = _files.NewPath();
        var lockFile = path + "-lock";
        File.WriteAllText(lockFile, string.Empty);
        var info = new FileInfo(lockFile) { IsReadOnly = true };
        try
        {
            Assert.False(CrawlDatabase.IsInUse(path));
        }
        finally
        {
            info.IsReadOnly = false;
        }
    }

    /// <summary>
    /// A probe takes the lock for an instant, so on Linux and macOS it can collide with this process acquiring the lock
    /// for a crawl. The acquire waits that instant out rather than refusing, and the in-process gate keeps the two apart.
    /// </summary>
    [Fact]
    public async Task ProbingDoesNotMakeThisProcessFailToAcquireTheLock()
    {
        var path = _files.NewPath();
        await NewCrawlAsync(path);

        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var probing = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                CrawlDatabase.IsInUse(path);
            }
        });

        try
        {
            // Every acquire succeeds despite the probe loop: none is mistaken for a live writer.
            for (var attempt = 0; attempt < 200; attempt++)
            {
                CrawlFileLock.Acquire(path).Dispose();
            }
        }
        finally
        {
            await stop.CancelAsync();
            await probing;
        }
    }

    /// <summary>A valid, empty crawl file, made without a network crawl: the writer creates the schema and a crawl row.</summary>
    private static async Task NewCrawlAsync(string path, CrawlStatus status = CrawlStatus.Completed)
    {
        await using (CrawlWriter.Open(path, NOWHERE, resume: false, new CrawlCounters(), NullLogger.Instance))
        {
        }

        using var connection = CrawlDatabase.OpenWriter(path);
        CrawlDatabase.Execute(connection, $"UPDATE crawl SET status = {(int)status}");
    }

    /// <summary>
    /// A lock held only for an instant (a probe, or a writer just letting go) doesn't refuse the next writer: the acquire
    /// keeps trying for a short while. The old acquire made a single attempt and refused at once.
    /// </summary>
    [Fact]
    public async Task AcquireWaitsOutABrieflyHeldLockRatherThanRefusing()
    {
        var path = _files.NewPath();
        await NewCrawlAsync(path);

        var held = CrawlFileLock.Acquire(path);
        var waiting = Task.Run(() => CrawlFileLock.Acquire(path));

        // Freed well within the acquire's patience: it should take the lock, not have refused.
        await Task.Delay(50);
        held.Dispose();

        using var taken = await waiting;
    }

    private static SqliteConnection Raw(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }
}
