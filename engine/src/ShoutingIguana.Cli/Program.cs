using System.CommandLine;
using ShoutingIguana.Cli.Commands;

var root = new RootCommand("Shouting Iguana crawler, v2 engine.")
{
    CrawlCommand.Create(),
    AnalyzeCommand.Create(),
    IssuesCommand.Create(),
    StatsCommand.Create(),
    TestSiteCommand.Create(),
    BenchCommand.Create()
};

// Ctrl+C asks the crawl to stop; give in-flight requests time to settle so the crawl stays resumable.
return await root.Parse(args).InvokeAsync(new InvocationConfiguration { ProcessTerminationTimeout = TimeSpan.FromSeconds(60) });
