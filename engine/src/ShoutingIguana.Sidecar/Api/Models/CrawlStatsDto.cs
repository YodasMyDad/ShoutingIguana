using System.Globalization;
using System.Text.Json;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary><see cref="CrawlStats"/> as JSON: the grouped counts are keyed by camelCase names, and status codes by number.</summary>
internal sealed record CrawlStatsDto
{
    public required string StartUrl { get; init; }

    /// <summary>As stored: running is also what an interrupted crawl leaves behind.</summary>
    public required CrawlStatus Status { get; init; }

    public required DateTime StartedAt { get; init; }
    public DateTime? FinishedAt { get; init; }
    public required string EngineVersion { get; init; }
    public long UrlCount { get; init; }
    public long InternalUrlCount { get; init; }
    public long QueuedCount { get; init; }
    public long LinkCount { get; init; }
    public long SitemapCount { get; init; }
    public long SitemapErrorCount { get; init; }
    public long SitemapUrlCount { get; init; }
    public long UnlinkedPageCount { get; init; }
    public long HtmlPageCount { get; init; }
    public long IndexablePageCount { get; init; }
    public double AverageResponseMs { get; init; }
    public long TotalBytes { get; init; }
    public required IReadOnlyDictionary<string, long> Outcomes { get; init; }
    public required IReadOnlyDictionary<string, long> StatusCodes { get; init; }
    public required IReadOnlyDictionary<string, long> SkipReasons { get; init; }
    public required IReadOnlyDictionary<string, long> NonIndexableReasons { get; init; }

    public static CrawlStatsDto From(CrawlStats stats) => new()
    {
        StartUrl = stats.StartUrl,
        Status = stats.Status,
        StartedAt = stats.StartedAt.UtcDateTime,
        FinishedAt = stats.FinishedAt?.UtcDateTime,
        EngineVersion = stats.EngineVersion,
        UrlCount = stats.UrlCount,
        InternalUrlCount = stats.InternalUrlCount,
        QueuedCount = stats.QueuedCount,
        LinkCount = stats.LinkCount,
        SitemapCount = stats.SitemapCount,
        SitemapErrorCount = stats.SitemapErrorCount,
        SitemapUrlCount = stats.SitemapUrlCount,
        UnlinkedPageCount = stats.UnlinkedPageCount,
        HtmlPageCount = stats.HtmlPageCount,
        IndexablePageCount = stats.IndexablePageCount,
        AverageResponseMs = stats.AverageResponseMs,
        TotalBytes = stats.TotalBytes,
        Outcomes = ByName(stats.Outcomes),
        StatusCodes = stats.StatusCodes.ToDictionary(group => group.Key.ToString(CultureInfo.InvariantCulture), group => group.Value),
        SkipReasons = ByName(stats.SkipReasons),
        NonIndexableReasons = ByName(stats.NonIndexableReasons)
    };

    private static Dictionary<string, long> ByName<TEnum>(IReadOnlyDictionary<TEnum, long> groups) where TEnum : struct, Enum =>
        groups.ToDictionary(group => JsonNamingPolicy.CamelCase.ConvertName(group.Key.ToString()), group => group.Value);
}
