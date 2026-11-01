using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// Reads the URL summary the last analysis stored (<c>url_summary</c>): one row per URL with what the grids sort and
/// filter on. A crawl that was never analysed, or was analysed by an engine from before the summary, has none: check
/// <see cref="IsAvailable"/> and offer to analyse it.
/// </summary>
public static class UrlSummaryReader
{
    /// <summary>Whether the last analysis stored the summary (<see cref="AnalysisInfo.HasUrlSummary"/>).</summary>
    public static bool IsAvailable(string databasePath)
    {
        using var connection = CrawlDatabase.OpenReader(databasePath);
        return IsAvailable(connection);
    }

    /// <summary>Every URL's row, in URL id order (crawl order), streamed.</summary>
    /// <exception cref="InvalidOperationException">The crawl has no summary (see <see cref="IsAvailable"/>).</exception>
    public static IEnumerable<UrlSummary> ReadAll(string databasePath)
    {
        using var connection = CrawlDatabase.OpenReader(databasePath);
        if (!IsAvailable(connection))
        {
            throw new InvalidOperationException("This crawl hasn't been analysed by this version of Shouting Iguana. Analyse it again to list its URLs.");
        }

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {string.Join(", ", UrlSummaryBuilder.COLUMNS)} FROM url_summary ORDER BY url_id";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            yield return Read(reader);
        }
    }

    /// <summary>The summary belongs to the last analysis only when that analysis stored it; an older engine's doesn't.</summary>
    internal static bool IsAvailable(SqliteConnection connection) =>
        IssueReader.ReadAnalysis(connection, transaction: null) is { HasUrlSummary: true } && IssueReader.HasTable(connection, transaction: null, "url_summary");

    /// <summary>A row selected with <see cref="UrlSummaryBuilder.COLUMNS"/>, in that order.</summary>
    private static UrlSummary Read(SqliteDataReader reader) => new()
    {
        UrlId = reader.GetInt32(0),
        IsInternal = reader.GetBoolean(1),
        Depth = reader.GetInt32(2),
        ResourceType = (ResourceType)reader.GetInt32(3),
        StatusCode = reader.IsDBNull(4) ? null : reader.GetInt32(4),
        IsIndexable = reader.IsDBNull(5) ? null : reader.GetBoolean(5),
        IndexabilityReason = reader.IsDBNull(6) ? null : (IndexabilityReason)reader.GetInt32(6),
        FinalUrlId = reader.GetInt32(7),
        FinalStatusCode = reader.IsDBNull(8) ? null : reader.GetInt32(8),
        InSitemap = reader.GetBoolean(9),
        Inlinks = reader.GetInt32(10),
        FollowedInlinks = reader.GetInt32(11),
        NofollowInlinks = reader.GetInt32(12),
        LinkingPages = reader.GetInt32(13),
        Outlinks = reader.IsDBNull(14) ? null : reader.GetInt32(14),
        Issues = reader.GetInt32(15),
        Errors = reader.GetInt32(16),
        Warnings = reader.GetInt32(17),
        Notices = reader.GetInt32(18),
        SizeBytes = reader.IsDBNull(19) ? null : reader.GetInt64(19),
        ResponseMs = reader.IsDBNull(20) ? null : reader.GetInt32(20),
        WordCount = reader.IsDBNull(21) ? null : reader.GetInt32(21)
    };
}
