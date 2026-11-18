using ShoutingIguana.Engine.Crawling.Models;

namespace ShoutingIguana.Engine.Storage.Models;

internal sealed record UpdateCrawlStatusCommand(CrawlStatus Status, bool IsFinished) : WriterCommand
{
    /// <summary>For a recheck's run, the requests it sent the site.</summary>
    public long Requests { get; init; }
}
