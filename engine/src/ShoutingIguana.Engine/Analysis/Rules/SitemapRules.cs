using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// Compares the sitemaps with what the crawl found. Problems with the sitemap files themselves go on the home page.
/// Coverage (pages missing from the sitemaps, no sitemap at all) needs a complete crawl with every sitemap read.
/// </summary>
internal sealed class SitemapRules(UrlTable urls, IReadOnlyList<StoredSitemap> sitemaps, CrawlOptions options, bool crawlComplete) : ISiteRule
{
    private const int LISTED_FAILURES = 3;

    public void Observe(AnalyzedPage page)
    {
    }

    public IEnumerable<IssueHit> Finish()
    {
        var anyRead = sitemaps.Any(sitemap => sitemap is { Error: null, StatusCode: >= 200 and < 300 });
        for (var id = 1; id < urls.Length; id++)
        {
            var url = urls[id];
            if (!url.IsInternal || !url.Fetched)
            {
                continue;
            }

            // A PDF or image listed in a sitemap is fine, and URLs the crawler couldn't check are reported as
            // response problems instead. What's left is pages that can't be indexed.
            var unverified = url.Outcome is FetchOutcome.Blocked or FetchOutcome.Timeout or FetchOutcome.NetworkError or FetchOutcome.ProcessingError;
            var nonIndexable = url.Outcome != FetchOutcome.Success || url.IndexabilityReason != IndexabilityReason.None;
            if (url.InSitemap && nonIndexable && !unverified)
            {
                yield return new IssueHit(IssueCatalog.SITEMAP_NON_INDEXABLE, id, $"The URL {RuleText.Status(url)}");
            }
            else if (crawlComplete && anyRead && !url.InSitemap && url.IsIndexablePage)
            {
                yield return new IssueHit(IssueCatalog.NOT_IN_SITEMAP, id);
            }
        }

        if (urls.HomeId == 0)
        {
            yield break;
        }

        // A failed guess at /sitemap.xml means there's no sitemap, not a broken one.
        var failed = sitemaps.Where(sitemap => sitemap is { Error: not null, IsProbe: false }).ToList();
        if (failed.Count > 0)
        {
            var listed = string.Join(", ", failed.Take(LISTED_FAILURES).Select(sitemap => $"{sitemap.Url} ({sitemap.Error})"));
            var more = failed.Count > LISTED_FAILURES ? $" and {failed.Count - LISTED_FAILURES:N0} more" : string.Empty;
            yield return new IssueHit(IssueCatalog.SITEMAP_ERROR, urls.HomeId, listed + more);
        }

        var anyFound = sitemaps.Any(sitemap => !sitemap.IsProbe || sitemap.Error is null);
        if (crawlComplete && options.DiscoverSitemaps && !anyFound)
        {
            yield return new IssueHit(IssueCatalog.SITEMAP_MISSING, urls.HomeId);
        }
    }
}
