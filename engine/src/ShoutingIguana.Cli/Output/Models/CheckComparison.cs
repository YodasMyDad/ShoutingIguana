using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Cli.Output.Models;

/// <summary>
/// One check compared with the other crawl, all of it, for <c>si compare --check</c>: its change, every row it flags now
/// with its change, every row it flagged then and doesn't now, and its causes then and now.
/// </summary>
/// <param name="Comparable">How far the two crawls can be compared.</param>
/// <param name="Check">Its change; null when it ran in neither crawl's analysis.</param>
/// <param name="Rows">What the check flags now, by importance.</param>
/// <param name="Cleared">What it flagged then and not now, in the other crawl's order by importance.</param>
/// <param name="Groups">Its causes now, largest first; none for a check that isn't grouped.</param>
/// <param name="FixedGroups">Its causes then with no URL now, largest first.</param>
internal sealed record CheckComparison(
    Comparability Comparable,
    CheckChange? Check,
    IReadOnlyList<ComparedRow> Rows,
    IReadOnlyList<ClearedIssue> Cleared,
    IReadOnlyList<ComparedGroup> Groups,
    IReadOnlyList<ClearedGroup> FixedGroups);
