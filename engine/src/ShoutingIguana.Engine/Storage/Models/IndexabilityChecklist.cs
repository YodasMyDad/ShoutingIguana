using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>"Indexable, and why": the verdict, and each test that decides it.</summary>
/// <param name="Indexable">The verdict; null when it can't be told: never fetched, or a page only status-checked.</param>
/// <param name="Reason"><see cref="IndexabilityReason.None"/> when indexable, otherwise why not.</param>
/// <param name="JudgedBy">Whose verdict: the analysis's (from the URL summary), or the crawl's own without one.</param>
public sealed record IndexabilityChecklist(bool? Indexable, IndexabilityReason? Reason, IndexabilityJudge JudgedBy, IReadOnlyList<IndexabilityTest> Tests);
