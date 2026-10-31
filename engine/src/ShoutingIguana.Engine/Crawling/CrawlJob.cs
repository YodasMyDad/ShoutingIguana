using ShoutingIguana.Engine.Crawling.Models;

namespace ShoutingIguana.Engine.Crawling;

/// <summary>A running crawl. Pause, resume or stop it; await <see cref="Completion"/> for the summary.</summary>
public sealed class CrawlJob : IAsyncDisposable
{
    private readonly CrawlSession _session;

    internal CrawlJob(CrawlSession session)
    {
        _session = session;
        Completion = Task.Run(session.RunAsync);
    }

    public Task<CrawlSummary> Completion { get; }

    public CrawlProgress Progress => _session.Snapshot();

    public bool IsPaused => _session.IsPaused;

    public void Pause() => _session.Pause();

    public void Resume() => _session.Resume();

    /// <summary>Stops after in-flight requests settle. The crawl database stays resumable.</summary>
    public Task<CrawlSummary> StopAsync()
    {
        _session.RequestStop();
        return Completion;
    }

    public async ValueTask DisposeAsync()
    {
        if (!Completion.IsCompleted)
        {
            await StopAsync().ConfigureAwait(false);
        }
    }
}
