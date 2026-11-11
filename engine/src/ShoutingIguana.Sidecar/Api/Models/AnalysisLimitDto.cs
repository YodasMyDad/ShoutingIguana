namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>One of the analysis's limits, for Settings.</summary>
/// <param name="Id">The limit's name, which the settings are keyed by.</param>
/// <param name="Value">What it's set to now.</param>
/// <param name="Default">The engine's value, which resetting it puts back.</param>
/// <param name="Unit"><c>characters</c>, <c>words</c>, <c>bits</c>, <c>bytes</c> or <c>ms</c>.</param>
/// <param name="Min">The least it may be set to.</param>
/// <param name="Max">The most it may be set to.</param>
/// <param name="Description">What it does, in a sentence to show as it is.</param>
internal sealed record AnalysisLimitDto(string Id, long Value, long Default, string Unit, long Min, long Max, string Description);
