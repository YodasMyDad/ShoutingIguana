namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// One analysis's checks of the site's own files with the files each is about (<see cref="IssueFileReader"/>), with when it
/// ran, so a host that read the analysis apart can tell it's the same one before putting the two together.
/// </summary>
/// <param name="AnalyzedAt">When the analysis these come from ran: its <c>AnalysisInfo.AnalyzedAt</c>.</param>
/// <param name="Checks">Each such check that found something, by id, with its files in the order its detail names them.</param>
public sealed record AnalysisFiles(DateTimeOffset AnalyzedAt, IReadOnlyDictionary<string, IReadOnlyList<IssueFile>> Checks);
