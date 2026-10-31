namespace ShoutingIguana.Cli.Benchmarking.Models;

/// <param name="EarlyWorkingSetMb">Peak working set while 10-30% of the crawl was done.</param>
/// <param name="LateWorkingSetMb">Peak working set over the last 30% of the crawl.</param>
/// <param name="GrowthMb">Late minus early: close to zero means memory stays flat as the crawl grows.</param>
/// <param name="GrowthBytesPerUrl">
/// What each extra URL costs in memory: the least-squares slope of the working set against URLs fetched over the last
/// 80% of the crawl. A flat engine stays near zero however big the site; a per-URL leak shows up as its size.
/// </param>
/// <param name="Correct">Every planted page and issue was found, and nothing else was flagged.</param>
/// <param name="AnalysisPeakWorkingSetMb">Peak working set while the analysis ran.</param>
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
    double GrowthBytesPerUrl,
    double DatabaseMb,
    double AnalysisSeconds,
    long IssuesFound,
    double AnalysisPeakWorkingSetMb,
    string Machine,
    string Runtime,
    IReadOnlyList<string> Mismatches,
    IReadOnlyList<string> Failures);
