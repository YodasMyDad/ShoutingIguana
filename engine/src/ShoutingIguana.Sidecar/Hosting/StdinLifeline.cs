namespace ShoutingIguana.Sidecar.Hosting;

/// <summary>
/// With <c>--stdin-lifeline</c>, the shell holds the other end of stdin. A <c>shutdown</c> line shuts the sidecar down,
/// and so does the end of stdin, which comes however the shell ends: a normal exit, a crash or a hard kill. The shell's
/// sidecar plugin leaves a sidecar running in those cases, so this is what makes sure none outlives its app.
/// </summary>
internal sealed class StdinLifeline(TextReader input, IHostApplicationLifetime lifetime, ILogger<StdinLifeline> logger) : IHostedService
{
    public const string SHUTDOWN = "shutdown";

    /// <summary>
    /// Watches once the sidecar is serving, so a stdin that is already over shuts it down cleanly rather than part-way
    /// through starting. Reading the console blocks, even through its async methods, so it gets a thread of its own:
    /// one that never holds up a shutdown that came another way.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        lifetime.ApplicationStarted.Register(() => new Thread(Watch) { IsBackground = true, Name = "Stdin lifeline" }.Start());
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Reads stdin until a shutdown line or its end, then shuts the sidecar down. Other lines are ignored.</summary>
    internal void Watch()
    {
        try
        {
            while (input.ReadLine() is { } line)
            {
                if (line.Trim() == SHUTDOWN)
                {
                    logger.LogInformation("Asked on stdin to shut down");
                    lifetime.StopApplication();
                    return;
                }
            }

            logger.LogInformation("Stdin ended: the shell has gone, so the sidecar is shutting down");
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            logger.LogInformation("Stdin can't be read ({Error}): the shell has gone, so the sidecar is shutting down", exception.Message);
        }

        lifetime.StopApplication();
    }
}
