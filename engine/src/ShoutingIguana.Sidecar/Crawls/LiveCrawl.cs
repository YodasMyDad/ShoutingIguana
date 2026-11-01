using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Sidecar.Crawls.Models;

namespace ShoutingIguana.Sidecar.Crawls;

/// <summary>A crawl this process is crawling or analysing. <see cref="CrawlManager"/> changes it under its lock.</summary>
internal sealed class LiveCrawl(string id, string path, string startUrl)
{
    public string Id { get; } = id;

    public string Path { get; } = path;

    public string StartUrl { get; } = startUrl;

    public LiveCrawlState State { get; set; }

    /// <summary>Null until the engine has opened the file.</summary>
    public CrawlJob? Job { get; set; }

    /// <summary>A stop asked for while the file was still opening, carried out once it is open.</summary>
    public bool StopRequested { get; set; }

    public bool AnalyzeWhenDone { get; set; }

    /// <summary>The engine's latest progress, for the library's counts.</summary>
    public CrawlProgress? Progress { get; set; }

    /// <summary>
    /// When this run began here, before its file was opened: everything fetched before it was fetched in an earlier run.
    /// Null for a crawl that is only being analysed here.
    /// </summary>
    public DateTime? RunStartedAt { get; init; }

    /// <summary>The run continues a crawl from its file, which has earlier runs to count back.</summary>
    public bool Resumed { get; init; }

    /// <summary>This run's progress over time. Kept while the crawl is live here, its analysis included.</summary>
    public ProgressHistory History { get; } = new();

    /// <summary>The earlier runs, counted back from the file once and kept: they don't change while this one runs.</summary>
    public CrawlHistory? EarlierRuns { get; set; }

    public CancellationTokenSource? Analysis { get; set; }

    /// <summary>Everything this process does with the crawl, from opening its file to the end of its analysis.</summary>
    public Task Lifetime { get; set; } = Task.CompletedTask;

    /// <summary>Completes once the engine has opened the file, or fails with the problem that stopped it.</summary>
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
