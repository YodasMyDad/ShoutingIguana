namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary><c>/changes</c> for a crawl with no previous crawl (a site's first): nothing to compare, which the UI shows as nothing.</summary>
/// <param name="With">Always null.</param>
internal sealed record NoComparisonDto(string CrawlId, ComparedCrawlDto? With);
