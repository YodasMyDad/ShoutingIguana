using Microsoft.Extensions.Logging;

namespace ShoutingIguana.Cli.Commands;

internal static class Logging
{
    public static ILoggerFactory Create(bool verbose) => LoggerFactory.Create(builder =>
    {
        builder.AddSimpleConsole(console =>
        {
            console.SingleLine = true;
            console.TimestampFormat = "HH:mm:ss ";
        });
        builder.SetMinimumLevel(verbose ? LogLevel.Information : LogLevel.Warning);

        // Notices are already printed by the console observer, and skipped checks by the analysis summary's notes; only
        // failures need the log too.
        if (!verbose)
        {
            builder.AddFilter("ShoutingIguana.Engine.Crawling.CrawlSession", LogLevel.Error);
            builder.AddFilter("ShoutingIguana.Engine.Analysis.CrawlAnalyzer", LogLevel.Error);
        }
    });
}
