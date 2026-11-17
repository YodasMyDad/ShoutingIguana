using System.CommandLine;
using ShoutingIguana.Cli.Commands;

namespace ShoutingIguana.Cli;

/// <summary>The si command tree, shared by the program and the CLI tests.</summary>
internal static class CliApp
{
    public static RootCommand Create() => new("Shouting Iguana crawler, v2 engine.")
    {
        CrawlCommand.Create(),
        AnalyzeCommand.Create(),
        IssuesCommand.Create(),
        ExportCommand.Create(),
        ExportCommand.CreateReport(),
        ScoreCommand.Create(),
        CompareCommand.Create(),
        StatsCommand.Create(),
        TestSiteCommand.Create(),
        BenchCommand.Create(),
        ReadBenchCommand.Create()
    };

    // Ctrl+C asks the crawl to stop; give in-flight requests time to settle so the crawl stays resumable.
    public static Task<int> RunAsync(string[] args) =>
        Create().Parse(args).InvokeAsync(new InvocationConfiguration { ProcessTerminationTimeout = TimeSpan.FromSeconds(60) });
}
