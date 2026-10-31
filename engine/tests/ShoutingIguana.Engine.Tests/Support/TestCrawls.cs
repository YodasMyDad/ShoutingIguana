using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage;

namespace ShoutingIguana.Engine.Tests.Support;

/// <summary>Runs crawls for tests and reads what they stored.</summary>
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
