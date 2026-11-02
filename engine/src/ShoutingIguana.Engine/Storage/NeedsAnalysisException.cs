using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// A grid query filters or sorts on columns of the URL summary, and the crawl has no summary that describes it: it's
/// running, was never analysed, was analysed by another engine, or changed since. Refused, rather than ignored.
/// </summary>
public sealed class NeedsAnalysisException(SummaryUnavailableReason reason, IReadOnlyList<string> columns)
    : Exception($"Filtering or sorting by {string.Join(", ", columns)} needs an analysis of this crawl.")
{
    public SummaryUnavailableReason Reason { get; } = reason;

    /// <summary>The columns the query filters or sorts on that need the summary.</summary>
    public IReadOnlyList<string> Columns { get; } = columns;
}
