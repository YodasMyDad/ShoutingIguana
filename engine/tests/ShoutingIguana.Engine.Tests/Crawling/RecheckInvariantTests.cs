using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>
/// The fix loop's promise: a recheck never says "fixed" where a full crawl says "still". Revision 0 of the synthetic site
/// is crawled, the site switched to revision 1, and every check a recheck can settle is rechecked whole on its own copy of
/// the crawl: each URL it judged must come out as a full crawl of revision 1 finds it. One that crawl flags is still (or
/// not checked, when the recheck couldn't read it); one it doesn't flag is fixed, or not checked where the recheck can't
/// know (a duplicate whose own text didn't change: a page it didn't fetch may share it now). Never fixed where the crawl
/// says still, and never still where it says fixed. Only the checks a recheck can't settle (orphan-page, no-https) are
/// left out, and slow-response, which a busy machine decides.
/// </summary>
[Collection(RecheckInvariantCollection.NAME)]
public sealed class RecheckInvariantTests(RecheckInvariantCrawls crawls)
{
    public static TheoryData<string> Checks() =>
    [
        .. IssueCatalog.All
            .Where(check => check.Recheck.Count > 0 && check.Id != SyntheticSiteExpectations.SLOW_RESPONSE)
            .Select(check => check.Id)
    ];

    [Theory]
    [MemberData(nameof(Checks))]
    public async Task ARecheckFindsWhatAFullCrawlFinds(string checkId)
    {
        var path = crawls.Copy(crawls.Then);
        var before = IssueReader.ReadAll(path, checkId).Select(issue => issue.Url).ToHashSet(StringComparer.Ordinal);
        if (before.Count == 0)
        {
            return;
        }

        var (_, recheck) = await RecheckCrawls.RecheckAsync(path, new RecheckAsk(checkId));

        var truth = crawls.Server.Site.GetExpectations().IssueUrls.GetValueOrDefault(checkId) ?? new HashSet<string>();
        var outcomes = RecheckReader.ReadRows(path, recheck.Id, null, [RecheckRole.Judged], 0, RecheckReader.MAX_LIMIT)!.Rows
            .ToDictionary(row => row.Url, row => row.Outcome, StringComparer.Ordinal);
        Assert.Equal(before.Order(StringComparer.Ordinal), outcomes.Keys.Order(StringComparer.Ordinal));

        var wrong = outcomes
            .Where(row => truth.Contains(row.Key)
                ? row.Value is not (RecheckOutcome.Still or RecheckOutcome.NotChecked)
                : row.Value is not (RecheckOutcome.Fixed or RecheckOutcome.NotChecked))
            .Select(row => $"{row.Key}: {row.Value}, where a full crawl of revision 1 says {(truth.Contains(row.Key) ? "still" : "fixed")}")
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.True(wrong.Count == 0, $"{checkId}: {string.Join("; ", wrong)}");
    }
}

/// <summary>The invariant's own crawl of revision 0, so its many rechecks run beside the other recheck tests.</summary>
public sealed class RecheckInvariantCrawls : IAsyncLifetime
{
    private readonly RecheckCrawls _crawls = new();

    public ShoutingIguana.TestSite.SyntheticSiteServer Server => _crawls.Server;

    public string Then => _crawls.Then;

    public string Copy(string path) => _crawls.Copy(path);

    public Task InitializeAsync() => _crawls.InitializeAsync();

    public Task DisposeAsync() => _crawls.DisposeAsync();
}

[CollectionDefinition(NAME)]
public sealed class RecheckInvariantCollection : ICollectionFixture<RecheckInvariantCrawls>
{
    public const string NAME = "A recheck finds what a full crawl finds";
}
