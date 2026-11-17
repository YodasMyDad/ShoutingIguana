namespace ShoutingIguana.Engine.Crawling.Models;

/// <param name="Error">Why the crawl failed, when <paramref name="Status"/> is Failed.</param>
public sealed record CrawlSummary(CrawlStatus Status, CrawlProgress Progress, string DatabasePath, string? Error)
{
    /// <summary>For a recheck's run, its number in the crawl file; null for a crawl's. Its status is the run's, never the crawl's.</summary>
    public int? RecheckId { get; init; }
}
