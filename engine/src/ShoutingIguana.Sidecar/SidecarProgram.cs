using ShoutingIguana.Sidecar.Models;

namespace ShoutingIguana.Sidecar;

/// <summary>
/// The si-sidecar process: started by the Tauri shell, it writes one ready line to stdout once it serves requests, and
/// exits 0 after a clean shutdown. Everything else it says goes to stderr and the log file.
/// </summary>
internal static class SidecarProgram
{
    public const int BAD_ARGUMENTS = 2;
    public const int FAILED = 1;

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

        SidecarHost host;
        try
        {
            host = await SidecarHost.StartAsync(options).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A port already taken, a state folder that can't be created: say so in a line, as the CLI does.
            await Console.Error.WriteLineAsync($"The sidecar couldn't start: {exception.Message}").ConfigureAwait(false);
            return FAILED;
        }

        await using (host.ConfigureAwait(false))
        {
            await Console.Out.WriteLineAsync(host.ReadyLine).ConfigureAwait(false);
            await Console.Out.FlushAsync().ConfigureAwait(false);
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
