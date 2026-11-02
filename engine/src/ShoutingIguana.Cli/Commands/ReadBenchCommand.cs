using System.CommandLine;
using System.Runtime.InteropServices;
using System.Text.Json;
using ShoutingIguana.Cli.Benchmarking;
using ShoutingIguana.Cli.Benchmarking.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Cli.Commands;

/// <summary>
/// Times the grids' readers on a crawl file and, with a budget, fails when any read's p95 is over it: the gate CI holds
/// the URL grid to on the 100k crawl, next to the crawl benchmark. Run it on any analysed crawl, a 1M one included.
/// </summary>
internal static class ReadBenchCommand
{
    public static Command Create()
    {
        var database = new Argument<string>("db") { Description = "An analysed .sicrawl file." };
        var runs = new Option<int>("--runs") { Description = "Times each read runs, after one untimed read.", DefaultValueFactory = _ => 10 };
        var maxP95 = new Option<double>("--max-p95-ms") { Description = "Fail if any read's p95 is over this many ms (0 = no check)." };
        var json = new Option<string?>("--json") { Description = "Write the results as JSON to this file." };
        var only = new Option<string?>("--only") { Description = "Run only the reads whose name contains this text." };
        var command = new Command("readbench", "Benchmark the grids' readers on a crawl file.") { database, runs, maxP95, json, only };

        command.SetAction(async (parse, cancellationToken) => await CommandErrors.RunAsync(async () =>
        {
            var path = parse.GetValue(database)!;
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"No crawl file at {path}.");
                return 2;
            }

            var runCount = Math.Max(1, parse.GetValue(runs));
            var budget = parse.GetValue(maxP95);
            var first = UrlGridReader.Query(path, new UrlQuery { Limit = 1 });
            Console.WriteLine($"Reading {path}: {first.Total:N0} URLs, {runCount} runs of each read, nothing cached{(budget > 0 ? $", budget {budget:N0} ms at p95" : string.Empty)}");
            if (!first.Summary.Available)
            {
                Console.WriteLine($"  The crawl has no URL summary ({first.Summary.Reason}), so the reads that need one are skipped. Analyse it first for the full run.");
            }

            var cases = new ReaderBenchmark(path, runCount, Console.WriteLine, parse.GetValue(only)).Run();
            List<string> failures = [];
            if (budget > 0)
            {
                failures.AddRange(cases.Where(read => read.FirstP95Ms > budget).Select(read => $"{read.Name}: first page {read.FirstP95Ms:N1} ms at p95, over {budget:N0} ms"));
                failures.AddRange(cases.Where(read => read.HalfP95Ms > budget).Select(read => $"{read.Name}: page half way down {read.HalfP95Ms:N1} ms at p95, over {budget:N0} ms"));
                if (!first.Summary.Available)
                {
                    failures.Add($"The crawl has no URL summary ({first.Summary.Reason}), so the grids' main reads weren't measured");
                }
            }

            var result = new ReaderBenchResult(path, first.Total, runCount, budget, first.Summary.Available, ReaderBenchmark.Machine,
                RuntimeInformation.FrameworkDescription, cases, failures);
            var slowest = cases.MaxBy(read => Math.Max(read.FirstP95Ms, read.HalfP95Ms ?? 0));
            Console.WriteLine();
            Console.WriteLine($"Readers: {cases.Count} reads of {first.Total:N0} URLs ({result.Machine})");
            if (slowest is not null)
            {
                Console.WriteLine($"  Slowest      {slowest.Name}: {Math.Max(slowest.FirstP95Ms, slowest.HalfP95Ms ?? 0):N1} ms at p95");
            }

            foreach (var failure in failures)
            {
                Console.WriteLine($"  FAILED       {failure}");
            }

            if (parse.GetValue(json) is { } jsonPath)
            {
                await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(result, CliJsonContext.Default.ReaderBenchResult), cancellationToken);
            }

            return failures.Count == 0 ? 0 : 1;
        }));

        return command;
    }
}
