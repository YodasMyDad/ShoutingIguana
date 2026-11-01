using Microsoft.Extensions.Logging.Console;
using Serilog;
using Serilog.Events;
using ShoutingIguana.Sidecar.Models;

namespace ShoutingIguana.Sidecar.Hosting;

/// <summary>
/// Logs go to stderr and to <c>&lt;state-dir&gt;/logs</c>, never to stdout: stdout carries only the ready line, which
/// the shell reads.
/// </summary>
internal static class SidecarLogging
{
    private const string FILE_TEMPLATE = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";
    private const int RETAINED_FILES = 14;
    private const long MAX_FILE_BYTES = 50L * 1024 * 1024;

    public static void Configure(ILoggingBuilder logging, SidecarOptions options)
    {
        logging.ClearProviders();
        logging.SetMinimumLevel(LogLevel.Information);

        // A line per request would bury what matters; failures are logged by the sidecar itself.
        logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        logging.AddFilter("Microsoft.Hosting", LogLevel.Warning);

        if (options.ConsoleLogging)
        {
            logging.AddSimpleConsole(console =>
            {
                console.SingleLine = true;
                console.TimestampFormat = "HH:mm:ss ";
            });
            logging.Services.Configure<ConsoleLoggerOptions>(console =>
            {
                console.LogToStandardErrorThreshold = LogLevel.Trace;

                // A shell that stops reading stderr must not block whoever logs: past the queue, lines are dropped
                // from stderr only. The file log still gets every one.
                console.QueueFullMode = ConsoleLoggerQueueFullMode.DropWrite;
            });
        }

        if (options.FileLogging)
        {
            // The filters above decide what is logged; the file takes whatever reaches it.
            var file = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.File(
                    Path.Combine(options.StateDirectory, "logs", "sidecar-.log"),
                    restrictedToMinimumLevel: LogEventLevel.Verbose,
                    outputTemplate: FILE_TEMPLATE,
                    fileSizeLimitBytes: MAX_FILE_BYTES,
                    rollingInterval: RollingInterval.Day,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: RETAINED_FILES)
                .CreateLogger();
            logging.AddSerilog(file, dispose: true);
        }
    }
}
