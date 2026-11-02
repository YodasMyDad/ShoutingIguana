using System.Text.Json;
using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Serialization;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// The notices a crawl raised, as its file keeps them: every run's, whoever ran it, oldest first. Safe while the crawl
/// runs, when the last second's may not be stored yet. A file from before notices were kept has none. Files are shared,
/// so a notice another program wrote oddly is read as well as it can be, never refused: one without a code or a message
/// is left out, a missing level reads as a warning and a missing time as the crawl's start, and a table of another shape
/// reads as no notices at all.
/// </summary>
public static class CrawlNoticeReader
{
    public const int DEFAULT_LIMIT = 1_000;

    /// <summary>The times <see cref="DateTimeOffset"/> can hold, in milliseconds since 1970: years 1 to 9999.</summary>
    private const long MIN_UNIX_MILLISECONDS = -62_135_596_800_000;
    private const long MAX_UNIX_MILLISECONDS = 253_402_300_799_999;

    /// <summary>
    /// The last <paramref name="limit"/> notices, oldest first. A <c>blocked</c> notice's count is its host's blocked
    /// responses as the file has them now, every run's, not the single one that raised it.
    /// </summary>
    public static IReadOnlyList<CrawlNotice> Read(string databasePath, int limit = DEFAULT_LIMIT)
    {
        using var connection = CrawlDatabase.OpenReader(databasePath);

        // One snapshot, so the counts are of the same moment as the notices.
        using var snapshot = connection.BeginTransaction(deferred: true);
        if (!HasNoticesTable(connection, snapshot))
        {
            return [];
        }

        var (startUrl, startedAt) = ReadCrawl(connection, snapshot);
        List<CrawlNotice> notices = [];
        using (var command = connection.CreateCommand())
        {
            command.Transaction = snapshot;
            command.CommandText = "SELECT id, at, level, code, message, data FROM (SELECT * FROM notices ORDER BY id DESC LIMIT $limit) ORDER BY id";
            command.Parameters.AddWithValue("$limit", limit);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (reader.IsDBNull(0) || reader.IsDBNull(3) || reader.IsDBNull(4))
                {
                    continue;
                }

                notices.Add(new CrawlNotice(LevelOf(reader.IsDBNull(2) ? null : reader.GetString(2)), reader.GetString(3), reader.GetString(4))
                {
                    Id = reader.GetInt64(0),
                    At = reader.IsDBNull(1) ? startedAt : TimeOf(reader.GetInt64(1), startedAt),
                    Data = reader.IsDBNull(5) ? null : DataOf(reader.GetString(5))
                });
            }
        }

        if (!notices.Any(IsBlockedHost))
        {
            return notices;
        }

        var blocked = BlockedByHost(connection, snapshot, startUrl);
        return [.. notices.Select(notice => IsBlockedHost(notice) ? notice with { Data = notice.Data! with { Count = blocked.GetValueOrDefault(notice.Data.Host!) } } : notice)];
    }

    /// <summary>The table, with every column this engine writes: one another program made its own way has none of its notices read.</summary>
    private static bool HasNoticesTable(SqliteConnection connection, SqliteTransaction snapshot)
    {
        if (!IssueReader.HasTable(connection, snapshot, "notices"))
        {
            return false;
        }

        HashSet<string> columns = new(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.Transaction = snapshot;
        command.CommandText = "SELECT name FROM pragma_table_info('notices')";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            columns.Add(reader.GetString(0));
        }

        return CrawlSchema.NOTICE_COLUMNS.All(columns.Contains);
    }

    /// <summary>The start URL, which names the site's hosts, and when the crawl started, the time a notice without one is given.</summary>
    private static (string? StartUrl, DateTimeOffset StartedAt) ReadCrawl(SqliteConnection connection, SqliteTransaction snapshot)
    {
        using var command = connection.CreateCommand();
        command.Transaction = snapshot;
        command.CommandText = "SELECT start_url, started_at FROM crawl WHERE id = 1";
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? (reader.IsDBNull(0) ? null : reader.GetString(0), reader.IsDBNull(1) ? DateTimeOffset.UnixEpoch : TimeOf(reader.GetInt64(1), DateTimeOffset.UnixEpoch))
            : (null, DateTimeOffset.UnixEpoch);
    }

    private static bool IsBlockedHost(CrawlNotice notice) => notice is { Code: "blocked", Data.Host: not null };

    /// <summary>
    /// The site's blocked responses per host, named as the crawl names its hosts (www and the bare domain are one), as the
    /// notices do: few, unless the site blocks everything, when they're what matters.
    /// </summary>
    private static Dictionary<string, long> BlockedByHost(SqliteConnection connection, SqliteTransaction snapshot, string? startUrl)
    {
        var scope = Uri.TryCreate(startUrl, UriKind.Absolute, out var start) && start.Scheme is "http" or "https"
            ? new CrawlScope(start, includeSubdomains: false, stayInStartFolder: false)
            : null;
        Dictionary<string, long> hosts = new(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        command.Transaction = snapshot;
        command.CommandText = $"SELECT u.url FROM responses r JOIN urls u ON u.id = r.url_id WHERE r.outcome = {(int)FetchOutcome.Blocked} AND u.is_internal = 1";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (Uri.TryCreate(reader.GetString(0), UriKind.Absolute, out var uri))
            {
                var host = UrlNormalizer.GetHost(uri);
                var name = scope?.HostName(host) ?? host;
                hosts[name] = hosts.GetValueOrDefault(name) + 1;
            }
        }

        return hosts;
    }

    /// <summary>As the writer stores it, in lower case. One this engine doesn't know, or none, reads as a warning: something to look at.</summary>
    private static CrawlNoticeLevel LevelOf(string? level) => level switch
    {
        "info" => CrawlNoticeLevel.Info,
        "error" => CrawlNoticeLevel.Error,
        _ => CrawlNoticeLevel.Warning
    };

    /// <summary>Milliseconds since 1970, as the writer stores times; one out of any calendar's range reads as <paramref name="fallback"/>.</summary>
    private static DateTimeOffset TimeOf(long milliseconds, DateTimeOffset fallback) =>
        milliseconds is >= MIN_UNIX_MILLISECONDS and <= MAX_UNIX_MILLISECONDS ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds) : fallback;

    /// <summary>The notice's facts. Files are shared, so data that isn't what this engine writes reads as none, not as an error.</summary>
    private static CrawlNoticeData? DataOf(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, EngineJsonContext.Default.CrawlNoticeData);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
