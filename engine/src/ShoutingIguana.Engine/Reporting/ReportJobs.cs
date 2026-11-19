using System.Globalization;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Reporting.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Reporting;

/// <summary>
/// Related problems as one job, as Simple's Summary and What to fix have them (docs/v2-ui-simple.md, "Related problems
/// are one job"; the app's <c>simple/fixRows.ts</c>): a problem a fix of another of the same severity clears entirely
/// (the analysis's clears, <c>All</c>) is folded under that one, a 404 and the links to it being one job. The first in
/// the order to fix that clears it takes it, and one level only: a problem that has others folded under it isn't folded
/// itself.
/// <para>
/// Only the jobs are numbered, from 1 in the order to fix them, each followed by the problems folded under it, which
/// have no number of their own. A job's sheet in the workbook carries its number ("2 …"), and a folded problem's its
/// job's number with a letter ("2a …"), so the report can say where each full list is.
/// </para>
/// </summary>
internal static class ReportJobs
{
    /// <summary>
    /// The checks as the report lists them, from <paramref name="order"/> (the order to fix them, each with its severity
    /// and what a fix of it clears): the jobs, each followed by the checks folded under it. Each has its job's number,
    /// its sheet's label, and for a folded one the id of its job.
    /// </summary>
    public static List<(string Id, int Number, string Sheet, string? Along)> Listed(IReadOnlyList<(string Id, IssueSeverity Severity, IReadOnlyList<ClearedCheck> Clears)> order)
    {
        var foldedUnder = FoldedUnder(order);
        List<(string Id, int Number, string Sheet, string? Along)> listed = [];
        var number = 0;
        foreach (var job in order.Where(check => !foldedUnder.ContainsKey(check.Id)))
        {
            number++;
            var label = number.ToString(CultureInfo.InvariantCulture);
            listed.Add((job.Id, number, label, null));
            var letter = 'a';
            foreach (var folded in order.Where(check => foldedUnder.GetValueOrDefault(check.Id) == job.Id))
            {
                listed.Add((folded.Id, number, $"{label}{letter++}", job.Id));
            }
        }

        return listed;
    }

    /// <summary>
    /// The problems as the report lists them, as the checks are listed above, from <paramref name="problems"/> in the
    /// order to fix them, each keeping its place in that order as its <see cref="ReportProblem.Rank"/>.
    /// </summary>
    public static List<ReportProblem> Listed(IReadOnlyList<ReportProblem> problems)
    {
        var byId = problems.Select((problem, index) => (problem, Rank: index + 1)).ToDictionary(each => each.problem.Check.Id, StringComparer.Ordinal);
        return
        [
            .. Listed([.. problems.Select(problem => (problem.Check.Id, problem.Check.Severity, problem.Clears))])
                .Select(each => byId[each.Id].problem with { Number = each.Number, Sheet = each.Sheet, Along = each.Along, Rank = byId[each.Id].Rank })
        ];
    }

    /// <summary>
    /// The checks folded under another among <paramref name="checks"/> (in the order to fix them), by id, each with the
    /// id of the check it's folded under.
    /// </summary>
    private static Dictionary<string, string> FoldedUnder(IReadOnlyList<(string Id, IssueSeverity Severity, IReadOnlyList<ClearedCheck> Clears)> checks)
    {
        Dictionary<string, string> foldedUnder = new(StringComparer.Ordinal);
        foreach (var part in checks.GroupBy(check => check.Severity))
        {
            var inPart = part.Select(check => check.Id).ToHashSet(StringComparer.Ordinal);
            HashSet<string> parents = new(StringComparer.Ordinal);
            foreach (var check in part)
            {
                if (foldedUnder.ContainsKey(check.Id))
                {
                    continue;
                }

                foreach (var cleared in check.Clears)
                {
                    var other = cleared.CheckId;
                    if (!cleared.All || !inPart.Contains(other) || other == check.Id || foldedUnder.ContainsKey(other) || parents.Contains(other))
                    {
                        continue;
                    }

                    foldedUnder[other] = check.Id;
                    parents.Add(check.Id);
                }
            }
        }

        return foldedUnder;
    }
}
