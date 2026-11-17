using ShoutingIguana.Engine.Analysis;
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
    /// The time the crawl has spent crawling, over every run of it, less the time paused: neither a pause nor the time
    /// between a stop and a continue is in it. Null when it isn't known (<see cref="CrawlRunReader"/>): an engine from
    /// before the time was kept made the crawl, or a run of it.
    /// </summary>
    public TimeSpan? TimeCrawling { get; init; }

    /// <summary>
    /// The crawl kept the site's icon (<see cref="FaviconReader"/>). A running crawl gains it a few requests after its
    /// start page; a crawl made before icons were kept has none.
    /// </summary>
    public bool HasFavicon { get; init; }

    /// <summary>
    /// URLs with a stored outcome, whatever it was, as <see cref="Crawling.Models.CrawlProgress.Fetched"/> counts them:
    /// those the crawl requested, those robots.txt kept it from requesting, which are stored as disallowed, and those it
    /// never asked because their host had stopped answering, which <see cref="CrawledUrlCount"/> leaves out. URLs it only
    /// found (a nofollow link, past a limit) aren't among them. Null when read without counts.
    /// </summary>
    public long? FetchedUrlCount { get; init; }

    /// <summary>
    /// The URLs crawled, as <see cref="Crawling.Models.CrawlProgress.Crawled"/> counts them and the URL grid has them
    /// (fetched): <see cref="FetchedUrlCount"/> less the URLs that weren't checked, which have an outcome though nothing
    /// was asked, their host having stopped answering. Null when read without counts.
    /// </summary>
    public long? CrawledUrlCount { get; init; }

    /// <summary>
    /// Internal HTML pages fetched and parsed, as <see cref="CrawlStats.HtmlPageCount"/> and a crawl's live progress count
    /// them: a URL that was only status-checked isn't one. Null when read without counts.
    /// </summary>
    public long? PageCount { get; init; }

    /// <summary>When the last analysis ran, or null when the crawl has never been analysed.</summary>
    public DateTimeOffset? AnalyzedAt { get; init; }

    /// <summary>
    /// The last analysis, with what it was run against, or null when the crawl has never been analysed. Whether it's out
    /// of date also depends on the thresholds set now: <see cref="OutOfDate"/>.
    /// </summary>
    public AnalysisInfo? Analysis { get; init; }

    /// <summary>
    /// Checks that flagged at least one URL in the last analysis, by severity: the rows of an issues screen, not the
    /// flagged URLs. Null when the crawl has never been analysed.
    /// </summary>
    public IReadOnlyDictionary<IssueSeverity, long>? FlaggedChecks { get; init; }

    /// <summary>
    /// The URLs each check flagged in the last analysis, by check id, as <see cref="AnalysisResults.Counts"/> has them: 0
    /// for a check that ran and found nothing, and a check it skipped is missing. Null when the crawl has never been
    /// analysed. With <see cref="OptionsFingerprint"/>, what a comparison with another crawl needs at the check level
    /// without opening either file (<see cref="CrawlComparability.FromSummaries"/>).
    /// </summary>
    public IReadOnlyDictionary<string, long>? CheckCounts { get; init; }

    /// <summary>
    /// <see cref="CrawlComparability.OptionsFingerprint"/> of the options the crawl was started with, as a comparison reads
    /// them: two crawls with the same fingerprint differ in none of the options that make a comparison partial.
    /// </summary>
    public string? OptionsFingerprint { get; init; }

    /// <summary>Why the last analysis no longer stands, if it doesn't; nothing when it does, or there's none.</summary>
    /// <param name="settings">The thresholds an analysis would run with now.</param>
    public IReadOnlyList<OutOfDateReason> OutOfDate(AnalysisSettings settings) =>
        Analysis is null ? [] : AnalysisFreshness.Check(Analysis, UpdatedAt, settings);
}
