using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A check that flagged a URL, as the last analysis stored it.</summary>
/// <param name="Severity">As the analysis stored it; null for one this engine doesn't know.</param>
/// <param name="Related">The other URL involved: the broken target, the duplicate, the canonical.</param>
public sealed record UrlIssue(string CheckId, IssueSeverity? Severity, string? Detail, UrlRef? Related);
