using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Crawling.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>What a list of crawls shows for one crawl file, from <see cref="CrawlSummaryReader"/>.</summary>
public sealed record CrawlFileSummary
{
    public required string StartUrl { get; init; }

    /// <summary>
    /// As stored. Paused is never stored, and Running is also what a crawl that was interrupted leaves behind:
    /// <see cref="CrawlDatabase.IsInUse"/> tells them apart.
    /// </summary>
    public required CrawlStatus Status { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>When the crawl's status last changed: it started, was resumed, or ended.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }

    public required string EngineVersion { get; init; }

    /// <summary>
    /// URLs the crawl requested, whatever the answer, as <see cref="Crawling.Models.CrawlProgress.Fetched"/> counts them.
    /// URLs it only found (a nofollow link, past a limit) aren't among them.
    /// </summary>
    public long FetchedUrlCount { get; init; }

    /// <summary>Internal HTML pages fetched successfully, as <see cref="CrawlStats.HtmlPageCount"/> counts them.</summary>
    public long PageCount { get; init; }

    /// <summary>When the last analysis ran, or null when the crawl has never been analysed.</summary>
    public DateTimeOffset? AnalyzedAt { get; init; }

    /// <summary>
    /// Checks that flagged at least one URL in the last analysis, by severity: the rows of an issues screen, not the
    /// flagged URLs. Null when the crawl has never been analysed.
    /// </summary>
    public IReadOnlyDictionary<IssueSeverity, long>? FlaggedChecks { get; init; }
}
