namespace ShoutingIguana.Sidecar.Hosting;

/// <summary>
/// An exception nothing caught ends the process, and .NET reports it only on stderr, of which the shell keeps just the
/// tail. This writes it, with its whole stack, to the sidecar's own log first, so a crash always leaves its cause in
/// <c>&lt;state-dir&gt;/logs</c>. The program starts it once the sidecar serves, and disposing it stops it.
/// </summary>
internal sealed class UnhandledExceptionLog(ILogger<UnhandledExceptionLog> logger) : IDisposable
{
    public UnhandledExceptionLog Start()
    {
        AppDomain.CurrentDomain.UnhandledException += Log;
        return this;
    }

    public void Dispose() => AppDomain.CurrentDomain.UnhandledException -= Log;

    /// <summary>Runs on the thread that threw, just before the process ends: the file log writes as it is called.</summary>
    internal void Log(object? sender, UnhandledExceptionEventArgs args) =>
        logger.LogCritical(args.ExceptionObject as Exception, "The sidecar stopped on an exception nothing caught: {ExceptionType}",
            args.ExceptionObject.GetType().FullName);
}
