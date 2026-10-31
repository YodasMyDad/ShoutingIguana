using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// The page's canonical: whether it's declared properly, and whether the URL it names can be indexed. Only canonicals
/// in the head or an HTTP Link header count, as with Google.
/// </summary>
internal sealed class CanonicalRules(UrlTable urls) : IPageRule
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
            var detail = urls.TraceRedirects(targetId).FinalId == page.UrlId
                ? "The canonical URL redirects back to this page"
                : $"The canonical URL {RuleText.Status(target)}";
            yield return new IssueHit(IssueCatalog.CANONICAL_TO_REDIRECT, page.UrlId, detail, targetId);
        }
        // Another site blocking or disallowing the crawler says nothing about whether Google can see the page.
        else if ((target.IsInternal && target.Outcome is FetchOutcome.RobotsDisallowed or FetchOutcome.Blocked) ||
                 target is { IsPage: true, IsIndexablePage: false })
        {
            yield return new IssueHit(IssueCatalog.CANONICAL_TO_NON_INDEXABLE, page.UrlId, $"The canonical URL {RuleText.Status(target)}", targetId);
        }
    }

    private static bool IsAbsolute(string href) =>
        href.TrimStart().StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        href.TrimStart().StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
