using System.Globalization;
using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>Slow responses and oversized HTML, as measured during the crawl.</summary>
internal sealed class PerformanceRules(AnalysisSettings settings) : IPageRule
{
    public IEnumerable<IssueHit> Check(AnalyzedPage page)
    {
        if (page.ResponseMs > settings.SlowResponseMs)
        {
            yield return new IssueHit(IssueCatalog.SLOW_RESPONSE, page.UrlId,
                string.Create(CultureInfo.InvariantCulture, $"{page.ResponseMs:N0} ms"));
        }

        if (page.BodyBytes > settings.LargePageBytes)
        {
            yield return new IssueHit(IssueCatalog.LARGE_PAGE, page.UrlId, RuleText.Size(page.BodyBytes.Value));
        }
    }
}
