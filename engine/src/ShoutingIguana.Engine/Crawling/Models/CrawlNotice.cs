namespace ShoutingIguana.Engine.Crawling.Models;

/// <summary>Something the person running the crawl should know about, e.g. the site is blocking the crawler.</summary>
/// <param name="Code">Stable identifier such as <c>blocked</c> or <c>robots-unreachable</c>, for UIs to key on.</param>
public sealed record CrawlNotice(CrawlNoticeLevel Level, string Code, string Message);
