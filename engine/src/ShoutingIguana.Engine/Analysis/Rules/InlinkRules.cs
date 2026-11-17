using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// Counts the internal links into every URL, then reports indexable pages that only a sitemap (or only nofollow links)
/// leads to, canonical pages that only other pages' canonicals lead to, and pages linked only from pages that can't be
/// indexed. Links that reach a page through a redirect count for it. A partial crawl can't tell an unlinked page from one
/// whose linking pages weren't fetched yet, so the checks only run on complete crawls.
/// </summary>
/// <param name="crawlComplete">The crawl read every page that could link to a sitemap page, and every sitemap: orphans can be judged.</param>
/// <param name="everyLinkingPage">
/// The crawl read every page that could link to any page, sitemaps aside: unlinked canonicals can be judged. Null for the
/// same as <paramref name="crawlComplete"/>.
/// </param>
internal sealed class InlinkRules(UrlTable urls, bool crawlComplete, bool? everyLinkingPage = null) : ISiteRule
{
    // The pages other pages' canonicals name, each once.
    private readonly HashSet<int> _canonicalTargets = [];

    public IReadOnlyList<IssueDefinition> Checks { get; } = [IssueCatalog.ORPHAN_PAGE, IssueCatalog.CANONICAL_UNLINKED, IssueCatalog.NON_INDEXABLE_INLINKS_ONLY];

    public void Observe(AnalyzedPage page)
    {
        urls.CountInlinks(page);

        // The canonical that decides, as the canonical checks read it: the first in the head or a Link header, naming another URL.
        if (page.Facts.Canonicals.FirstOrDefault(canonical => canonical is { Url: not null } && (canonical.InHead || canonical.FromHttpHeader)) is { } canonical &&
            canonical.Url != page.Url &&
            page.Links.FirstOrDefault(link => link.Kind == LinkKind.Canonical && !link.Flags.HasFlag(LinkFlags.OutsideHead)).ToId is > 0 and var target &&
            target != page.UrlId)
        {
            _canonicalTargets.Add(target);
        }
    }

    public IEnumerable<IssueHit> Finish()
    {
        if (!(everyLinkingPage ?? crawlComplete))
        {
            yield break;
        }

        urls.CreditRedirectTargets();
        if (crawlComplete)
        {
            // Google takes nofollow as a hint, so a page that only nofollow links lead to is all but unlinked.
            for (var id = 1; id < urls.Length; id++)
            {
                if (urls[id] is { IsInternal: true, IsIndexablePage: true, InSitemap: true } && id != urls.StartId && id != urls.HomeId &&
                    urls.FollowedInlinks(id) == 0)
                {
                    yield return new IssueHit(IssueCatalog.ORPHAN_PAGE, id, urls.NofollowInlinks(id) == 0 ? "Only found in a sitemap" : "Only nofollow links lead here");
                }
            }
        }

        // A sitemap page nothing links to is an orphan, when orphans could be judged; otherwise it's this check's.
        foreach (var id in _canonicalTargets.Order())
        {
            if (urls[id] is { IsInternal: true, IsIndexablePage: true } target && (!target.InSitemap || !crawlComplete) && IsUnlinked(id))
            {
                yield return new IssueHit(IssueCatalog.CANONICAL_UNLINKED, id, "Only canonicals lead here");
            }
        }

        for (var id = 1; id < urls.Length; id++)
        {
            if (urls[id] is { IsInternal: true, IsIndexablePage: true } && id != urls.StartId && id != urls.HomeId &&
                urls.FollowedInlinks(id) is > 0 and var linking && urls.IndexableInlinks(id) == 0)
            {
                yield return new IssueHit(IssueCatalog.NON_INDEXABLE_INLINKS_ONLY, id,
                    linking == 1 ? "Linked from 1 page, which isn't indexable" : $"Linked from {RuleText.Count(linking, "page", "pages")}, none of them indexable");
            }
        }
    }

    /// <summary>No page links to it, followed or not, even through a redirect, and it isn't where the crawl started.</summary>
    private bool IsUnlinked(int id) =>
        id != urls.StartId && id != urls.HomeId && urls.FollowedInlinks(id) == 0 && urls.NofollowInlinks(id) == 0;
}
