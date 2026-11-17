using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Crawling.Models;

namespace ShoutingIguana.Cli.Benchmarking.Models;

/// <summary>One crawl of the benchmark's site and its analysis, with the memory sampled through each.</summary>
/// <param name="LinkReads">With --read-links, the links grid read all through the crawl; null without.</param>
/// <param name="Analysis">Null when the crawl didn't complete, or the analysis was stopped.</param>
internal sealed record BenchRun(
    CrawlSummary Summary,
    IReadOnlyList<MemorySample> Samples,
    LinkReads? LinkReads,
    AnalysisSummary? Analysis,
    IReadOnlyList<MemorySample> AnalysisSamples);
