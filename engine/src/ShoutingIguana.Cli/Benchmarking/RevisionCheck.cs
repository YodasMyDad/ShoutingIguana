using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Cli.Benchmarking;

/// <summary>
/// Holds a comparison of a crawl of the synthetic site's revision 1 with a crawl of its revision 0 to what the site planted
/// (<see cref="SyntheticSite.GetChanges"/>), exactly, as the benchmark holds a crawl to the site's issues: every check's
/// state and counts, every row's state as the grids (rows read with the other crawl to compare with) and /fixed (what a
/// check no longer flags) serve it, the summary's split of each check's rows, and two complete crawls compared like for like.
/// </summary>
internal static class RevisionCheck
{
    private const int PAGE = UrlGridReader.MAX_LIMIT;

    /// <summary>Each way the comparison differs from what the site planted; empty when it's exact.</summary>
    /// <param name="rows">How many rows were compared, flagged now or then.</param>
    public static List<string> Compare(string now, string then, SyntheticSite site, out long rows)
    {
        List<string> mismatches = [];
        var cache = new ReadCache();
        var summary = CrawlComparisonReader.ReadSummary(now, then, cache) ?? throw new InvalidOperationException("Both crawls must be analysed to be compared.");
        if (summary.Comparable != Comparability.Full || summary.Differences.Count > 0 || summary.PreviousNeedsAnalysis.Count > 0)
        {
            mismatches.Add($"Two complete crawls with the same options compare {summary.Comparable}, differing in " +
                           $"{string.Join(", ", summary.Differences.Select(difference => $"{difference.Code} {difference.Field}"))}; the other needs analysing for " +
                           $"{string.Join(", ", summary.PreviousNeedsAnalysis)}");
        }

        List<(string CheckId, string Url, RowChangeState State)> found = [];
        foreach (var check in summary.Checks)
        {
            List<RowChangeState> states = [];
            for (var offset = 0; ; offset += PAGE)
            {
                var page = IssueGroupReader.ReadRows(now, check.CheckId, new UrlQuery { Columns = ["url"], Offset = offset, Limit = PAGE }, null, then, cache);
                if (page?.Page is not { } urls)
                {
                    break;
                }

                for (var index = 0; index < urls.Rows.Count; index++)
                {
                    var state = page.Changes?[index]?.State ?? throw new InvalidOperationException($"{check.CheckId}: a row has no change.");
                    found.Add((check.CheckId, (string)urls.Rows[index].Values[1]!, state));
                    states.Add(state);
                }

                if (urls.Rows.Count < PAGE)
                {
                    break;
                }
            }

            for (var offset = 0; ; offset += PAGE)
            {
                var cleared = CrawlComparisonReader.ReadCleared(now, then, check.CheckId, new ClearedQuery(Offset: offset, Limit: PAGE), cache)!;
                found.AddRange(cleared.Rows.Select(row => (check.CheckId, row.Url, row.State)));
                states.AddRange(cleared.Rows.Select(row => row.State));
                if (cleared.Rows.Count < PAGE)
                {
                    break;
                }
            }

            if (RowSplit.Of(states) != check.Rows)
            {
                mismatches.Add($"{check.CheckId}: the summary splits its rows {check.Rows}, the rows listed are {RowSplit.Of(states)}");
            }
        }

        rows = found.Count;
        mismatches.AddRange(site.GetChanges(0, 1, Judging).Compare(
            summary.Checks.Select(check => (check.CheckId, check.State.ToString(), (int?)check.Then, (int?)check.Now)),
            found.Select(row => (row.CheckId, row.Url, row.State.ToString())),
            IssueCatalog.All.Select(check => check.Id)));
        return mismatches;
    }

    /// <summary>What each check needs of a URL to judge it, the engine's own rule (<see cref="CheckJudging"/>), by name.</summary>
    private static SyntheticJudging Judging(string checkId) =>
        IssueCatalog.Find(checkId) is { } check ? Enum.Parse<SyntheticJudging>(CheckJudging.Of(check).ToString()) : SyntheticJudging.Pages;
}
