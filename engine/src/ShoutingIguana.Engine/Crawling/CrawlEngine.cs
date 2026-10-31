using Microsoft.Extensions.Logging;
using ShoutingIguana.Engine.Crawling.Models;

namespace ShoutingIguana.Engine.Crawling;

/// <summary>Entry point for hosts (CLI, desktop app, API): starts or resumes crawls.</summary>
public sealed class CrawlEngine(ILoggerFactory loggerFactory, TimeProvider timeProvider)
{
    public CrawlEngine(ILoggerFactory loggerFactory)
        : this(loggerFactory, TimeProvider.System)
    {
    }

    /// <summary>Opens (or resumes) the crawl database and starts crawling in the background.</summary>
    /// <exception cref="ArgumentException">The options are invalid.</exception>
    /// <exception cref="InvalidOperationException">The database already holds a crawl and Resume is false.</exception>
    public CrawlJob Start(CrawlRequest request, ICrawlObserver? observer = null) =>
        new(CrawlSession.Open(request, observer ?? NullCrawlObserver.Instance, loggerFactory, timeProvider));
}
