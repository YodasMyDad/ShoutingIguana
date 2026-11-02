using System.CommandLine;
using System.Runtime.InteropServices;
using System.Text.Json;
using ShoutingIguana.Cli.Benchmarking;
using ShoutingIguana.Cli.Benchmarking.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Cli.Commands;

/// <summary>
/// Times the grids' readers on a crawl file and fails when a read is too slow: with <c>--gate</c>, each class of read over
/// its own p95 at 100k URLs (the gate CI holds the 100k benchmark crawl to, which catches a read that would miss the
/// budget at 1M); with <c>--max-p95-ms</c>, any read over one budget. Run it on any analysed crawl, a 1M one included.
/// </summary>
internal static class ReadBenchCommand
{
    public static Command Create()
    {
        var database = new Argument<string>("db") { Description = "An analysed .sicrawl file." };
        var runs = new Option<int>("--runs") { Description = "Times each read runs, after one untimed read.", DefaultValueFactory = _ => 10 };
        var maxP95 = new Option<double>("--max-p95-ms") { Description = "Fail if any read's p95 is over this many ms (0 = no check)." };
        var gate = new Option<bool>("--gate") { Description = "Fail if any read's p95 is over its class's gate for a crawl of 100k URLs." };
        var json = new Option<string?>("--json") { Description = "Write the results as JSON to this file." };
        var only = new Option<string?>("--only") { Description = "Run only the reads whose name contains this text." };
        var command = new Command("readbench", "Benchmark the grids' readers on a crawl file.") { database, runs, maxP95, gate, json, only };

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
            var gated = parse.GetValue(gate);
            var first = UrlGridReader.Query(path, new UrlQuery { Limit = 1 });
            var held = string.Join(", ", new[] { budget > 0 ? $"budget {budget:N0} ms at p95" : null, gated ? "each class at its 100k gate" : null }.OfType<string>());
            Console.WriteLine($"Reading {path}: {first.Total:N0} URLs, {runCount} runs of each read, nothing cached{(held.Length > 0 ? ", " + held : string.Empty)}");
            if (!first.Summary.Available)
            {
                Console.WriteLine($"  The crawl has no URL summary, as {Why(first.Summary.Reason!.Value)}, so the reads that need one are skipped. Analyse it first for the full run.");
            }

            var cases = new ReaderBenchmark(path, runCount, Console.WriteLine, parse.GetValue(only)).Run();
            if (cases.Count == 0)
            {
                Console.Error.WriteLine($"No read's name contains '{parse.GetValue(only)}', so nothing was timed.");
                return 2;
            }

            List<string> failures = [];
            if (budget > 0)
            {
                failures.AddRange(Over(cases, _ => budget, "over the budget of"));
            }

            if (gated)
            {
                failures.AddRange(Over(cases, read => ReaderBenchmark.GATE_MS[read.Class], $"over its class's gate of"));
            }

            if ((budget > 0 || gated) && !first.Summary.Available)
            {
                failures.Add($"The crawl has no URL summary, as {Why(first.Summary.Reason!.Value)}, so the grids' main reads weren't measured");
            }

            var result = new ReaderBenchResult(path, first.Total, runCount, budget, gated, first.Summary.Available, ReaderBenchmark.Machine,
                RuntimeInformation.FrameworkDescription, cases, failures);
            var slowest = cases.MaxBy(read => read.WorstP95Ms);
            Console.WriteLine();
            Console.WriteLine($"Readers: {cases.Count} reads of {first.Total:N0} URLs ({result.Machine})");
            if (slowest is not null)
            {
                Console.WriteLine($"  Slowest      {slowest.Name}: {slowest.WorstP95Ms:N1} ms at p95");
            }

            foreach (var group in cases.GroupBy(read => read.Class).OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                var worst = group.MaxBy(read => read.WorstP95Ms)!;
                Console.WriteLine($"  {group.Key,-12} slowest {worst.WorstP95Ms,8:N1} ms (gate {ReaderBenchmark.GATE_MS[group.Key]:N0}): {worst.Name}");
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

    /// <summary>Each read over its limit, at the page where it is.</summary>
    private static IEnumerable<string> Over(IReadOnlyList<ReaderBenchCase> cases, Func<ReaderBenchCase, double> limit, string what) =>
        cases.SelectMany(read => new[] { ("first page", (double?)read.FirstP95Ms), ("page half way down", read.HalfP95Ms), ("page just past half way", read.PastHalfP95Ms) }
            .Where(page => page.Item2 > limit(read))
            .Select(page => $"{read.Name}: {page.Item1} {page.Item2:N1} ms at p95, {what} {limit(read):N0} ms"));

    private static string Why(SummaryUnavailableReason reason) => reason switch
    {
        SummaryUnavailableReason.Crawling => "the crawl is running",
        SummaryUnavailableReason.NotAnalyzed => "it was never analysed",
        SummaryUnavailableReason.EngineChanged => "another version of the engine analysed it",
        _ => "it was continued after its analysis"
    };
}
