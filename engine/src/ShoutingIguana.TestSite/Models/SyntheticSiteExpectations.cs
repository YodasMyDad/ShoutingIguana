namespace ShoutingIguana.TestSite.Models;

/// <summary>What a correct crawl of a <see cref="SyntheticSite"/> must find.</summary>
/// <param name="HtmlPages">Internal 200 HTML pages: home, products, categories, static pages, sitemap-only orphans and plant pages.</param>
/// <param name="BrokenUrls">Distinct internal URLs that return 404.</param>
/// <param name="Redirects">Distinct redirecting URLs (single hops, both hops of each chain, and the redirect plants).</param>
/// <param name="RobotsDisallowed">Distinct URLs blocked by robots.txt.</param>
/// <param name="NofollowOnly">Distinct URLs reachable only through nofollow links, so never fetched.</param>
/// <param name="DuplicateTitlePages">Indexable pages whose title is shared with another indexable page.</param>
/// <param name="Issues">
/// Flagged URLs per issue id after analysis. Every other issue must come out at 0, except slow-response, which
/// lists only the planted slow page because a busy machine can make others slow too.
/// </param>
public sealed record SyntheticSiteExpectations(
    int HtmlPages,
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
    IReadOnlyDictionary<string, int> Issues)
{
    /// <summary>Issues a busy machine can add to, so only the planted minimum is required.</summary>
    private static readonly HashSet<string> TIMING_DEPENDENT = new(StringComparer.Ordinal) { "slow-response" };

    /// <summary>Differences between an analysis and these expectations, one line per issue.</summary>
    /// <param name="allIssueIds">Every issue the analysis can report, so issues that weren't planted are checked for zero.</param>
    public IReadOnlyList<string> CompareIssues(IReadOnlyDictionary<string, long> found, IEnumerable<string> allIssueIds)
    {
        var known = allIssueIds.ToHashSet(StringComparer.Ordinal);
        List<string> mismatches = [.. Issues.Keys.Where(id => !known.Contains(id)).Select(id => $"Expected issue '{id}' isn't in the catalog")];
        foreach (var id in known.Order(StringComparer.Ordinal))
        {
            var expected = Issues.GetValueOrDefault(id);
            var actual = found.GetValueOrDefault(id);
            var atLeast = TIMING_DEPENDENT.Contains(id);
            if (atLeast ? actual < expected : actual != expected)
            {
                mismatches.Add($"{id}: expected {(atLeast ? "at least " : string.Empty)}{expected:N0}, found {actual:N0}");
            }
        }

        return mismatches;
    }
}
