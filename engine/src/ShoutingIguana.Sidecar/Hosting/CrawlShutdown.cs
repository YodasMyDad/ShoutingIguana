using ShoutingIguana.Sidecar.Crawls;

namespace ShoutingIguana.Sidecar.Hosting;

/// <summary>
/// On shutdown (the shutdown call, Ctrl+C, SIGTERM or the shell ending), every crawl is stopped and stays resumable, and
/// every analysis is cancelled. Registered before the web server, so it stops after the server has stopped taking
/// requests.
/// </summary>
internal sealed class CrawlShutdown(CrawlManager crawls) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => crawls.StopAllAsync(cancellationToken);
}
