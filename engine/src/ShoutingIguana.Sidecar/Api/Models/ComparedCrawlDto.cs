namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>The crawl a comparison is with (<c>/changes</c>'s <c>with</c>), as the library has it.</summary>
/// <param name="Chosen"><c>previous</c>, the crawl's previous crawl, or <c>picked</c>, named by the request.</param>
internal sealed record ComparedCrawlDto(
    string Id,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    CrawlListStatus Status,
    string? StartUrl,
    DateTime? AnalyzedAt,
    long? PagesAnalyzed,
    long CrawledUrlCount,
    string Chosen);
