using System.Collections.Concurrent;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>Keeps every notice a crawl raises, for tests to assert on.</summary>
internal sealed class RecordingObserver : ICrawlObserver
{
    private readonly ConcurrentQueue<CrawlNotice> _notices = new();

    public IReadOnlyList<CrawlNotice> Notices => [.. _notices];

    public int Count(string code) => _notices.Count(notice => notice.Code == code);

    public void OnProgress(CrawlProgress progress)
    {
    }

    public void OnNotice(CrawlNotice notice) => _notices.Enqueue(notice);
}
