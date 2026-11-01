using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Sidecar.Events.Models;

/// <summary>
/// How far a running analysis has got: <paramref name="Done"/> of <paramref name="Total"/> in its phase (URLs read, pages
/// checked, cross-page rules finished, summary rows, then writing). Each phase as it starts and ends, and a few times a
/// second between; the last of a phase has done equal to total.
/// </summary>
internal sealed record AnalysisEventDto(string CrawlId, AnalysisPhase Phase, long Done, long Total);
