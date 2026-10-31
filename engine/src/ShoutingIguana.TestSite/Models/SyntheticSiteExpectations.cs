namespace ShoutingIguana.TestSite.Models;

/// <summary>What a correct crawl of a <see cref="SyntheticSite"/> must find.</summary>
/// <param name="HtmlPages">Internal 200 HTML pages the crawler parses: home, products, categories, static pages, sitemap-only orphans and plant pages.</param>
/// <param name="AnalysedPages">HTML pages the analysis audits: all of them except Cloudflare-style /cdn-cgi/ endpoints.</param>
/// <param name="BrokenUrls">Distinct internal URLs that return 404.</param>
/// <param name="Redirects">Distinct redirecting URLs (single hops, both hops of each chain, and the redirect plants).</param>
/// <param name="RobotsDisallowed">Distinct URLs blocked by robots.txt.</param>
/// <param name="NofollowOnly">Distinct URLs reachable only through nofollow links, so never fetched.</param>
/// <param name="DuplicateTitlePages">Indexable pages whose title is shared with another indexable page.</param>
/// <param name="IssueUrls">
/// The exact URLs each issue id must flag after analysis. Every other issue must flag nothing. slow-response lists
/// only the planted slow page, because a busy machine can make other pages slow too: the comparison adds every page
/// whose stored response time is over the threshold.
/// </param>
public sealed record SyntheticSiteExpectations(
    int HtmlPages,
    int AnalysedPages,
    int Products,
    int Orphans,
    int BrokenUrls,
    int ServerErrorUrls,
    int Redirects,
    int RobotsDisallowed,
    int NofollowOnly,
    int ExternalOk,
    int ExternalMissing,
    int Images,
    int Noindex,
    int Canonicalised,
    int DuplicateTitlePages,
    int MissingDescriptions,
    int ImagesMissingAlt,
    IReadOnlyDictionary<string, IReadOnlySet<string>> IssueUrls)
{
    public const string SLOW_RESPONSE = "slow-response";

    /// <summary>Of <see cref="NofollowOnly"/>, the URLs that are files (images) rather than HTML pages.</summary>
    public int NofollowOnlyFiles { get; init; }

    private const int LISTED_URLS = 5;

    /// <summary>How many URLs an issue must flag (slow-response: at least this many).</summary>
    public int Count(string issueId) => IssueUrls.TryGetValue(issueId, out var urls) ? urls.Count : 0;

    /// <summary>Differences between an analysis and these expectations, one line per issue, naming the URLs involved.</summary>
    /// <param name="found">Every (issue id, URL) pair the analysis stored.</param>
    /// <param name="allIssueIds">Every issue the analysis can report, so issues that weren't planted are checked for nothing flagged.</param>
    /// <param name="slowPages">Audited pages whose stored response time is over the slow-response threshold.</param>
    public IReadOnlyList<string> CompareIssues(
        IEnumerable<(string Issue, string Url)> found,
        IEnumerable<string> allIssueIds,
        IReadOnlyCollection<string> slowPages)
    {
        var known = allIssueIds.ToHashSet(StringComparer.Ordinal);
        var flagged = found
            .GroupBy(row => row.Issue, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(row => row.Url).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);

        List<string> mismatches = [.. IssueUrls.Keys.Where(id => !known.Contains(id)).Select(id => $"Expected issue '{id}' isn't in the catalog")];
        mismatches.AddRange(flagged.Keys.Where(id => !known.Contains(id)).Select(id => $"Found issue '{id}', which isn't in the catalog"));
        foreach (var id in known.Order(StringComparer.Ordinal))
        {
            var expected = IssueUrls.TryGetValue(id, out var urls) ? urls.ToHashSet(StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal);
            if (id == SLOW_RESPONSE)
            {
                expected.UnionWith(slowPages);
            }

            var actual = flagged.GetValueOrDefault(id) ?? [];
            var missing = expected.Where(url => !actual.Contains(url)).Order(StringComparer.Ordinal).ToList();
            var unexpected = actual.Where(url => !expected.Contains(url)).Order(StringComparer.Ordinal).ToList();
            if (missing.Count > 0 || unexpected.Count > 0)
            {
                mismatches.Add($"{id}: expected {expected.Count:N0}, found {actual.Count:N0}{List("missing", missing)}{List("unexpected", unexpected)}");
            }
        }

        return mismatches;
    }

    private static string List(string label, IReadOnlyList<string> urls) => urls.Count == 0
        ? string.Empty
        : $"; {label} {string.Join(", ", urls.Take(LISTED_URLS))}{(urls.Count > LISTED_URLS ? $" and {urls.Count - LISTED_URLS:N0} more" : string.Empty)}";
}
