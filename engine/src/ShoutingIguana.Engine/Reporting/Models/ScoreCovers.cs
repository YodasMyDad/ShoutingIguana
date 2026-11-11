namespace ShoutingIguana.Engine.Reporting.Models;

/// <summary>What a score is built from: the pages read, the addresses checked, the checks that ran, and whether the crawl finished.</summary>
/// <param name="Complete">The crawl read everything it found; a score of a crawl that didn't is partial.</param>
/// <param name="BlockedByRobots">Addresses robots.txt kept the crawl out of, which weren't read and aren't covered.</param>
internal sealed record ScoreCovers(long Pages, long Urls, int ChecksRun, int ChecksSkipped, bool Complete, long BlockedByRobots);
