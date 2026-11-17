using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Support;

/// <summary>
/// Every row of a comparison as the readers serve it: each row a check flags now with its change (its rows read with the
/// other crawl to compare with), and each row it flagged then and not now (what /fixed lists), so the whole comparison
/// can be held to the synthetic site's expected one, as the issues table is held to its expectations.
/// </summary>
internal static class ComparisonRows
{
    private const int PAGE = UrlGridReader.MAX_LIMIT;

    /// <summary>Each row of every check that ran in either crawl: its URL, its id in the crawl that has it flagged, and its state.</summary>
    public static List<(string CheckId, string Url, RowChangeState State)> Read(string now, string then, CrawlComparison summary)
    {
        List<(string, string, RowChangeState)> rows = [];
        foreach (var check in summary.Checks)
        {
            for (var offset = 0; ; offset += PAGE)
            {
                var page = IssueGroupReader.ReadRows(now, check.CheckId, new UrlQuery { Columns = ["url"], Offset = offset, Limit = PAGE }, null, then);
                if (page?.Page is not { } urls)
                {
                    break;
                }

                rows.AddRange(urls.Rows.Select((row, index) => (check.CheckId, (string)row.Values[1]!, page.Changes![index]!.State)));
                if (urls.Rows.Count < PAGE)
                {
                    break;
                }
            }

            for (var offset = 0; ; offset += PAGE)
            {
                var cleared = CrawlComparisonReader.ReadCleared(now, then, check.CheckId, new ClearedQuery(Offset: offset, Limit: PAGE))!;
                rows.AddRange(cleared.Rows.Select(row => (check.CheckId, row.Url, row.State)));
                if (cleared.Rows.Count < PAGE)
                {
                    break;
                }
            }
        }

        return rows;
    }

    /// <summary>
    /// How a comparison differs from what the site expects, one line per check, and where a check's row split doesn't add up
    /// to its rows: empty when it's exact.
    /// </summary>
    public static List<string> Mismatches(string now, string then, SyntheticSiteChanges expected)
    {
        var summary = CrawlComparisonReader.ReadSummary(now, then)!;
        var rows = Read(now, then, summary);
        List<string> mismatches =
        [
            .. expected.Compare(
                summary.Checks.Select(check => (check.CheckId, check.State.ToString(), (int?)check.Then, (int?)check.Now)),
                rows.Select(row => (row.CheckId, row.Url, row.State.ToString())),
                IssueCatalog.All.Select(check => check.Id))
        ];

        // The summary's split is the rows the grids and /fixed list, state by state.
        var byCheck = rows.ToLookup(row => row.CheckId, StringComparer.Ordinal);
        foreach (var check in summary.Checks)
        {
            var counted = RowSplit.Of(byCheck[check.CheckId].Select(row => row.State));
            if (counted != check.Rows)
            {
                mismatches.Add($"{check.CheckId}: the summary splits its rows {check.Rows}, the rows listed are {counted}");
            }
        }

        return mismatches;
    }
}
