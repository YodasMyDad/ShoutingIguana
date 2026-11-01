namespace ShoutingIguana.Sidecar.Library.Models;

/// <summary>
/// <c>summaries.json</c> in the state folder: the summaries read so far, so a launch doesn't read every crawl again. Only
/// the engine version that wrote it uses it, and only in the format it reads: another may count differently, or keep
/// less of each summary. A file from before the format was written reads as format 0.
/// </summary>
internal sealed record SummaryCacheFile(string EngineVersion, int Format, IReadOnlyList<PersistedSummary> Summaries);
