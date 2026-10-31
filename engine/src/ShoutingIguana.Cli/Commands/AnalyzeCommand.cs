using System.CommandLine;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Cli.Output;
using ShoutingIguana.Engine.Analysis;

namespace ShoutingIguana.Cli.Commands;

internal static class AnalyzeCommand
{
    public const int CANCELLED = 130;

    public static Command Create()
    {
        var database = new Argument<string>("db") { Description = "A .sicrawl file." };
        var verbose = new Option<bool>("--verbose", "-v") { Description = "Detailed engine logging." };
        var command = new Command("analyze", "Run the SEO checks over a stored crawl (again), replacing its issues.") { database, verbose };

        command.SetAction((parse, cancellationToken) => CommandErrors.RunAsync(async () =>
        {
            var path = parse.GetValue(database)!;
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"No crawl file at {path}.");
                return 2;
            }

            using var loggerFactory = Logging.Create(parse.GetValue(verbose));
            return await RunAsync(path, loggerFactory, cancellationToken);
        }));

        return command;
    }

    /// <summary>
    /// Analyses a crawl and prints the summary; returns the exit code. Shared with the crawl command, which analyses
    /// when it finishes. A cancelled analysis keeps the previous results.
    /// </summary>
    public static async Task<int> RunAsync(string path, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        try
        {
            var summary = await new CrawlAnalyzer(loggerFactory).AnalyzeAsync(path, cancellationToken: cancellationToken);
            Console.WriteLine($"Analysed {summary.PagesAnalyzed:N0} pages in {summary.Duration.TotalSeconds:N1} s.");
            if (summary.IsPartial)
            {
                Console.WriteLine("  The crawl didn't finish, so orphan and sitemap-coverage checks were skipped.");
            }
            IssueSummaryPrinter.Print(summary.Issues);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Console.Error.WriteLine("Analysis cancelled; the results of the previous analysis, if any, are unchanged.");
            return CANCELLED;
        }
    }
}
