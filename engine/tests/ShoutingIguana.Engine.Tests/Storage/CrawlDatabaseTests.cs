using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Configuration.Models;
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

    private static SqliteConnection Raw(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }
}
