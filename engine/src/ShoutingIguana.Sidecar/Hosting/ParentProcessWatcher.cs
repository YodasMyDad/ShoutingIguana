using System.Diagnostics;

namespace ShoutingIguana.Sidecar.Hosting;

/// <summary>
/// Shuts the sidecar down when the shell that started it ends, however it ends: the second guard, after the stdin
/// lifeline, so no sidecar outlives its app.
/// </summary>
internal sealed class ParentProcessWatcher(int parentProcessId, IHostApplicationLifetime lifetime, ILogger<ParentProcessWatcher> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Once the sidecar is serving, so a parent that is already gone shuts it down cleanly rather than part-way through starting.
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (lifetime.ApplicationStarted.Register(() => started.TrySetResult()).ConfigureAwait(false))
        {
            try
            {
                await started.Task.WaitAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }

        Process parent;
        try
        {
            parent = Process.GetProcessById(parentProcessId);
        }
        catch (ArgumentException)
        {
            logger.LogWarning("The parent process {ParentProcessId} isn't running, so the sidecar is shutting down", parentProcessId);
            lifetime.StopApplication();
            return;
        }

        using (parent)
        {
            try
            {
                await parent.WaitForExitAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }

        logger.LogInformation("The parent process {ParentProcessId} ended, so the sidecar is shutting down", parentProcessId);
        lifetime.StopApplication();
    }
}
