using ShoutingIguana.Engine.Crawling.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>A crawl notice, as the alert event carries it and the crawl file keeps it.</summary>
/// <param name="Id">The crawl's own number for it, from 1, across every run: the same live and read back, so the UI can tell them apart from new ones.</param>
/// <param name="Code">Stable, for the UI to key its text and actions on.</param>
/// <param name="Message">The engine's text, which may include text from the site.</param>
/// <param name="Data">What it's about, for the UI to offer its action; null for a notice without facts.</param>
internal sealed record AlertDto(string CrawlId, long Id, CrawlNoticeLevel Level, string Code, string Message, DateTime At, AlertDataDto? Data)
{
    public static AlertDto From(string crawlId, CrawlNotice notice) =>
        new(crawlId, notice.Id, notice.Level, notice.Code, notice.Message, notice.At.UtcDateTime, notice.Data is { } data ? AlertDataDto.From(data) : null);
}
