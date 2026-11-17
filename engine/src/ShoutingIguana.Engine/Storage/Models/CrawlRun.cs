namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One run of a crawl: from its start, or its being continued, to its being stopped or its end.</summary>
/// <param name="StartedAt">When it began crawling.</param>
/// <param name="EndedAt">
/// When it ended. Null for a run that hasn't: the one going now, or one cut short when its process died, whose last
/// stretch ends where the engine last stored it.
/// </param>
/// <param name="TimeCrawling">The time it spent crawling: its stretches, added up.</param>
/// <param name="Crawling">The stretches it was crawling in, oldest first: one for a run never paused, and a pause is the gap between two.</param>
public sealed record CrawlRun(DateTimeOffset StartedAt, DateTimeOffset? EndedAt, TimeSpan TimeCrawling, IReadOnlyList<CrawlingStretch> Crawling)
{
    /// <summary>
    /// A recheck's run (the fix loop), which fetched some of the crawl's URLs again: its time is its own, and no part of the
    /// crawl's <see cref="CrawlRuns.TimeCrawling"/>.
    /// </summary>
    public bool IsRecheck { get; init; }
}
