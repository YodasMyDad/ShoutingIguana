using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>The two crawl files of a comparison, read once per pair of snapshots, and how far they can be compared.</summary>
/// <param name="Then">The other crawl, compared with.</param>
/// <param name="Now">This crawl.</param>
internal sealed record ComparisonPair(ComparedFile Then, ComparedFile Now, ComparabilityVerdict Verdict)
{
    public bool CountsOnly => Verdict.Comparable == Comparability.CountsOnly;
}
