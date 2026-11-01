using System.Globalization;
using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// The few numbers a list of crawls shows, cheap enough to read for every crawl in it: the crawl row, two counts and
/// the last analysis. It never touches the links, which <see cref="CrawlStatsReader.Read"/> counts. Safe to run while
/// the crawl is still writing.
/// </summary>
public static class CrawlSummaryReader
{
    /// <exception cref="InvalidOperationException">The file isn't a crawl, or was written with another schema.</exception>
    public static CrawlFileSummary Read(string databasePath)
    {
        using var connection = CrawlDatabase.OpenReader(databasePath);
        var summary = ReadCrawlRow(connection);
        var analyzedAt = AnalyzedAt(connection);
        return summary with
        {
            FetchedUrlCount = Count(connection, "SELECT COUNT(*) FROM responses"),
            PageCount = Count(connection, $"""
                SELECT COUNT(*) FROM responses r JOIN urls u ON u.id = r.url_id
                WHERE u.is_internal = 1 AND r.is_html = 1 AND r.outcome = {(int)FetchOutcome.Success} AND r.word_count IS NOT NULL
                """),
            AnalyzedAt = analyzedAt,
            FlaggedChecks = analyzedAt is null ? null : FlaggedChecks(connection)
        };
    }

    private static CrawlFileSummary ReadCrawlRow(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT start_url, status, started_at, updated_at, finished_at, engine_version FROM crawl WHERE id = 1";
        using var crawl = command.ExecuteReader();
        return crawl.Read()
            ? new CrawlFileSummary
            {
                StartUrl = crawl.GetString(0),
                Status = (CrawlStatus)crawl.GetInt32(1),
                StartedAt = DateTimeOffset.FromUnixTimeMilliseconds(crawl.GetInt64(2)),
                UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(crawl.GetInt64(3)),
                FinishedAt = crawl.IsDBNull(4) ? null : DateTimeOffset.FromUnixTimeMilliseconds(crawl.GetInt64(4)),
                EngineVersion = crawl.GetString(5)
            }
            : throw new InvalidOperationException("This file is not a crawl database.");
    }

    /// <summary>A first analysis that was cancelled or failed leaves its tables behind without a row.</summary>
    private static DateTimeOffset? AnalyzedAt(SqliteConnection connection) =>
        CrawlDatabase.Scalar(connection, "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'analysis'") is not null &&
        CrawlDatabase.Scalar(connection, "SELECT analyzed_at FROM analysis WHERE id = 1") is long analyzedAt
            ? DateTimeOffset.FromUnixTimeMilliseconds(analyzedAt)
            : null;

    private static Dictionary<IssueSeverity, long> FlaggedChecks(SqliteConnection connection)
    {
        Dictionary<IssueSeverity, long> checks = [];
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT severity, COUNT(*) FROM issue_types WHERE url_count > 0 GROUP BY severity";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            // Stored as the severity's name. One this engine doesn't know (a newer engine's) is left out.
            if (Enum.TryParse<IssueSeverity>(reader.GetString(0), ignoreCase: false, out var severity) && Enum.IsDefined(severity))
            {
                checks[severity] = reader.GetInt64(1);
            }
        }

        return checks;
    }

    private static long Count(SqliteConnection connection, string sql) =>
        Convert.ToInt64(CrawlDatabase.Scalar(connection, sql) ?? 0L, CultureInfo.InvariantCulture);
}
