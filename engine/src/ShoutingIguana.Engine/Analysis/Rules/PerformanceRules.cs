using System.Globalization;
using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>Slow responses and oversized HTML, as measured during the crawl.</summary>
internal sealed class PerformanceRules(AnalysisSettings settings) : IPageRule
{
    /// <summary>
    /// How much uncompressed HTML Googlebot reads for Google Search (Search Central, the Googlebot doc). Google's
    /// limit, not an <see cref="AnalysisSettings"/> threshold: content and links past it are ignored.
    /// </summary>
    internal const long GOOGLEBOT_HTML_BYTES = 2 * 1024 * 1024;

    public IEnumerable<IssueHit> Check(AnalyzedPage page)
    {
        // The first byte tells a server that took long to answer from a page that took long to download.
        if (page.ResponseMs > settings.SlowResponseMs)
        {
            yield return new IssueHit(IssueCatalog.SLOW_RESPONSE, page.UrlId,
                string.Create(CultureInfo.InvariantCulture, $"{page.ResponseMs:N0} ms, over {settings.SlowResponseMs:N0} ms (first byte after {page.TimeToFirstByteMs:N0} ms)"));
        }

        if (page.BodyBytes > settings.LargePageBytes)
        {
            yield return new IssueHit(IssueCatalog.LARGE_PAGE, page.UrlId, $"{RuleText.Size(page.BodyBytes.Value)}, over {RuleText.LimitSize(settings.LargePageBytes)}");
        }

        if (page.BodyBytes > GOOGLEBOT_HTML_BYTES)
        {
            yield return new IssueHit(IssueCatalog.HTML_PAST_CRAWL_LIMIT, page.UrlId, $"{RuleText.Size(page.BodyBytes.Value)}, past the {RuleText.LimitSize(GOOGLEBOT_HTML_BYTES)} Google reads");
        }
    }
}
