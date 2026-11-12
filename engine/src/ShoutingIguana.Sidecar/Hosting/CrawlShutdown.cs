using ShoutingIguana.Sidecar.Crawls;
using ShoutingIguana.Sidecar.Exports;

namespace ShoutingIguana.Sidecar.Hosting;

/// <summary>
/// On shutdown (the shutdown call, Ctrl+C, SIGTERM or the shell ending), every crawl is stopped and stays resumable,
/// every analysis is cancelled, and every export is cancelled, leaving no file. That starts as soon as shutdown does,
/// before the web server waits for the requests in flight, however slow one is; then the host waits for them all, as
/// long as its shutdown timeout allows.
/// </summary>
internal sealed class CrawlShutdown(CrawlManager crawls, ExportManager exports) : IHostedLifecycleService
{
    private Task _stopped = Task.CompletedTask;

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Runs before any hosted service stops, the web server included. Nothing new can start from here on.</summary>
    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        _stopped = Task.WhenAll(crawls.StopAllAsync(cancellationToken), exports.StopAllAsync(cancellationToken));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => _stopped;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
