namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>What a score was worked out from, to say beside it: "covers all 265 pages and 82 of 86 checks".</summary>
/// <param name="Pages">The pages the analysis read (<see cref="AnalysisSummary.PagesAnalyzed"/>): what a page check's count is a share of.</param>
/// <param name="Urls">The URLs crawled when the analysis ran (<see cref="AnalysisSummary.CrawledUrlCount"/>): what a URL check's count is a share of.</param>
/// <param name="ChecksRun">The checks that ran, whatever they found.</param>
/// <param name="ChecksSkipped">The checks the analysis couldn't run. They take nothing off, and add nothing.</param>
/// <param name="Complete">The crawl read all it set out to: it finished, and no limit kept internal URLs out.</param>
/// <param name="Reason">Why not, when it isn't complete.</param>
/// <param name="BlockedByRobots">The site's URLs robots.txt kept the crawler out of, which weren't read and so aren't covered.</param>
public sealed record ScoreCoverage(long Pages, long Urls, int ChecksRun, int ChecksSkipped, bool Complete, ScoreIncompleteReason? Reason, long BlockedByRobots);
