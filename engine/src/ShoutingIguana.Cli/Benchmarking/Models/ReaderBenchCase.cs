namespace ShoutingIguana.Cli.Benchmarking.Models;

/// <summary>
/// One read the reader benchmark times: its first page, a page half way down and one just past it (read backwards from
/// the far end) when it has more than one.
/// </summary>
/// <param name="Kind">What it reads: <c>grid</c>, <c>facets</c>, <c>lookup</c>, <c>inspector</c>, <c>links</c>, <c>groups</c>, <c>rows</c>, <c>links-grid</c>, <c>sitemaps</c> or <c>robots</c>.</param>
/// <param name="Class">How it reads, which sets how it grows with the crawl and so its gate (<see cref="ReaderBenchmark.GATE_MS"/>).</param>
/// <param name="Matching">The rows it lists, where it lists rows.</param>
/// <param name="HalfP50Ms">Null when it has one page.</param>
/// <param name="PastHalfP50Ms">Just past half way, where a page is read from the far end; null when it has one page.</param>
public sealed record ReaderBenchCase(
    string Name,
    string Kind,
    string Class,
    long? Matching,
    double FirstP50Ms,
    double FirstP95Ms,
    double? HalfP50Ms,
    double? HalfP95Ms,
    double? PastHalfP50Ms,
    double? PastHalfP95Ms)
{
    /// <summary>Its slowest page at p95.</summary>
    public double WorstP95Ms => Math.Max(FirstP95Ms, Math.Max(HalfP95Ms ?? 0, PastHalfP95Ms ?? 0));
}
