using ShoutingIguana.Engine.Crawling.Models;

namespace ShoutingIguana.Engine.Storage.Models;

internal sealed record UpdateCrawlStatusCommand(CrawlStatus Status, bool IsFinished) : WriterCommand;
