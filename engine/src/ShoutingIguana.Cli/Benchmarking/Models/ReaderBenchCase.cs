namespace ShoutingIguana.Cli.Benchmarking.Models;

/// <summary>One read the reader benchmark times: its first page, and a page half way down when it has more than one.</summary>
/// <param name="Kind">What it reads: <c>grid</c>, <c>facets</c>, <c>lookup</c>, <c>inspector</c>, <c>links</c>, <c>groups</c> or <c>rows</c>.</param>
/// <param name="Matching">The rows it lists, where it lists rows.</param>
/// <param name="HalfP50Ms">Null when it has one page.</param>
public sealed record ReaderBenchCase(
    string Name,
    string Kind,
    long? Matching,
    double FirstP50Ms,
    double FirstP95Ms,
    double? HalfP50Ms,
    double? HalfP95Ms);
