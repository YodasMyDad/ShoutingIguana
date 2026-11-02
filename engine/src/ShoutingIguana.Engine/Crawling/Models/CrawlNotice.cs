namespace ShoutingIguana.Engine.Crawling.Models;

/// <summary>Something the person running the crawl should know about, e.g. the site is blocking the crawler.</summary>
/// <param name="Code">Stable identifier such as <c>blocked</c> or <c>robots-unreachable</c>, for UIs to key on.</param>
public sealed record CrawlNotice(CrawlNoticeLevel Level, string Code, string Message)
{
    /// <summary>
    /// Numbers the crawl's notices, from 1, across every run of it: the crawl file keeps each one under its id, so a UI
    /// that heard of one live and reads it back from the file can tell it's the same.
    /// </summary>
    public long Id { get; init; }

    /// <summary>When it was raised.</summary>
    public DateTimeOffset At { get; init; }

    /// <summary>The facts it's about (which host, how many responses), for a UI to offer what to do.</summary>
    public CrawlNoticeData? Data { get; init; }
}
