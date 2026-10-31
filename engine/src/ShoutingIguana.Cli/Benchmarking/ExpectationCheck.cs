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
