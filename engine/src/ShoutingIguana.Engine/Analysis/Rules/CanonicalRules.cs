using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// The page's canonical: whether it's declared properly, and whether the URL it names can be indexed. Only canonicals
/// in the head or an HTTP Link header count, as with Google.
/// </summary>
/// <param name="readUrl">The stored URL with an id, or null: a canonical that redirects is reported with the URL its redirects end at.</param>
internal sealed class CanonicalRules(UrlTable urls, Func<int, string?> readUrl) : IPageRule
{
    public IEnumerable<IssueHit> Check(AnalyzedPage page)
    {
        var canonicals = page.Facts.Canonicals;
        var honoured = canonicals.Where(canonical => canonical.InHead || canonical.FromHttpHeader).ToList();
        if (honoured.Count == 0)
        {
            if (canonicals.Count > 0)
            {
                yield return new IssueHit(IssueCatalog.CANONICAL_OUTSIDE_HEAD, page.UrlId);
            }
            else if (page.IsIndexable)
            {
                yield return new IssueHit(IssueCatalog.CANONICAL_MISSING, page.UrlId);
            }

            yield break;
        }

        if (honoured.FirstOrDefault(canonical => canonical.Url is null) is { } invalid)
        {
            yield return new IssueHit(IssueCatalog.CANONICAL_INVALID, page.UrlId, invalid.Href);
        }

        var distinct = honoured.Where(canonical => canonical.Url is not null).Select(canonical => canonical.Url).Distinct(StringComparer.Ordinal).Count();
        if (distinct > 1)
        {
            yield return new IssueHit(IssueCatalog.CANONICAL_MULTIPLE, page.UrlId, RuleText.Count(distinct, "URL", "different URLs"));
        }

        if (honoured.FirstOrDefault(canonical => canonical is { FromHttpHeader: false, Url: not null } && !IsAbsolute(canonical.Href)) is { } relative)
        {
            yield return new IssueHit(IssueCatalog.CANONICAL_RELATIVE, page.UrlId, relative.Href);
        }

        // The first honoured canonical decides, as it does for indexability.
        if (honoured.FirstOrDefault(canonical => canonical.Url is not null) is not { } canonical || canonical.Url == page.Url)
        {
            yield break;
        }

        var targetId = page.Links.FirstOrDefault(link => link.Kind == LinkKind.Canonical && !link.Flags.HasFlag(LinkFlags.OutsideHead)).ToId;
        if (page.IndexabilityReason == IndexabilityReason.Canonicalised)
        {
            yield return new IssueHit(IssueCatalog.CANONICALISED_PAGE, page.UrlId, null, targetId > 0 ? targetId : null);
        }

        // The noindex wins, so the page isn't canonicalised, but the two signals contradict each other. Not when the
        // canonical leads back to this page through redirects (it's a self-reference), or leads nowhere and is ignored.
        var pointsElsewhere = targetId == 0 || (urls.TraceRedirects(targetId).FinalId != page.UrlId && !urls.IsBrokenThroughRedirects(targetId));
        if (page.Facts.Robots.NoIndex && pointsElsewhere)
        {
            yield return new IssueHit(IssueCatalog.NOINDEX_WITH_CANONICAL, page.UrlId, null, targetId > 0 ? targetId : null);
        }

        if (targetId == 0 || urls[targetId] is not { Fetched: true } target)
        {
            yield break;
        }

        // A canonical that leads nowhere, even through redirects, is ignored, which leaves the page indexable.
        if (urls.IsBrokenThroughRedirects(targetId))
        {
            yield return new IssueHit(IssueCatalog.CANONICAL_BROKEN, page.UrlId, $"The canonical URL {RuleText.BrokenStatus(urls, targetId)}", targetId);
        }
        else if (target.Outcome == FetchOutcome.Redirect)
        {
            // /page/ naming /page, which redirects to /page/, still points search engines at /page/.
            var trace = urls.TraceRedirects(targetId);
            var detail = trace.FinalId == page.UrlId
                ? "The canonical URL redirects back to this page"
                : $"The canonical URL {RuleText.Status(target)}{EndsAt(trace)}";
            yield return new IssueHit(IssueCatalog.CANONICAL_TO_REDIRECT, page.UrlId, detail, targetId);
        }
        // Another site disallowing the crawler says nothing about whether Google can see the page. Nor does any site
        // refusing it (a firewall, a bot challenge, a rate limit): the URL is unverified, as the sitemap checks treat it,
        // and internal-blocked reports it.
        else if ((target.IsInternal && target.Outcome == FetchOutcome.RobotsDisallowed) || target is { IsPage: true, IsIndexablePage: false })
        {
            yield return new IssueHit(IssueCatalog.CANONICAL_TO_NON_INDEXABLE, page.UrlId, $"The canonical URL {RuleText.Status(target)}", targetId);
        }
    }

    /// <summary>
    /// Where the canonical's redirects end, which is the URL the canonical should name: " to https://example.com/page" after
    /// one redirect, " and, after 3 redirects, ends at https://example.com/page" after more. Only a URL that answered is
    /// known to be the end: one the crawl never asked for, or was refused at, may redirect again, so it is where the
    /// redirects lead, and the detail says why no more is known: ", which wasn't checked", ", which is blocked by robots.txt".
    /// </summary>
    private string EndsAt(RedirectTrace trace)
    {
        if (readUrl(trace.FinalId) is not { } final)
        {
            return string.Empty;
        }

        var last = urls[trace.FinalId];
        var unknown = last switch
        {
            { Fetched: false } => ", which wasn't checked",
            { Outcome: FetchOutcome.Success } => string.Empty,
            _ => $", which {RuleText.Status(last)}"
        };

        return trace.Hops == 1
            ? $" to {final}{unknown}"
            : $" and, after {RuleText.Count(trace.Hops, "redirect", "redirects")}, {(unknown.Length == 0 ? "ends at" : "leads to")} {final}{unknown}";
    }

    private static bool IsAbsolute(string href) =>
        href.TrimStart().StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        href.TrimStart().StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
