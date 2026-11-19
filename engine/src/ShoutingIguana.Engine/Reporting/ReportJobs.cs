using ShoutingIguana.Engine.Reporting.Models;

namespace ShoutingIguana.Engine.Reporting;

/// <summary>
/// Related problems as one job, as Simple's Summary and What to fix have them (docs/v2-ui-simple.md, "Related problems
/// are one job"; the app's <c>simple/fixRows.ts</c>): a problem a fix of another of the same severity clears entirely
/// (the analysis's clears, <c>All</c>) is folded under that one, a 404 and the links to it being one job. The first in
/// the order to fix that clears it takes it, and one level only: a problem that has others folded under it isn't folded
/// itself.
/// </summary>
internal static class ReportJobs
{
    /// <summary>
    /// The problems folded under another among <paramref name="problems"/> (in the order to fix them), by check id, each
    /// with the problem it's folded under.
    /// </summary>
    public static IReadOnlyDictionary<string, ReportProblem> FoldedUnder(IReadOnlyList<ReportProblem> problems)
    {
        Dictionary<string, ReportProblem> foldedUnder = new(StringComparer.Ordinal);
        foreach (var part in problems.GroupBy(problem => problem.Check.Severity))
        {
            var inPart = part.ToDictionary(problem => problem.Check.Id, StringComparer.Ordinal);
            HashSet<string> parents = new(StringComparer.Ordinal);
            foreach (var problem in part)
            {
                if (foldedUnder.ContainsKey(problem.Check.Id))
                {
                    continue;
                }

                foreach (var cleared in problem.Clears)
                {
                    if (!cleared.All || !inPart.TryGetValue(cleared.CheckId, out var other) || other == problem
                        || foldedUnder.ContainsKey(other.Check.Id) || parents.Contains(other.Check.Id))
                    {
                        continue;
                    }

                    foldedUnder[other.Check.Id] = problem;
                    parents.Add(problem.Check.Id);
                }
            }
        }

        return foldedUnder;
    }

    /// <summary>
    /// The problems as jobs, in the order to fix them, each followed by those folded under it: what "Fix these first"
    /// lists, the folded ones marked as fixed along with the job above.
    /// </summary>
    public static IEnumerable<(ReportProblem Problem, bool Folded)> InJobs(IReadOnlyList<ReportProblem> problems,
        IReadOnlyDictionary<string, ReportProblem> foldedUnder) =>
        problems.Where(problem => !foldedUnder.ContainsKey(problem.Check.Id)).SelectMany(job =>
            problems.Where(problem => foldedUnder.GetValueOrDefault(problem.Check.Id) == job).Select(problem => (problem, true)).Prepend((job, false)));
}
