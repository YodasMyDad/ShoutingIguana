namespace ShoutingIguana.Engine.Crawling.Models;

/// <param name="Error">Why the crawl failed, when <paramref name="Status"/> is Failed.</param>
public sealed record CrawlSummary(CrawlStatus Status, CrawlProgress Progress, string DatabasePath, string? Error);
