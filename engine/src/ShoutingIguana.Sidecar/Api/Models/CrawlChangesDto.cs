namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>
/// A library item's changes since its previous crawl (the fix loop), at the check level, from the two crawls' cached
/// summaries: no file is opened. Every check is in it, hidden or not, each in this crawl's fix-first order, then catalog order.
/// </summary>
/// <param name="Score">The score now less then; null when either has none, or they were scored by different versions.</param>
/// <param name="Partial">Either crawl didn't read all it set out to: the change is "so far".</param>
/// <param name="Fixed">Checks that found something then and nothing now.</param>
/// <param name="New">Checks that ran then and found nothing, and find something now.</param>
/// <param name="NewCheck">Checks that didn't run then.</param>
/// <param name="NotCheckedNow">Checks that ran then and were skipped now.</param>
internal sealed record CrawlChangesDto(
    int? Score,
    bool Partial,
    IReadOnlyList<string> Fixed,
    IReadOnlyList<string> New,
    IReadOnlyList<string> Fewer,
    IReadOnlyList<string> More,
    IReadOnlyList<string> NewCheck,
    IReadOnlyList<string> NotCheckedNow);
