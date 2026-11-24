using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage;

namespace ShoutingIguana.Engine.Tests.Support;

/// <summary>
/// Runs crawls for tests and reads what they stored.
/// <para>
/// A test's crawl of the synthetic site, or of any site that answers with a block, sets
/// <see cref="CrawlOptions.BrowserFallback"/> to false unless the fallback is what it tests. A crawl has it on by
/// default, and the synthetic site plants pages that answer plain HTTP with a challenge: what it expects of them
/// (internal-blocked) is what a crawl without a browser records. With it on, the first of them starts a real
/// browser, and the one a browser is let into has the rest of the site read in it. A site that never blocks leaves
/// the default alone, which is how most tests show that on changes nothing for it.
/// </para>
/// </summary>
internal static class TestCrawls
{
    public static readonly TimeSpan TIMEOUT = TimeSpan.FromMinutes(2);

    public static CrawlJob Start(CrawlOptions options, string path, bool resume = false) =>
        new CrawlEngine(NullLoggerFactory.Instance).Start(new CrawlRequest(options, path, resume));

    public static async Task<CrawlSummary> RunAsync(CrawlOptions options, string path, bool resume = false)
    {
        await using var job = Start(options, path, resume);
        return await job.Completion.WaitAsync(TIMEOUT);
    }

    public static long Count(string path, string sql)
    {
        using var connection = CrawlDatabase.OpenReader(path);
        return Convert.ToInt64(CrawlDatabase.Scalar(connection, sql), CultureInfo.InvariantCulture);
    }

    /// <summary>Every row of a query, each as one line, sorted: tables compare equal when their rows do.</summary>
    public static List<string> Rows(string path, string sql)
    {
        using var connection = CrawlDatabase.OpenReader(path);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        List<string> rows = [];
        while (reader.Read())
        {
            rows.Add(string.Join(" | ", Enumerable.Range(0, reader.FieldCount)
                .Select(column => reader.IsDBNull(column) ? "null" : Convert.ToString(reader.GetValue(column), CultureInfo.InvariantCulture))));
        }

        rows.Sort(StringComparer.Ordinal);
        return rows;
    }

    /// <summary>Polls a running crawl until a query returns a non-zero count.</summary>
    public static async Task WaitUntilAsync(string path, string sql)
    {
        var deadline = DateTime.UtcNow + TIMEOUT;
        while (Count(path, sql) == 0)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting for: {sql}");
            }

            await Task.Delay(10);
        }
    }
}
