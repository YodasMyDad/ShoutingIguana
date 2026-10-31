using System.Collections.Concurrent;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>Keeps every notice a crawl raises, and when on the clock it was given, for tests to assert on.</summary>
internal sealed class RecordingObserver(TimeProvider? time = null) : ICrawlObserver
{
    private readonly ConcurrentQueue<(CrawlNotice Notice, DateTimeOffset At)> _notices = new();
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public IReadOnlyList<CrawlNotice> Notices => [.. _notices.Select(notice => notice.Notice)];

    public int Count(string code) => _notices.Count(notice => notice.Notice.Code == code);

    /// <summary>When the first notice with this code was raised.</summary>
    public DateTimeOffset FirstAt(string code) => _notices.First(notice => notice.Notice.Code == code).At;

    public void OnProgress(CrawlProgress progress)
    {
    }

    public void OnNotice(CrawlNotice notice) => _notices.Enqueue((notice, _time.GetUtcNow()));
}
