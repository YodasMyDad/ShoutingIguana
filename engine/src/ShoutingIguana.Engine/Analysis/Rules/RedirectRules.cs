using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>Internal redirects: chains, loops, where they end up, temporary status codes and HTTPS downgrades.</summary>
internal sealed class RedirectRules(UrlTable urls) : ISiteRule
{
    public void Observe(AnalyzedPage page)
    {
    }

    public IEnumerable<IssueHit> Finish()
    {
        for (var id = 1; id < urls.Length; id++)
        {
            var url = urls[id];
            if (url is not { IsInternal: true, Fetched: true, Outcome: FetchOutcome.Redirect })
            {
                continue;
            }

            if (url.RedirectTargetId == 0)
            {
                yield return new IssueHit(IssueCatalog.REDIRECT_BROKEN, id, $"HTTP {url.StatusCode} without a usable Location header");
                continue;
            }

            if (url.StatusCode is 302 or 303 or 307)
            {
                yield return new IssueHit(IssueCatalog.REDIRECT_TEMPORARY, id, $"HTTP {url.StatusCode}", url.RedirectTargetId);
            }

            if (url.IsHttps && !urls[url.RedirectTargetId].IsHttps)
            {
                yield return new IssueHit(IssueCatalog.REDIRECT_DOWNGRADE, id, null, url.RedirectTargetId);
            }

            var trace = urls.TraceRedirects(id);
            if (trace.IsLoop)
            {
                yield return new IssueHit(IssueCatalog.REDIRECT_LOOP, id, RuleText.Count(trace.Hops, "hop", "hops"));
                continue;
            }

            if (trace.Hops >= 2)
            {
                yield return new IssueHit(IssueCatalog.REDIRECT_CHAIN, id, RuleText.Count(trace.Hops, "redirect", "redirects"), trace.FinalId);
            }

            var final = urls[trace.FinalId];
            if (final.IsBroken)
            {
                yield return new IssueHit(IssueCatalog.REDIRECT_BROKEN, id, $"Ends at a URL that {RuleText.Status(final)}", trace.FinalId);
            }
        }
    }
}
