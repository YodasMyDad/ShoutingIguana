using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Reporting.Models;

/// <summary>
/// Everything the report says of one crawl, and the workbook's summary sheet with it: read once
/// (<see cref="ReportReader"/>), so the two documents can't differ.
/// </summary>
internal sealed record ReportModel
{
    /// <summary>The site, as its owner names it: the start URL's host without a leading www.</summary>
    public required string Site { get; init; }

    public required string StartUrl { get; init; }

    /// <summary>The origin the site's pages are on (where the start URL's redirects end), which a page's address is shown without.</summary>
    public required string Origin { get; init; }

    public required CrawlStatus Status { get; init; }

    /// <summary>The crawl is running or was interrupted: its file says running.</summary>
    public required bool StillRunning { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }

    /// <summary>The time spent crawling, pauses apart; null when it isn't known.</summary>
    public TimeSpan? TimeCrawling { get; init; }

    public required DateTimeOffset AnalyzedAt { get; init; }

    /// <summary>The engine that worked out the results.</summary>
    public required string AnalysisEngineVersion { get; init; }

    /// <summary>Why the results no longer stand, when they don't and the report was told how to judge; empty otherwise.</summary>
    public IReadOnlyList<OutOfDateReason> OutOfDate { get; init; } = [];

    /// <summary>The pages read.</summary>
    public required long Pages { get; init; }

    /// <summary>The addresses checked: every URL with an answer, or refused one.</summary>
    public required long Urls { get; init; }

    /// <summary>The pages that can appear in search.</summary>
    public required long IndexablePages { get; init; }

    /// <summary>Addresses found and not yet read: what a crawl stopped early left.</summary>
    public required long Queued { get; init; }

    /// <summary>Addresses found and not read, by why.</summary>
    public IReadOnlyDictionary<SkipReason, long> NotRead { get; init; } = new Dictionary<SkipReason, long>();

    /// <summary>Addresses robots.txt kept the crawl out of.</summary>
    public long BlockedByRobots { get; init; }

    public required CrawlOptions Options { get; init; }

    /// <summary>Every check the engine has.</summary>
    public required int ChecksInCatalog { get; init; }

    /// <summary>The checks the analysis ran.</summary>
    public required int ChecksRun { get; init; }

    /// <summary>The checks that found something, in "Fix first" order, numbered from 1.</summary>
    public required IReadOnlyList<ReportProblem> Problems { get; init; }

    /// <summary>The checks that couldn't run, in catalog order.</summary>
    public required IReadOnlyList<ReportSkipped> Skipped { get; init; }

    /// <summary>The checks that ran and found nothing, in catalog order.</summary>
    public required IReadOnlyList<ReportPassed> Passed { get; init; }

    /// <summary>The score, or null when there is none (<see cref="NoScore"/> says why).</summary>
    public ReportScore? Score { get; init; }

    public NoScoreReason? NoScore { get; init; }

    /// <summary>When the document is made, and the time zone its dates are written in.</summary>
    public required DateTimeOffset MadeAt { get; init; }

    public required TimeZoneInfo TimeZone { get; init; }

    /// <summary>The engine writing the document.</summary>
    public required string EngineVersion { get; init; }

    /// <summary>How many of the checks that found something are of a severity.</summary>
    public int ChecksOf(IssueSeverity severity) => Problems.Count(problem => problem.Check.Severity == severity);

    /// <summary>The crawl read everything it found.</summary>
    public bool Finished => Status == CrawlStatus.Completed && NotRead.GetValueOrDefault(SkipReason.UrlLimit) == 0;
}
