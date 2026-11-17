using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// The comparison by cause: a check's groups matched across the two files as its grouping says. A cause grouped by the
/// other URL is matched by that URL's text, one grouped by detail by the detail (or the fault) with the other URL's text,
/// and a duplicate cluster by the pages it shares: its name is its first page, which changes when that page is fixed.
/// </summary>
public static partial class CrawlComparisonReader
{
    /// <summary>The checks whose groups are clusters of pages sharing a text or the content, named by their first page.</summary>
    private static readonly HashSet<string> CLUSTERS = new(StringComparer.Ordinal)
    {
        IssueCatalog.TITLE_DUPLICATE.Id, IssueCatalog.DESCRIPTION_DUPLICATE.Id, IssueCatalog.H1_DUPLICATE.Id, IssueCatalog.DUPLICATE_CONTENT.Id,
        IssueCatalog.NEAR_DUPLICATE_CONTENT.Id
    };

    private static readonly Dictionary<(string?, string?), long> NO_CAUSES = [];
    private static readonly Dictionary<int, long> NO_CLUSTERS = [];

    /// <summary>The change of each group on a page of a check's groups, and how many causes then have no URL now.</summary>
    internal static GroupChanges? GroupChangesOf(GridReadContext now, GridReadContext then, string checkId, IReadOnlyList<IssueGroup> groups)
    {
        if (Pair(now, then) is not { } pair)
        {
            return null;
        }

        var compared = new PageComparison(pair.Verdict.Comparable);
        if (pair.CountsOnly || IssueCatalog.Find(checkId) is not { Grouping: not IssueGrouping.None } check || !pair.Then.Ran(checkId) || !pair.Now.Ran(checkId))
        {
            return new GroupChanges(compared, [.. groups.Select(_ => (GroupChange?)null)]);
        }

        var pass = GroupPassOf(now, then, pair, check);
        var cluster = CLUSTERS.Contains(check.Id);
        List<GroupChange?> changes =
        [
            .. groups.Select(group => (GroupChange?)ChangeOf(group.Pages, cluster
                ? group.RelatedUrlId is { } id ? pass.ClusterThen.GetValueOrDefault(id) : 0
                : pass.ThenByKey.GetValueOrDefault((group.Detail, group.RelatedUrl))))
        ];
        return new GroupChanges(compared with { FixedGroups = pass.Cleared.Count }, changes);
    }

    private static GroupChange ChangeOf(long now, long then) => new(
        then == 0 ? GroupChangeState.New : now < then ? GroupChangeState.Fewer : now > then ? GroupChangeState.More : GroupChangeState.Same,
        then);

    /// <summary>A check's causes matched across the two files, kept with the check's join. A check that didn't run now has no cause now.</summary>
    private static GroupPass GroupPassOf(GridReadContext now, GridReadContext then, ComparisonPair pair, IssueDefinition check) =>
        KeptLarge(now, then, $"groups|{check.Id}", () => CLUSTERS.Contains(check.Id)
            ? ClusterPass(now, then, check, pair.Now.Ran(check.Id))
            : CausePass(now, then, check, pair.Now.Ran(check.Id)));

    /// <summary>Causes by their detail and their other URL's text: the groups of each file, as the Issues screen counts them.</summary>
    private static GroupPass CausePass(GridReadContext now, GridReadContext then, IssueDefinition check, bool ranNow)
    {
        var groupsThen = Causes(then, check);
        HashSet<(string?, string?)> keysNow = ranNow ? [.. Causes(now, check).Select(group => (group.Detail, group.RelatedUrl))] : [];
        Dictionary<(string?, string?), long> byKey = [];
        foreach (var group in groupsThen)
        {
            byKey[(group.Detail, group.RelatedUrl)] = byKey.GetValueOrDefault((group.Detail, group.RelatedUrl)) + group.Pages;
        }

        List<ThenGroup> cleared =
        [
            .. groupsThen.Where(group => !keysNow.Contains((group.Detail, group.RelatedUrl)))
                .OrderByDescending(group => group.Pages).ThenBy(group => group.Detail, StringComparer.Ordinal).ThenBy(group => group.RelatedUrlId)
        ];
        return new GroupPass(byKey, NO_CLUSTERS, cleared);
    }

    /// <summary>
    /// Duplicate clusters by their pages: each cluster now matches the cluster then it shares the most pages with (the one
    /// named by the page first in that crawl, on a tie); a cluster then no cluster now matches has no URL now.
    /// </summary>
    private static GroupPass ClusterPass(GridReadContext now, GridReadContext then, IssueDefinition check, bool ranNow)
    {
        Dictionary<UInt128, int> clusterThen = [];
        Dictionary<int, long> sizesThen = [];
        foreach (var (cluster, url) in ClusterMembers(then, check))
        {
            clusterThen.TryAdd(UrlKey.Compute(url), cluster);
            sizesThen[cluster] = sizesThen.GetValueOrDefault(cluster) + 1;
        }

        Dictionary<(int Now, int Then), long> shared = [];
        if (ranNow)
        {
            foreach (var (cluster, url) in ClusterMembers(now, check))
            {
                if (clusterThen.TryGetValue(UrlKey.Compute(url), out var was))
                {
                    shared[(cluster, was)] = shared.GetValueOrDefault((cluster, was)) + 1;
                }
            }
        }

        var best = shared.GroupBy(pair => pair.Key.Now)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key.Then).First().Key.Then);
        var matched = best.Values.ToHashSet();
        using var lookup = new ComparisonLookup(then);
        List<ThenGroup> cleared =
        [
            .. sizesThen.Where(cluster => !matched.Contains(cluster.Key)).OrderByDescending(cluster => cluster.Value).ThenBy(cluster => cluster.Key)
                .Select(cluster => new ThenGroup(null, cluster.Key, lookup.TextOf(cluster.Key), cluster.Value))
        ];
        return new GroupPass(NO_CAUSES, best.ToDictionary(pair => pair.Key, pair => sizesThen[pair.Value]), cleared);
    }

    /// <summary>A file's groups of a check, each by its detail, its other URL by id and text, and its URLs, as the Issues screen counts them.</summary>
    private static List<ThenGroup> Causes(GridReadContext context, IssueDefinition check)
    {
        List<ThenGroup> groups = [];
        using var command = context.Command($"""
            SELECT g.detail, g.related_url_id, u.url, g.pages
            FROM ({IssueGroupReader.Counted(context, check, check.Grouping)}) g LEFT JOIN urls u ON u.id = g.related_url_id
            """, [new("$check", check.Id)]);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            groups.Add(new ThenGroup(
                reader.IsDBNull(0) ? null : reader.GetString(0),
                reader.IsDBNull(1) || reader.GetInt64(1) is < int.MinValue or > int.MaxValue ? null : reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetInt64(3)));
        }

        return groups;
    }

    /// <summary>A duplicate check's pages in one file, each with the cluster it's in (its first page's id there).</summary>
    private static IEnumerable<(int Cluster, string Url)> ClusterMembers(GridReadContext context, IssueDefinition check)
    {
        using var command = context.Command($"""
            SELECT m.related_url_id, u.url
            FROM ({IssueGroupReader.Members(context, check, IssueGrouping.RelatedUrl)}) m JOIN urls u ON u.id = m.url_id
            WHERE m.related_url_id IS NOT NULL
            """, [new("$check", check.Id)]);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            yield return (reader.GetInt32(0), reader.GetString(1));
        }
    }

    /// <summary>A file's issues of a check by the group each is in: its detail (or fault), its other URL's id, and the URL's id.</summary>
    private static IEnumerable<(string? Detail, int? RelatedUrlId, int UrlId)> Members(GridReadContext context, IssueDefinition check)
    {
        using var command = context.Command(IssueGroupReader.Members(context, check, check.Grouping), [new("$check", check.Id)]);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            yield return (
                reader.IsDBNull(0) ? null : reader.GetString(0),
                reader.IsDBNull(1) || reader.GetInt64(1) is < int.MinValue or > int.MaxValue ? null : reader.GetInt32(1),
                reader.GetInt32(2));
        }
    }
}
