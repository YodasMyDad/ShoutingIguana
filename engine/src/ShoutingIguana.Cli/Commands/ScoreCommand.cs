using System.CommandLine;
using ShoutingIguana.Cli.Output;
using ShoutingIguana.Engine.Storage;

namespace ShoutingIguana.Cli.Commands;

/// <summary>The site's score, as the last analysis stored it, with how it was worked out: the same number the app and the report show.</summary>
internal static class ScoreCommand
{
    public static Command Create()
    {
        var database = new Argument<string>("db") { Description = "A .sicrawl file." };
        var command = new Command("score", "Show the site's score from the last analysis, and how it was worked out.") { database };

        command.SetAction(parse => CommandErrors.Run(() =>
        {
            var path = parse.GetValue(database)!;
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"No crawl file at {path}.");
                return 2;
            }

            if (IssueReader.ReadResults(path) is not { } results)
            {
                Console.Error.WriteLine($"This crawl hasn't been analysed yet. Run: si analyze \"{path}\"");
                return 2;
            }

            ScorePrinter.Print(results, Console.Out);
            return 0;
        }));

        return command;
    }
}
