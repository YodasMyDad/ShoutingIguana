namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>
/// How far an analysis has got: <paramref name="Done"/> of <paramref name="Total"/> in its phase. Each phase is
/// reported as it starts (0 done) and as it ends (done equals total, which the end makes exact), and a few times a second
/// in between.
/// </summary>
public sealed record AnalysisProgress(AnalysisPhase Phase, long Done, long Total);
