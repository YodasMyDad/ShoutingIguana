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
    public static void SetStoredStatus(string path, CrawlStatus status) => Assert.Equal(1, Execute(path, $"UPDATE crawl SET status = {(int)status}"));

    /// <summary>What an engine from before the URL summary leaves: its analysis without the columns and tables added since.</summary>
    public static void AsAnalysedByAnEarlierEngine(string path) => Execute(path, """
        ALTER TABLE analysis DROP COLUMN is_partial;
        ALTER TABLE analysis DROP COLUMN crawl_updated_at;
        ALTER TABLE analysis DROP COLUMN analysis_version;
        ALTER TABLE analysis DROP COLUMN checks_fingerprint;
        DROP TABLE url_summary;
        DROP TABLE skipped_checks;
        """);

    /// <summary>What a crawl run by another program leaves: the crawl row stamped again.</summary>
    public static void StampAsCrawledAgain(string path) => Assert.Equal(1, Execute(path, "UPDATE crawl SET updated_at = updated_at + 1000"));

    /// <summary>
    /// A URL the crawl stored as refused by robots.txt, added to a crawl too small to have found one: the synthetic site's
    /// robots.txt disallows /private/, which only its larger sites link to.
    /// </summary>
    public static void AddRefusedByRobots(string path, string url) => Execute(path, $"""
        INSERT INTO urls (url, is_internal, fetch_mode, depth, state, skip_reason, source) VALUES ('{url}', 1, 0, 2, 2, 0, 1);
        INSERT INTO responses (url_id, fetched_at, outcome, ttfb_ms, response_ms, is_html, title_count, meta_description_count, h1_count, is_noindex, is_nofollow,
                               indexability, indexability_reason, outlink_count)
        VALUES (last_insert_rowid(), 0, 5, 0, 0, 0, 0, 0, 0, 0, 0, 1, 6, 0);
        UPDATE crawl SET updated_at = updated_at + 1000;
        """);

    private static int Execute(string path, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteNonQuery();
    }

    /// <summary>Holds a crawl file's lock as a crawl in another program would, until disposed.</summary>
    public static FileStream HoldLock(string path) =>
        new(path + "-lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 1,
            OperatingSystem.IsWindows() ? FileOptions.DeleteOnClose : FileOptions.None);
}
