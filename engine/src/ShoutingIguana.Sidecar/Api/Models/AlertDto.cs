using ShoutingIguana.Engine.Crawling.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>A crawl notice, as the alert event carries it.</summary>
/// <param name="Code">Stable, for the UI to key its text and actions on.</param>
/// <param name="Message">The engine's text, which may include text from the site.</param>
internal sealed record AlertDto(string CrawlId, CrawlNoticeLevel Level, string Code, string Message, DateTime At);
