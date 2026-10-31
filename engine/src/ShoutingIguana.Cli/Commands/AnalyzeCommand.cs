using System.CommandLine;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Cli.Output;
using ShoutingIguana.Engine.Analysis;

namespace ShoutingIguana.Cli.Commands;

internal static class AnalyzeCommand
{
    public static Command Create()
    {
        var database = new Argument<string>("db") { Description = "A .sicrawl file." };
        var verbose = new Option<bool>("--verbose", "-v") { Description = "Detailed engine logging." };
        var command = new Command("analyze", "Run the SEO checks over a stored crawl (again), replacing its issues.") { database, verbose };

        command.SetAction(async (parse, cancellationToken) =>
        {
            var path = parse.GetValue(database)!;
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"No crawl file at {path}.");
                return 2;
            }

            using var loggerFactory = Logging.Create(parse.GetValue(verbose));
            return await RunAsync(path, loggerFactory, cancellationToken) ? 0 : 1;
        });

        return command;
    }

    /// <summary>Analyses a crawl and prints the summary. Shared with the crawl command, which analyses when it finishes.</summary>
    public static async Task<bool> RunAsync(string path, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
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
            return true;
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return false;
        }
    }
}
