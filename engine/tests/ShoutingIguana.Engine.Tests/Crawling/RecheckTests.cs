using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>
/// The fix loop's "Recheck these N pages": the synthetic site crawled at revision 0, then switched to revision 1, and each
/// check revision 1 changes rechecked on a copy of that crawl, to its exact result.
/// </summary>
[Collection(RecheckCrawlsCollection.NAME)]
public sealed class RecheckTests(RecheckCrawls crawls)
{
    /// <summary>Checks a pass over every page decides from many pages at once: a recheck settles them against the pages as stored.</summary>
    private static readonly HashSet<string> ACROSS_PAGES =
    [
        "title-duplicate", "description-duplicate", "h1-duplicate", "duplicate-content", "near-duplicate-content", "orphan-page", "not-in-sitemap",
        "site-hidden-from-search", "slow-response"
    ];

    /// <summary>
    /// Each check revision 1 changes, rechecked alone: its result is exactly what fetching its URLs again gives. A URL the
    /// check still flags is still; one no longer linked (its referring page, fetched again because the URL still failed,
    /// dropped the link) is let go and fixed; one read again in a way the check can judge and not flagged is fixed; and
    /// one that answers no longer as the check needs (a page that is now an error) wasn't checked, never fixed. URLs in its
    /// scope newly flagged are new: the broken links moved to another missing URL, fetched one step.
    /// </summary>
    [Theory]
    [InlineData("internal-client-error", 2)]
    [InlineData("broken-internal-links", 0)]
    [InlineData("internal-server-error", 0)]
    [InlineData("title-too-long", 0)]
    [InlineData("title-too-short", 0)]
    [InlineData("h1-multiple", 0)]
    [InlineData("noindex-page", 0)]
    [InlineData("image-too-large", 0)]
    [InlineData("redirect-chain", 0)]
    [InlineData("hreflang-missing-return", 0)]
    [InlineData("blocked-by-robots", 0)]
    public async Task EachCheckRevisionOneChangesIsRecheckedToItsExactResult(string checkId, int newInScope)
    {
        var path = crawls.Copy(crawls.Then);
        var before = IssueReader.ReadAll(path, checkId).Select(issue => issue.Url).ToHashSet(StringComparer.Ordinal);
        var scoreBefore = IssueReader.ReadResults(path)!.Score?.Value;
        var rows = IssueReader.ReadAll(path).Select(issue => (issue.Issue, issue.Url)).ToHashSet();

        var (run, recheck) = await RecheckCrawls.RecheckAsync(path, new RecheckAsk(checkId));

        Assert.Equal(CrawlStatus.Completed, run.Status);
        var expected = Expected(crawls.Server.Site, checkId, before);
        Assert.Equal(
            (RecheckStatus.Completed, before.Count, (long?)before.Count, expected.Fixed, expected.Still, expected.NotChecked, 0, newInScope),
            (recheck.Status, recheck.Judged, recheck.CountBefore, recheck.Result!.Fixed, recheck.Result.Still, recheck.Result.NotChecked, recheck.Result.NotRechecked,
                recheck.Result.New));
        Assert.Equal((scoreBefore, IssueReader.ReadResults(path)!.Score?.Value), (recheck.Result.ScoreBefore, recheck.Result.ScoreAfter));

        // The check's issues on the URLs it judged are revision 1's (bar those it couldn't judge), and every other page's
        // issues of a check of the page alone are as revision 0's crawl found them.
        var scope = RecheckReader.ReadRows(path, recheck.Id, null, null, 0, RecheckReader.MAX_LIMIT)!.Rows.Select(row => row.Url).ToHashSet(StringComparer.Ordinal);
        var after = IssueReader.ReadAll(path).Select(issue => (issue.Issue, issue.Url)).ToHashSet();
        var pageAlone = IssueCatalog.All.Where(check => check.References == IssueReferences.None && !ACROSS_PAGES.Contains(check.Id)).Select(check => check.Id).ToHashSet();
        Assert.Equal(
            rows.Where(row => pageAlone.Contains(row.Issue) && !scope.Contains(row.Url)).Order(),
            after.Where(row => pageAlone.Contains(row.Issue) && !scope.Contains(row.Url)).Order());
        Assert.Equal(expected.StillUrls.Order(StringComparer.Ordinal),
            after.Where(row => row.Issue == checkId && before.Contains(row.Url)).Select(row => row.Url).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// What fetching the check's URLs again at revision 1 gives each: still flagged, fixed, or not checked. A URL a check of
    /// what others refer to flagged that nothing leads to now is let go, and fixed.
    /// </summary>
    private static (int Fixed, int Still, int NotChecked, IReadOnlyList<string> StillUrls) Expected(SyntheticSite after, string checkId, IReadOnlySet<string> judged)
    {
        var check = IssueCatalog.Find(checkId)!;
        var flagged = after.GetExpectations().IssueUrls.GetValueOrDefault(checkId) ?? new HashSet<string>();
        List<string> still = [];
        int fixedCount = 0, notChecked = 0;
        foreach (var url in judged)
        {
            if (flagged.Contains(url))
            {
                still.Add(url);
            }
            else if (check.References == IssueReferences.Sources && after.Fate(url) == SyntheticUrlFate.NotFound ||
                     Judges(ExpectedJudging.Of(checkId), after.FetchFate(url)))
            {
                fixedCount++;
            }
            else
            {
                notChecked++;
            }
        }

        return (fixedCount, still.Count, notChecked, still);
    }

    private static bool Judges(SyntheticJudging judging, SyntheticUrlFate fate) => judging switch
    {
        SyntheticJudging.Responses => fate is SyntheticUrlFate.Page or SyntheticUrlFate.Answered or SyntheticUrlFate.Failed,
        SyntheticJudging.Urls => fate is SyntheticUrlFate.Page or SyntheticUrlFate.Answered,
        _ => fate is SyntheticUrlFate.Page
    };
}
