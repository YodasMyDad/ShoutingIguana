namespace ShoutingIguana.Sidecar.Library.Models;

/// <summary>
/// <c>summaries.json</c> in the state folder: the summaries read so far, so a launch doesn't read every crawl again. Only
/// the engine version that wrote it uses it: another may count differently.
/// </summary>
internal sealed record SummaryCacheFile(string EngineVersion, IReadOnlyList<PersistedSummary> Summaries);
