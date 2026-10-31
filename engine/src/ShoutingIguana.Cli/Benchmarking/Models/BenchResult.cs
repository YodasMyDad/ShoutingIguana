namespace ShoutingIguana.Cli.Benchmarking.Models;

/// <param name="EarlyWorkingSetMb">Peak working set while 10-30% of the crawl was done.</param>
/// <param name="LateWorkingSetMb">Peak working set over the last 30% of the crawl.</param>
/// <param name="GrowthMb">Late minus early: close to zero means memory stays flat as the crawl grows.</param>
internal sealed record BenchResult(
    int SyntheticPages,
    int Concurrency,
    string Status,
    bool Correct,
    int ExpectedHtmlPages,
    long CrawledHtmlPages,
    long Fetched,
    long Links,
    double ElapsedSeconds,
    double PagesPerSecond,
    double PeakWorkingSetMb,
    double PeakManagedMb,
    double EarlyWorkingSetMb,
    double LateWorkingSetMb,
    double GrowthMb,
    double DatabaseMb,
    string Machine,
    string Runtime,
    IReadOnlyList<string> Mismatches,
    IReadOnlyList<string> Failures);
