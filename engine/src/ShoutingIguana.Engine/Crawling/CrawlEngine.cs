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

    /// <summary>
    /// Rechecks some of a crawl's URLs (the fix loop's "Recheck these N pages"): a short run in the background that fetches
    /// the plan's URLs again into the same file, as the crawl's options say and at its pace, and leaves the crawl's status
    /// and queue as they were. Analyse the crawl after it: the analysis settles the recheck's result.
    /// </summary>
    /// <exception cref="ArgumentException">The options are invalid.</exception>
    /// <exception cref="InvalidOperationException">The plan was refused, or the file is in use or was interrupted.</exception>
    public CrawlJob Recheck(RecheckRequest request, ICrawlObserver? observer = null) =>
        new(CrawlSession.OpenRecheck(request, observer ?? NullCrawlObserver.Instance, loggerFactory, timeProvider));
}
