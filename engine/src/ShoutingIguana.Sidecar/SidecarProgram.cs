using ShoutingIguana.Sidecar.Hosting;
using ShoutingIguana.Sidecar.Models;

namespace ShoutingIguana.Sidecar;

/// <summary>
/// The si-sidecar process: started by the Tauri shell, it writes one ready line to stdout once it serves requests, and
/// exits 0 after a clean shutdown. Everything else it says goes to stderr and the log file.
/// </summary>
internal static class SidecarProgram
{
    public const int FAILED = 1;
    public const int BAD_ARGUMENTS = 2;

    /// <summary>Another sidecar is using the state folder: the shell is already running one, or a stale one hasn't exited.</summary>
    public const int STATE_IN_USE = 3;

    public static async Task<int> RunAsync(string[] args)
    {
        if (SidecarArguments.IsHelp(args))
        {
            Console.Out.WriteLine(SidecarArguments.USAGE);
            return 0;
        }

        if (!TryReadOptions(args, out var options, out var error))
        {
            await Console.Error.WriteLineAsync(error).ConfigureAwait(false);
            return BAD_ARGUMENTS;
        }

        return await RunAsync(options, Console.Out, Console.Error).ConfigureAwait(false);
    }

    /// <summary>Starts the sidecar, writes the ready line once it serves, and returns the exit code once it has shut down.</summary>
    /// <param name="cancellationToken">Cancelling it before the sidecar is serving is a shutdown asked for while starting.</param>
    internal static async Task<int> RunAsync(SidecarOptions options, TextWriter output, TextWriter error, CancellationToken cancellationToken = default)
    {
        SidecarHost host;
        try
        {
            host = await SidecarHost.StartAsync(options, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C or SIGTERM while starting: as clean an exit as after any other shutdown, and nothing was served.
            return 0;
        }
        catch (StateFolderInUseException exception)
        {
            await error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return STATE_IN_USE;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A port already taken, a state folder that can't be created: say so in a line, as the CLI does.
            await error.WriteLineAsync($"The sidecar couldn't start: {exception.Message}").ConfigureAwait(false);
            return FAILED;
        }

        await using (host.ConfigureAwait(false))
        {
            await output.WriteLineAsync(host.ReadyLine).ConfigureAwait(false);
            await output.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            await host.WaitForShutdownAsync().ConfigureAwait(false);
        }

        return 0;
    }

    /// <summary>
    /// The options from the command line and this process's environment. The token is then taken out of the
    /// environment, so no process the sidecar starts (step 4 starts browsers) inherits it.
    /// </summary>
    internal static bool TryReadOptions(string[] args, out SidecarOptions options, out string error)
    {
        var parsed = SidecarArguments.TryParse(args, Environment.GetEnvironmentVariable, out options, out error);
        Environment.SetEnvironmentVariable(SidecarArguments.TOKEN_VARIABLE, null);
        return parsed;
    }
}
