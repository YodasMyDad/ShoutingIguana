namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>A crawl as the library shows it.</summary>
internal enum CrawlListStatus
{
    /// <summary>Crawling, in this process.</summary>
    Running,
    Paused,
    Stopping,

    /// <summary>An analysis is running, in this process.</summary>
    Analyzing,

    /// <summary>A recheck is fetching some of the crawl's URLs again, in this process; analysing follows it (the fix loop).</summary>
    Rechecking,

    /// <summary>Stopped, and can be continued.</summary>
    Stopped,

    /// <summary>The file says it is running, but nothing holds its lock: the process crawling it ended. It can be continued.</summary>
    Interrupted,
    Completed,
    Failed,

    /// <summary>Another process is writing to it: a crawl, or an analysis.</summary>
    InUse,

    /// <summary>
    /// The file couldn't be read, though it may be a crawl: reading it took too long, it is damaged, or another program
    /// had it locked. The item's error says which.
    /// </summary>
    Unreadable
}
