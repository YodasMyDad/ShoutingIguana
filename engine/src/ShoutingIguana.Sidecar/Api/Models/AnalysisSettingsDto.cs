namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>The limits every analysis runs with, and what each is.</summary>
/// <param name="Settings">Each limit's value by its name: the body a save takes.</param>
/// <param name="Limits">Every limit with its value, the engine's default, its unit, its range and what it does.</param>
/// <param name="IsDefault">Every limit is at the engine's value.</param>
internal sealed record AnalysisSettingsDto(IReadOnlyDictionary<string, long> Settings, IReadOnlyList<AnalysisLimitDto> Limits, bool IsDefault);
