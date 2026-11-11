using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Sidecar.Options.Models;

/// <summary>One of the analysis's limits, as Settings shows it: what it is, in what unit, and what it may be set to.</summary>
/// <param name="Id">The JSON name of the limit's property in <see cref="AnalysisSettings"/>, which the API and the saved file key it by.</param>
/// <param name="Unit"><c>characters</c>, <c>words</c>, <c>bits</c>, <c>bytes</c> or <c>ms</c>.</param>
/// <param name="Min">The least it may be set to.</param>
/// <param name="Max">The most it may be set to.</param>
/// <param name="Description">What the limit does, in one sentence the UI shows as it is.</param>
internal sealed record AnalysisLimit(
    string Id,
    string Unit,
    long Min,
    long Max,
    string Description,
    Func<AnalysisSettings, long> Get,
    Func<AnalysisSettings, long, AnalysisSettings> Set);
