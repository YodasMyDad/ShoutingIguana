using System.Text.Json;
using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Serialization;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// The notices a crawl raised, as its file keeps them: every run's, whoever ran it, oldest first. Safe while the crawl
/// runs, when the last second's may not be stored yet. A file from before notices were kept has none.
/// </summary>
public static class CrawlNoticeReader
{
    public const int DEFAULT_LIMIT = 1_000;

    /// <summary>
    /// The last <paramref name="limit"/> notices, oldest first. A <c>blocked</c> notice's count is its host's blocked
    /// responses as the file has them now, not the single one that raised it.
    /// </summary>
    public static IReadOnlyList<CrawlNotice> Read(string databasePath, int limit = DEFAULT_LIMIT)
    {
        using var connection = CrawlDatabase.OpenReader(databasePath);

        // One snapshot, so the counts are of the same moment as the notices.
        using var snapshot = connection.BeginTransaction(deferred: true);
        if (!IssueReader.HasTable(connection, snapshot, "notices"))
        {
            return [];
        }

        List<CrawlNotice> notices = [];
        using (var command = connection.CreateCommand())
        {
            command.Transaction = snapshot;
            command.CommandText = "SELECT id, at, level, code, message, data FROM (SELECT * FROM notices ORDER BY id DESC LIMIT $limit) ORDER BY id";
            command.Parameters.AddWithValue("$limit", limit);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                notices.Add(new CrawlNotice(LevelOf(reader.GetString(2)), reader.GetString(3), reader.GetString(4))
                {
                    Id = reader.GetInt64(0),
                    At = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)),
                    Data = reader.IsDBNull(5) ? null : DataOf(reader.GetString(5))
                });
            }
        }

        if (!notices.Any(IsBlockedHost))
        {
            return notices;
        }

        var blocked = BlockedByHost(connection, snapshot);
        return [.. notices.Select(notice => IsBlockedHost(notice) ? notice with { Data = notice.Data! with { Count = blocked.GetValueOrDefault(notice.Data.Host!) } } : notice)];
    }

    private static bool IsBlockedHost(CrawlNotice notice) => notice is { Code: "blocked", Data.Host: not null };

    /// <summary>The blocked responses per host: few, unless the site blocks everything, when they're what matters.</summary>
    private static Dictionary<string, long> BlockedByHost(SqliteConnection connection, SqliteTransaction snapshot)
    {
        Dictionary<string, long> hosts = new(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        command.Transaction = snapshot;
        command.CommandText = $"SELECT u.url FROM responses r JOIN urls u ON u.id = r.url_id WHERE r.outcome = {(int)FetchOutcome.Blocked}";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (Uri.TryCreate(reader.GetString(0), UriKind.Absolute, out var uri))
            {
                var host = UrlNormalizer.GetHost(uri);
                hosts[host] = hosts.GetValueOrDefault(host) + 1;
            }
        }

        return hosts;
    }

    /// <summary>As the writer stores it, in lower case. One this engine doesn't know reads as a warning: something to look at.</summary>
    private static CrawlNoticeLevel LevelOf(string level) => level switch
    {
        "info" => CrawlNoticeLevel.Info,
        "error" => CrawlNoticeLevel.Error,
        _ => CrawlNoticeLevel.Warning
    };

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
