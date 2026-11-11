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

    /// <summary>
    /// The part of <see cref="Outcomes"/> and <see cref="StatusCodes"/> that is the crawler's own probes of the site's
    /// other versions, which nothing on the site leads to.
    /// </summary>
    public SiteVersionStats SiteVersions { get; init; } = SiteVersionStats.None;

    /// <summary>
    /// URLs failed without a request, because their host had stopped answering ("Not checked"), on this site or another.
    /// How they would have answered isn't known, so they aren't among <see cref="NonIndexableReasons"/>.
    /// </summary>
    public long NotCheckedCount { get; init; }

    /// <summary>
    /// Internal URLs that cannot be indexed, by reason, as the analysis judges them: the crawler's probes of the site's
    /// other versions, which nothing on the site leads to, and URLs that weren't checked aren't among them.
    /// </summary>
    public IReadOnlyDictionary<IndexabilityReason, long> NonIndexableReasons { get; init; } = new Dictionary<IndexabilityReason, long>();
}
