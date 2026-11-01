namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// The last analysis as one snapshot: when it ran, the URLs each check flagged (0 for a check that ran and found
/// nothing; a check it skipped is missing), and what it couldn't do.
/// </summary>
public sealed record AnalysisResults(AnalysisInfo Analysis, IReadOnlyDictionary<string, long> Counts, IReadOnlyList<string> Notes);
