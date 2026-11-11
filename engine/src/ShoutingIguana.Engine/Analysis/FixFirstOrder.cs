using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Analysis;

/// <summary>
/// The checks that found something, in the order to fix them: the most severe first, then the most URLs flagged, then
/// catalog order. The one order for every host: the score's lines follow it, and what "fixing the first five" would
/// leave is worked out from it. A check the user has hidden in the app is still in it: hiding is the app's own
/// preference, which the engine doesn't know.
/// </summary>
public static class FixFirstOrder
{
    private static readonly Dictionary<string, int> CATALOG_ORDER =
        IssueCatalog.All.Select((check, index) => (check.Id, index)).ToDictionary(pair => pair.Id, pair => pair.index, StringComparer.Ordinal);

    /// <summary>The checks with a count above 0, in fix-first order.</summary>
    public static IReadOnlyList<IssueCount> Of(IEnumerable<IssueCount> checks) =>
    [
        .. checks.Where(check => check.Count > 0)
            .OrderBy(check => check.Issue.Severity)
            .ThenByDescending(check => check.Count)
            .ThenBy(check => CATALOG_ORDER.GetValueOrDefault(check.Issue.Id, int.MaxValue))
            .ThenBy(check => check.Issue.Id, StringComparer.Ordinal)
    ];

    /// <summary>
    /// The ids of the checks an analysis found something with, in fix-first order, from its stored counts and the catalog
    /// as it is now. A check the catalog doesn't know (a newer engine's) is left out, as it has no severity to place it by.
    /// </summary>
    /// <param name="counts">URLs flagged per check id, as <see cref="Storage.IssueReader.ReadCounts"/> gives them.</param>
    public static IReadOnlyList<string> Of(IReadOnlyDictionary<string, long> counts) =>
    [
        .. Of(IssueCatalog.All.Where(check => counts.ContainsKey(check.Id)).Select(check => new IssueCount(check, counts[check.Id])))
            .Select(check => check.Issue.Id)
    ];
}
