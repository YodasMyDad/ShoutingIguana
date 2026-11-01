using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Cli.Benchmarking;

/// <summary>Compares a finished crawl of the synthetic site with what the site says must be found.</summary>
internal static class ExpectationCheck
{
    public static List<string> Compare(CrawlStats stats, SyntheticSiteExpectations expected)
    {
        List<string> mismatches = [];
        Check(mismatches, "HTML pages", expected.HtmlPages, stats.HtmlPageCount);
        Check(mismatches, "Sitemap-only pages", expected.Orphans, stats.UnlinkedPageCount);
        Check(mismatches, "Redirects", expected.Redirects, Get(stats.Outcomes, FetchOutcome.Redirect));
        Check(mismatches, "Robots-disallowed URLs", expected.RobotsDisallowed, Get(stats.Outcomes, FetchOutcome.RobotsDisallowed));
        Check(mismatches, "404 responses", expected.BrokenUrls + expected.ExternalMissing, Get(stats.StatusCodes, 404));
        Check(mismatches, "500 responses", expected.ServerErrorUrls, Get(stats.StatusCodes, 500));
        Check(mismatches, "Nofollow-only URLs", expected.NofollowOnly, Get(stats.SkipReasons, SkipReason.NoFollow));
        Check(mismatches, "Noindex pages", expected.Noindex, Get(stats.NonIndexableReasons, IndexabilityReason.Noindex));
        Check(mismatches, "Canonicalised pages", expected.Canonicalised, Get(stats.NonIndexableReasons, IndexabilityReason.Canonicalised));
        Check(mismatches, "Unreachable URLs", expected.Unreachable, Get(stats.NonIndexableReasons, IndexabilityReason.Unreachable));
        Check(mismatches, "URLs not checked", expected.NotChecked, stats.NotCheckedCount);
        return mismatches;
    }

    /// <summary>
    /// The URL summary the grids read agrees with the stats, which count from the crawl's own tables: a row for every URL,
    /// and the same indexability, judged as the analysis judged it, the crawler's probes of the site's other versions left
    /// out of both. Every page the summary calls indexable counts, read or not: a page that was only status-checked, whose
    /// HTML nobody read, must never pass for one, and URLs that weren't checked are no more unreachable than fine.
    /// </summary>
    public static List<string> CompareSummary(IEnumerable<UrlSummary> rows, CrawlStats stats)
    {
        long count = 0, indexablePages = 0;
        Dictionary<IndexabilityReason, long> nonIndexable = [];
        foreach (var row in rows)
        {
            count++;
            if (!row.IsInternal || row.Source == DiscoverySource.SiteVersion)
            {
                continue;
            }

            if (row is { IsIndexable: true, ResourceType: ResourceType.Page })
            {
                indexablePages++;
            }

            if (row.IndexabilityReason is { } reason and not IndexabilityReason.None)
            {
                nonIndexable[reason] = nonIndexable.GetValueOrDefault(reason) + 1;
            }
        }

        List<string> mismatches = [];
        Check(mismatches, "URL summary rows", stats.UrlCount, count);
        Check(mismatches, "Indexable pages in the URL summary", stats.IndexablePageCount, indexablePages);
        foreach (var reason in stats.NonIndexableReasons.Keys.Union(nonIndexable.Keys).Where(reason => reason != IndexabilityReason.None).Order())
        {
            Check(mismatches, $"{reason} URLs in the URL summary", Get(stats.NonIndexableReasons, reason), nonIndexable.GetValueOrDefault(reason));
        }

        return mismatches;
    }

    private static long Get<TKey>(IReadOnlyDictionary<TKey, long> counts, TKey key) => counts.GetValueOrDefault(key);

    private static void Check(List<string> mismatches, string what, long expected, long actual)
    {
        if (expected != actual)
        {
            mismatches.Add($"{what}: expected {expected:N0}, found {actual:N0}");
        }
    }
}
