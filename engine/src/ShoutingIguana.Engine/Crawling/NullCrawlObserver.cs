using ShoutingIguana.Engine.Crawling.Models;

namespace ShoutingIguana.Engine.Crawling;

internal sealed class NullCrawlObserver : ICrawlObserver
{
    public static NullCrawlObserver Instance { get; } = new();

    public void OnProgress(CrawlProgress progress)
    {
    }

    public void OnNotice(CrawlNotice notice)
    {
    }
}
