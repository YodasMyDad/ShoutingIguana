using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>One crawl in the library.</summary>
internal sealed record CrawlListItem
{
    public required string Id { get; init; }
    public required string Path { get; init; }
    public required string FileName { get; init; }

    /// <summary>The start URL's host without a leading www., which the UI groups crawls by.</summary>
    public string? Site { get; init; }

    /// <summary>
    /// The site as the library groups it (the fix loop): <see cref="Site"/>, then <c>:port</c> when the start URL has a port
    /// of its own. The app groups by it, and the previous crawl is chosen by it. Null for a file with no start URL.
    /// </summary>
    public string? SiteKey { get; init; }

    public string? StartUrl { get; init; }
    public required CrawlListStatus Status { get; init; }

    /// <summary>Why the file couldn't be read, in words for the UI: given only when the status is unreadable.</summary>
    public string? Error { get; init; }

    public DateTime? StartedAt { get; init; }
    public DateTime? FinishedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }

    /// <summary>
    /// The time spent crawling over every run, pauses and the time between runs left out; live, the crawl's progress. Null
    /// when it isn't known: a run of the crawl was made by an engine that didn't keep it.
    /// </summary>
    public long? CrawlingMs { get; init; }

    /// <summary>
    /// URLs with a stored outcome, as <c>si stats</c> and a crawl's progress count them; live, the crawl's progress. Those
    /// that weren't checked are among them, which <see cref="CrawledUrlCount"/> leaves out.
    /// </summary>
    public long FetchedUrlCount { get; init; }

    /// <summary>The URLs crawled, as the URL grid counts them (fetched); live, the crawl's progress.</summary>
    public long CrawledUrlCount { get; init; }

    /// <summary>
    /// Internal HTML pages fetched and parsed, as the crawl counts them (live, its progress), Cloudflare's own
    /// <c>/cdn-cgi/</c> endpoints left out as the analysis leaves them out: the pages the last analysis audited
    /// (<see cref="PagesAnalyzed"/>) while the crawl hasn't changed since.
    /// </summary>
    public long PageCount { get; init; }

    /// <summary>
    /// The pages the last analysis audited, which its score covers: the analysis's own <c>pagesAnalyzed</c>, as it stood
    /// when it ran. The count to put beside the score. Null before the first analysis.
    /// </summary>
    public long? PagesAnalyzed { get; init; }

    /// <summary>Null before the first analysis.</summary>
    public IssueCountsDto? Issues { get; init; }

    public DateTime? AnalyzedAt { get; init; }

    /// <summary>
    /// The score of the last analysis: the number, its word, and whether it is of a crawl that didn't read all of the
    /// site. Null when there's none: never analysed, no page read, or analysed by an engine from before the score.
    /// </summary>
    public SiteScoreSummary? Score { get; init; }

    /// <summary>
    /// The last analysis no longer stands: the crawl changed after it, this engine would judge it differently, or its
    /// settings differ. False before the first analysis.
    /// </summary>
    public bool OutOfDate { get; init; }

    public long SizeBytes { get; init; }

    /// <summary>
    /// The crawl kept the site's icon, which <c>GET /api/crawls/{id}/favicon</c> gives (E6). A running crawl gains it a few
    /// requests after its start page; false for a crawl whose site has none, and for one made before icons were kept.
    /// </summary>
    public bool HasFavicon { get; init; }

    /// <summary>A file opened from elsewhere that isn't there any more.</summary>
    public bool Missing { get; init; }

    /// <summary>
    /// The crawl this one is compared with by default (the fix loop): the newest earlier crawl of the same site that can be,
    /// one with the same start URL first. Null for a site's first crawl, and for a crawl never analysed.
    /// </summary>
    public PreviousCrawlDto? Previous { get; init; }

    /// <summary>
    /// What changed since <see cref="Previous"/>, check by check, from the two crawls' cached summaries (the fix loop). Null
    /// without a previous crawl, and while this crawl is live, interrupted or can't be read: its analysis then doesn't
    /// describe it.
    /// </summary>
    public CrawlChangesDto? Changes { get; init; }

    /// <summary>The crawl's rechecks (the fix loop): how many, and the latest in brief. Null for a crawl never rechecked.</summary>
    public RechecksBriefDto? Rechecks { get; init; }
}
