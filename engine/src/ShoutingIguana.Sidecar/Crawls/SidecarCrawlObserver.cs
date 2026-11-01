using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;

namespace ShoutingIguana.Sidecar.Crawls;

/// <summary>Hands one crawl's progress and notices to the manager, from the engine's threads.</summary>
internal sealed class SidecarCrawlObserver(CrawlManager manager, LiveCrawl crawl) : ICrawlObserver
{
    public void OnProgress(CrawlProgress progress) => manager.OnProgress(crawl, progress);

    public void OnNotice(CrawlNotice notice) => manager.OnNotice(crawl, notice);
}
