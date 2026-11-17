namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>When a crawl was crawling, as its file keeps it: every run of it, and the time they took between them.</summary>
/// <param name="TimeCrawling">
/// The time spent crawling over every run: each from its start to its end, less the time it was paused. Neither a pause
/// nor the time between a stop and a continue is in it, nor the time a recheck spent (its run is listed, marked as one).
/// </param>
/// <param name="Runs">
/// Each run, oldest first. Null when there are more stretches of crawling than <see cref="CrawlRunReader.MAX_STRETCHES"/>
/// (a crawl paused that many times), which aren't listed.
/// </param>
public sealed record CrawlRuns(TimeSpan TimeCrawling, IReadOnlyList<CrawlRun>? Runs);
