using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>Headline numbers for a stored crawl.</summary>
public sealed record CrawlStats
{
    public required string StartUrl { get; init; }
    public required CrawlStatus Status { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
    public required string EngineVersion { get; init; }
    public long UrlCount { get; init; }
    public long InternalUrlCount { get; init; }
    public long QueuedCount { get; init; }
    public long LinkCount { get; init; }
    /// <summary>Sitemap files read successfully.</summary>
    public long SitemapCount { get; init; }

    /// <summary>
    /// Sitemap files that were declared or listed but couldn't be read. A failed guess at /sitemap.xml isn't one, and nor
    /// is a sitemap left unread because it was past the crawler's limits.
    /// </summary>
    public long SitemapErrorCount { get; init; }

    public long SitemapUrlCount { get; init; }

    /// <summary>Internal HTML pages found only through sitemaps, with no link to them from any other URL: candidates for orphan pages.</summary>
    public long UnlinkedPageCount { get; init; }

    /// <summary>Internal HTML pages fetched successfully.</summary>
    public long HtmlPageCount { get; init; }
    public long IndexablePageCount { get; init; }
    public double AverageResponseMs { get; init; }
    public long TotalBytes { get; init; }
    public IReadOnlyDictionary<FetchOutcome, long> Outcomes { get; init; } = new Dictionary<FetchOutcome, long>();
    public IReadOnlyDictionary<int, long> StatusCodes { get; init; } = new Dictionary<int, long>();
    public IReadOnlyDictionary<SkipReason, long> SkipReasons { get; init; } = new Dictionary<SkipReason, long>();

    /// <summary>Internal URLs that cannot be indexed, by reason.</summary>
    public IReadOnlyDictionary<IndexabilityReason, long> NonIndexableReasons { get; init; } = new Dictionary<IndexabilityReason, long>();
}
