using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.TestSite;

namespace ShoutingIguana.Sidecar.Tests.Support;

/// <summary>Crawl files made with the engine directly, as another program (the CLI) would leave them.</summary>
internal static class TestCrawlFiles
{
    public static async Task<string> CrawlAsync(SyntheticSiteServer site, string path, int maxUrls = 10, bool analyse = false,
        CrawlOptions? options = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        options ??= new CrawlOptions { StartUrl = site.BaseUri.AbsoluteUri, MaxUrls = maxUrls };
        await using (var job = new CrawlEngine(NullLoggerFactory.Instance).Start(new CrawlRequest(options, path)))
        {
            Assert.Equal(CrawlStatus.Completed, (await job.Completion.WaitAsync(EventStreamReader.TIMEOUT)).Status);
        }

        if (analyse)
        {
            await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        }

        return path;
    }

    /// <summary>What a crash leaves: the file says it is in this state, and nothing holds its lock.</summary>
    public static void SetStoredStatus(string path, CrawlStatus status)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE crawl SET status = {(int)status}";
        Assert.Equal(1, command.ExecuteNonQuery());
    }

    /// <summary>Holds a crawl file's lock as a crawl in another program would, until disposed.</summary>
    public static FileStream HoldLock(string path) =>
        new(path + "-lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 1,
            OperatingSystem.IsWindows() ? FileOptions.DeleteOnClose : FileOptions.None);
}
