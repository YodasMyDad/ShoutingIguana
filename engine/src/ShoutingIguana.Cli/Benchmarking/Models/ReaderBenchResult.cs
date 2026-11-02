namespace ShoutingIguana.Cli.Benchmarking.Models;

/// <summary>The reader benchmark's run: each read's times, and the ones over budget.</summary>
/// <param name="Urls">Every URL in the crawl.</param>
/// <param name="Runs">How many times each read ran, each on a fresh connection with nothing cached, as the first read of a filter set is.</param>
/// <param name="BudgetMs">The p95 every read must stay under; 0 for none.</param>
/// <param name="Gate">Each read class held to its own p95 at 100k URLs (<see cref="ReaderBenchmark.GATE_MS"/>).</param>
/// <param name="SummaryAvailable">The crawl has a URL summary that describes it, so the grids read it.</param>
public sealed record ReaderBenchResult(
    string Crawl,
    long Urls,
    int Runs,
    double BudgetMs,
    bool Gate,
    bool SummaryAvailable,
    string Machine,
    string Runtime,
    IReadOnlyList<ReaderBenchCase> Cases,
    IReadOnlyList<string> Failures);
