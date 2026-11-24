using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// Internal redirects: chains, loops, where they end up, temporary status codes and HTTPS downgrades. A chain is
/// reported once, where visitors and crawlers enter it (a URL that's linked, in a sitemap, or the start URL), not again
/// at every hop along the way.
/// </summary>
/// <param name="readUrl">The stored URL with an id: a loop's detail names its hops.</param>
internal sealed class RedirectRules(UrlTable urls, Func<int, string?> readUrl) : ISiteRule
{
    /// <summary>By default, Google's crawlers follow up to 10 redirect hops.</summary>
    private const int GOOGLEBOT_MAX_HOPS = 10;

    /// <summary>Redirecting URLs a crawled page links to in any way.</summary>
    private readonly HashSet<int> _linked = [];

    public IReadOnlyList<IssueDefinition> Checks { get; } = [IssueCatalog.REDIRECT_CHAIN, IssueCatalog.REDIRECT_LOOP, IssueCatalog.REDIRECT_BROKEN, IssueCatalog.REDIRECT_TEMPORARY, IssueCatalog.REDIRECT_DOWNGRADE];

    public void Observe(AnalyzedPage page)
    {
        foreach (var link in page.Links)
        {
            // An instant meta refresh is a hop of the chain itself, not a way into it.
            if (!(page.IsRedirect && link.Kind == LinkKind.MetaRefresh) && urls[link.ToId] is { Fetched: true, Outcome: FetchOutcome.Redirect })
            {
                _linked.Add(link.ToId);
            }
        }
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
                yield return new IssueHit(IssueCatalog.REDIRECT_BROKEN, id, $"{HttpStatusNames.Describe(url.StatusCode)} without a usable Location header");
                continue;
            }

            // A temporary redirect to another site is a hand-off, such as to a shop's hosted login or checkout, not a move
            // whose address search engines should switch to.
            if (url.StatusCode is 302 or 303 or 307 && urls[url.RedirectTargetId].IsInternal)
            {
                yield return new IssueHit(IssueCatalog.REDIRECT_TEMPORARY, id, HttpStatusNames.Describe(url.StatusCode), url.RedirectTargetId);
            }

            if (url.IsHttps && !urls[url.RedirectTargetId].IsHttps)
            {
                yield return new IssueHit(IssueCatalog.REDIRECT_DOWNGRADE, id, null, url.RedirectTargetId);
            }

            var trace = urls.TraceRedirects(id);
            if (trace.IsLoop)
            {
                yield return new IssueHit(IssueCatalog.REDIRECT_LOOP, id, LoopHops(id, trace));
                continue;
            }

            // A way into the chain: the start URL or a URL of the crawl's list, a sitemap's entry, or a link's target.
            if (trace.Hops >= 2 && (id == urls.StartId || url.Source == DiscoverySource.Seed || url.InSitemap || _linked.Contains(id)))
            {
                yield return new IssueHit(IssueCatalog.REDIRECT_CHAIN, id, ChainLength(trace), trace.FinalId);
            }

            var final = urls[trace.FinalId];
            if (final.IsBroken)
            {
                yield return new IssueHit(IssueCatalog.REDIRECT_BROKEN, id, $"Ends at a URL that {RuleText.Status(final)}", trace.FinalId);
            }
        }
    }

    /// <summary>
    /// The loop hop by hop, so the rule that sends it back can be found: "3 hops: /a/ (301) → /a (301) → back to /a/", each
    /// URL as a path where it's on the looping URL's own host.
    /// </summary>
    private string LoopHops(int id, RedirectTrace trace)
    {
        var start = readUrl(id) ?? string.Empty;
        List<string> hops = [];
        HashSet<int> visited = [];
        var current = id;
        while (visited.Add(current) && hops.Count < UrlTable.MAX_REDIRECT_HOPS)
        {
            var url = urls[current];
            var shown = RuleText.Near(readUrl(current) ?? string.Empty, start);
            hops.Add(url.IsMetaRefresh ? $"{shown} (meta refresh)"
                : url.StatusCode > 0 ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{shown} ({url.StatusCode})")
                : shown);
            current = url.RedirectTargetId;
        }

        return RuleText.Fit($"{RuleText.Count(trace.Hops, "hop", "hops")}: {string.Join(" → ", hops)} → back to {RuleText.Near(readUrl(current) ?? string.Empty, start)}");
    }

    private string ChainLength(RedirectTrace trace)
    {
        if (urls[trace.FinalId] is { Outcome: FetchOutcome.Redirect, RedirectTargetId: > 0 })
        {
            return $"More than {UrlTable.MAX_REDIRECT_HOPS} redirects, which browsers don't follow";
        }

        var hops = RuleText.Count(trace.Hops, "redirect", "redirects");
        return trace.Hops > GOOGLEBOT_MAX_HOPS ? $"{hops}, more than the {GOOGLEBOT_MAX_HOPS} Googlebot follows" : hops;
    }
}
