using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// Reads the URL summary the last analysis stored (<c>url_summary</c>): one row per URL with what the grids sort and
/// filter on. Check <see cref="IsAvailable(string)"/> first: a crawl that was never analysed has none, nor has one analysed
/// by another version of the engine, and a crawl that changed after its analysis has one no longer worth reading.
/// </summary>
public static class UrlSummaryReader
{
    /// <summary>
    /// Whether there is a summary to read: the last analysis stored one this engine reads
    /// (<see cref="AnalysisInfo.HasUrlSummary"/>), and the crawl hasn't changed since (it was continued, or is running).
    /// A summary of an earlier state of the crawl would show the URLs found since as unknown, or not at all, so until the
    /// crawl is analysed again the grids show what the crawl alone knows. An analysis that's out of date only because the
    /// engine's checks or the thresholds changed keeps its summary, which still describes the crawl.
    /// </summary>
    public static bool IsAvailable(string databasePath)
    {
        using var connection = CrawlDatabase.OpenReader(databasePath);
        using var snapshot = connection.BeginTransaction(deferred: true);
        return IsAvailable(connection, snapshot);
    }

    /// <summary>
    /// Every URL's row, in URL id order (crawl order), streamed, from one snapshot: the rows are those of the analysis
    /// found available, even if another analysis finishes or the crawl is continued meanwhile.
    /// </summary>
    /// <exception cref="InvalidOperationException">There is no summary to read (see <see cref="IsAvailable(string)"/>).</exception>
    public static IEnumerable<UrlSummary> ReadAll(string databasePath)
    {
        using var connection = CrawlDatabase.OpenReader(databasePath);
        using var snapshot = connection.BeginTransaction(deferred: true);
        if (!IsAvailable(connection, snapshot))
        {
            throw new InvalidOperationException(
                "This crawl has no URL summary to read: it changed after its last analysis, or that analysis was made by another version of Shouting Iguana. Analyse it again to list its URLs.");
        }

        using var command = connection.CreateCommand();
        command.Transaction = snapshot;
        command.CommandText = $"SELECT {string.Join(", ", UrlSummaryBuilder.COLUMNS)} FROM url_summary ORDER BY url_id";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            yield return Read(reader);
        }
    }

    /// <summary>
    /// The summary belongs to the last analysis only when that analysis stored it (an older engine's doesn't), and
    /// describes the crawl only while the crawl's stamp is still the one the analysis kept.
    /// </summary>
    internal static bool IsAvailable(SqliteConnection connection, SqliteTransaction? transaction) =>
        IssueReader.ReadAnalysis(connection, transaction) is { HasUrlSummary: true, CrawlUpdatedAt: { } analysed } &&
        IssueReader.HasTable(connection, transaction, "url_summary") &&
        IssueReader.ReadCrawlUpdatedAt(connection, transaction) == analysed;

    /// <summary>A row selected with <see cref="UrlSummaryBuilder.COLUMNS"/>, in that order.</summary>
    private static UrlSummary Read(SqliteDataReader reader) => new()
    {
        UrlId = reader.GetInt32(0),
        IsInternal = reader.GetBoolean(1),
        Source = (DiscoverySource)reader.GetInt32(2),
        Depth = reader.IsDBNull(3) ? null : reader.GetInt32(3),
        ResourceType = (ResourceType)reader.GetInt32(4),
        StatusCode = reader.IsDBNull(5) ? null : reader.GetInt32(5),
        IsIndexable = reader.IsDBNull(6) ? null : reader.GetBoolean(6),
        IndexabilityReason = reader.IsDBNull(7) ? null : (IndexabilityReason)reader.GetInt32(7),
        FinalUrlId = reader.GetInt32(8),
        FinalStatusCode = reader.IsDBNull(9) ? null : reader.GetInt32(9),
        InSitemap = reader.IsDBNull(10) ? null : reader.GetBoolean(10),
        Inlinks = reader.GetInt32(11),
        FollowedInlinks = reader.GetInt32(12),
        NofollowInlinks = reader.GetInt32(13),
        LinkingPages = reader.GetInt32(14),
        Outlinks = reader.IsDBNull(15) ? null : reader.GetInt32(15),
        Issues = reader.GetInt32(16),
        Errors = reader.GetInt32(17),
        Warnings = reader.GetInt32(18),
        Notices = reader.GetInt32(19),
        SizeBytes = reader.IsDBNull(20) ? null : reader.GetInt64(20),
        ResponseMs = reader.IsDBNull(21) ? null : reader.GetInt32(21),
        WordCount = reader.IsDBNull(22) ? null : reader.GetInt32(22),
        TitleLength = reader.IsDBNull(23) ? null : reader.GetInt32(23),
        DescriptionLength = reader.IsDBNull(24) ? null : reader.GetInt32(24),
        UsedOn = reader.GetInt32(25),
        TitleWidth = reader.IsDBNull(26) ? null : reader.GetInt32(26),
        DescriptionWidth = reader.IsDBNull(27) ? null : reader.GetInt32(27)
    };
}
