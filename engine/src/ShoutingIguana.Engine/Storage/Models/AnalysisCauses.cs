namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// One analysis's checks as work to do (<see cref="IssueCauseReader"/>), with when it ran, so a host that read the analysis
/// apart can tell it's the same one before putting the two together.
/// </summary>
/// <param name="AnalyzedAt">When the analysis these come from ran: its <c>AnalysisInfo.AnalyzedAt</c>.</param>
/// <param name="Checks">Every check of the catalog that found something, by id.</param>
public sealed record AnalysisCauses(DateTimeOffset AnalyzedAt, IReadOnlyDictionary<string, CheckCauses> Checks);
