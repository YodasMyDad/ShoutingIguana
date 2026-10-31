using ShoutingIguana.Engine.Crawling.Models;

namespace ShoutingIguana.Engine.Crawling;

/// <summary>Receives crawl progress (twice a second) and notices. Called from background threads.</summary>
public interface ICrawlObserver
{
    void OnProgress(CrawlProgress progress);

    void OnNotice(CrawlNotice notice);
}
