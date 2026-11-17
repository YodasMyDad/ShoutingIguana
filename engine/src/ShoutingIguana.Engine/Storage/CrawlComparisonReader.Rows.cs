using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Storage;

/// <summary>The comparison by URL: one check's rows joined across the two files, and the change of each row on a page.</summary>
public static partial class CrawlComparisonReader
{
    private const string CHECK_SKIPPED = "This check didn't run in this crawl's analysis.";

    /// <summary>
    /// One check's rows joined across the two files, in one streamed pass over each: the rows then go into a set by URL
    /// key, those now are streamed against it, and the URLs flagged on one side only are looked up on the other by their
    /// text (an index seek each) to say whether that crawl judged them.
    /// </summary>
    internal static CheckPass RowPass(GridReadContext now, GridReadContext then, ComparisonPair pair, string checkId)
    {
        var (ranThen, ranNow) = (pair.Then.Ran(checkId), pair.Now.Ran(checkId));
        List<ClearedRow> cleared = [];
        if (!ranThen)
        {
            // It didn't run then, so nothing it flags now can be compared, and nothing it flagged then is known.
            return new CheckPass(RowSplit.None with { NotComparedThen = pair.Now.Count(checkId) ?? 0 }, cleared);
        }

        using var lookupNow = new ComparisonLookup(now);
        if (!ranNow)
        {
            // It ran then and not now: whatever it flagged then wasn't checked this time.
            var skipped = new NotCheckedReason(NotCheckedCode.CheckSkipped,
                pair.Now.Results.Skipped.FirstOrDefault(check => check.CheckId == checkId)?.Text ?? CHECK_SKIPPED);
            foreach (var (thenId, url) in Flagged(then, checkId))
            {
                cleared.Add(new ClearedRow(thenId, lookupNow.Find(url) is { Reading: not UrlReading.NotFound } found ? found.Id : null, RowChangeState.NotChecked, skipped));
            }

            return new CheckPass(RowSplit.None with { NotChecked = cleared.Count }, cleared);
        }

        Dictionary<UInt128, int> previous = new((int)Math.Min(pair.Then.Count(checkId) ?? 0, int.MaxValue));
        foreach (var (thenId, url) in Flagged(then, checkId))
        {
            previous[UrlKey.Compute(url)] = thenId;
        }

        var judging = JudgingOf(checkId);
        using var lookupThen = new ComparisonLookup(then);
        long still = 0, @new = 0, newUrl = 0, notComparedThen = 0;
        foreach (var (_, url) in Flagged(now, checkId))
        {
            if (previous.Remove(UrlKey.Compute(url)))
            {
                still++;
                continue;
            }

            switch (FlaggedNowOnly(lookupThen.Find(url), judging))
            {
                case RowChangeState.New:
                    @new++;
                    break;
                case RowChangeState.NewUrl:
                    newUrl++;
                    break;
                default:
                    notComparedThen++;
                    break;
            }
        }

        // What's left was flagged then and not now: found again, read, or not.
        foreach (var thenId in previous.Values)
        {
            var url = lookupThen.TextOf(thenId) ?? throw new InvalidOperationException($"The crawl's issue names URL {thenId}, which it doesn't have.");
            cleared.Add(FlaggedThenOnly(thenId, lookupNow.Find(url), judging));
        }

        var split = RowSplit.Of(cleared.Select(row => row.State)) with { Still = still, New = @new, NewUrl = newUrl, NotComparedThen = notComparedThen };
        return new CheckPass(split, cleared);
    }

    /// <summary>A row flagged now and not then: the other crawl didn't find the URL, judged it and didn't flag it, or didn't judge it.</summary>
    private static RowChangeState FlaggedNowOnly(FoundUrl? then, UrlJudging judging) =>
        then is not { Reading: not UrlReading.NotFound } found ? RowChangeState.NewUrl
        : CheckJudging.Judges(judging, found.Reading) ? RowChangeState.New
        : RowChangeState.NotComparedThen;

    /// <summary>A row flagged then and not now: this crawl didn't find the URL, judged it and didn't flag it, or didn't judge it, and why.</summary>
    private static ClearedRow FlaggedThenOnly(int thenId, FoundUrl? now, UrlJudging judging) =>
        now is not { Reading: not UrlReading.NotFound } found ? new ClearedRow(thenId, null, RowChangeState.Gone, null)
        : CheckJudging.Judges(judging, found.Reading) ? new ClearedRow(thenId, found.Id, RowChangeState.Fixed, null)
        : new ClearedRow(thenId, found.Id, RowChangeState.NotChecked, ReasonOf(found));

    /// <summary>
    /// The change of each URL a check flags now, asked about by its id in this crawl: its text, then the URL in the other
    /// crawl by that text, then the check's issue on it there. A few index seeks each, so a page of rows costs little.
    /// </summary>
    internal static RowChanges? RowChangesOf(GridReadContext now, GridReadContext then, string checkId, IReadOnlyList<int> urlIds)
    {
        if (Pair(now, then) is not { } pair)
        {
            return null;
        }

        var compared = new PageComparison(pair.Verdict.Comparable);
        if (pair.CountsOnly || !pair.Now.Ran(checkId))
        {
            return new RowChanges(compared, [.. urlIds.Select(_ => (RowChange?)null)]);
        }

        var judging = JudgingOf(checkId);
        var ranThen = pair.Then.Ran(checkId);
        using var lookupNow = new ComparisonLookup(now);
        using var lookupThen = new ComparisonLookup(then);
        List<RowChange?> changes = [];
        foreach (var urlId in urlIds)
        {
            if (!lookupNow.IssueOn(checkId, urlId).Flagged || lookupNow.TextOf(urlId) is not { } url)
            {
                changes.Add(null);
                continue;
            }

            if (!ranThen)
            {
                changes.Add(new RowChange(RowChangeState.NotComparedThen));
                continue;
            }

            var found = lookupThen.Find(url);
            var issueThen = found is { } known ? lookupThen.IssueOn(checkId, known.Id) : (false, null);
            changes.Add(issueThen.Flagged ? new RowChange(RowChangeState.Still, issueThen.Detail) : new RowChange(FlaggedNowOnly(found, judging)));
        }

        return new RowChanges(compared, changes);
    }

    /// <summary>The URLs a check flagged in one file, by id and text, in id order (the issues' key).</summary>
    private static IEnumerable<(int Id, string Url)> Flagged(GridReadContext context, string checkId)
    {
        using var command = context.Command("SELECT i.url_id, u.url FROM issues i JOIN urls u ON u.id = i.url_id WHERE i.issue = $check ORDER BY i.url_id",
            [new("$check", checkId)]);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            yield return (reader.GetInt32(0), reader.GetString(1));
        }
    }

    /// <summary>Why this crawl didn't judge a URL it found, in its code and a few words of what the URL does now.</summary>
    private static NotCheckedReason ReasonOf(FoundUrl url) => url switch
    {
        { Outcome: FetchOutcome.RobotsDisallowed } => new NotCheckedReason(NotCheckedCode.Robots, "robots.txt refuses it"),
        { Error: { } error } when error.StartsWith(RetryingFetcher.RATE_LIMITED_ERROR, StringComparison.Ordinal) => new NotCheckedReason(NotCheckedCode.RateLimited, error),
        { Reading: UrlReading.NotRead, Error: { } error } when error.StartsWith(RetryingFetcher.NOT_CHECKED_ERROR, StringComparison.Ordinal) =>
            new NotCheckedReason(NotCheckedCode.NotRead, error),
        { Reading: UrlReading.NotRead } => new NotCheckedReason(NotCheckedCode.NotRead, NotReadText(url)),
        { Outcome: FetchOutcome.Blocked } => new NotCheckedReason(NotCheckedCode.Blocked, url.BlockReason ?? "A firewall or bot protection answered in its place"),
        { Reading: UrlReading.Failed } => new NotCheckedReason(NotCheckedCode.Failed, url.Error ?? url.Outcome switch
        {
            FetchOutcome.Timeout => "Timed out",
            FetchOutcome.NetworkError => "Connection failed",
            _ => "The crawler couldn't process the response"
        }),
        { Outcome: FetchOutcome.Redirect, StatusCode: { } status } => new NotCheckedReason(NotCheckedCode.Redirects, HttpStatusNames.Describe(status)),
        { StatusCode: { } status and not 200 } => new NotCheckedReason(NotCheckedCode.Failed, HttpStatusNames.Describe(status)),
        _ => new NotCheckedReason(NotCheckedCode.Failed, "It answered, but not with a page of the site")
    };

    private static string NotReadText(FoundUrl url) => url.State != UrlState.Skipped
        ? "Not reached before the crawl stopped"
        : url.SkipReason switch
        {
            SkipReason.NoFollow => "Only nofollow links lead to it",
            SkipReason.NotFollowedByOption => "Links of its kind weren't followed",
            SkipReason.ResourcesDisabled => "Resources weren't checked",
            SkipReason.ExternalsDisabled => "External links weren't checked",
            SkipReason.DepthLimit => "Past the depth limit",
            SkipReason.UrlLimit => "Past the URL limit",
            SkipReason.Recheck => "Found by a recheck, and not fetched",
            _ => "Not fetched"
        };
}
