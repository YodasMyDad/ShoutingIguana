using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage;

namespace ShoutingIguana.Engine.Crawling;

/// <summary>
/// Stamps each notice with when it was raised, to the millisecond the crawl file keeps, has the writer keep it with the
/// crawl, and passes it on with its id: what the host hears is what the file says.
/// </summary>
internal sealed class NoticeRecorder(ICrawlObserver inner, CrawlWriter writer, TimeProvider timeProvider) : ICrawlObserver
{
    public void OnProgress(CrawlProgress progress) => inner.OnProgress(progress);

    public void OnNotice(CrawlNotice notice)
    {
        var at = DateTimeOffset.FromUnixTimeMilliseconds(timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
        inner.OnNotice(writer.RecordNotice(notice with { At = at }));
    }
}
