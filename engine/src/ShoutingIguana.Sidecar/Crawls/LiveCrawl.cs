using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
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

    public CancellationTokenSource? Analysis { get; set; }

    /// <summary>Everything this process does with the crawl, from opening its file to the end of its analysis.</summary>
    public Task Lifetime { get; set; } = Task.CompletedTask;

    /// <summary>Completes once the engine has opened the file, or fails with the problem that stopped it.</summary>
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
