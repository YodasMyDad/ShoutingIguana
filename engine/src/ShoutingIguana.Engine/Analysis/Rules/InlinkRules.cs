using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// Counts the internal links into every URL, then reports indexable pages that only a sitemap leads to. Links that
/// reach a page through a redirect count for it. A partial crawl can't tell an orphan from a page whose linking pages
/// weren't fetched yet, so the check only runs on complete crawls.
/// </summary>
internal sealed class InlinkRules(UrlTable urls, bool crawlComplete) : ISiteRule
{
    public IReadOnlyList<IssueDefinition> Checks { get; } = [IssueCatalog.ORPHAN_PAGE];

    public void Observe(AnalyzedPage page) => urls.CountInlinks(page);

    public IEnumerable<IssueHit> Finish()
    {
        if (!crawlComplete)
        {
            yield break;
        }

        urls.CreditRedirectTargets();
        for (var id = 1; id < urls.Length; id++)
        {
            if (urls[id] is { IsInternal: true, IsIndexablePage: true, InSitemap: true } && id != urls.StartId && id != urls.HomeId &&
                urls.FollowedInlinks(id) == 0 && urls.NofollowInlinks(id) == 0)
            {
                yield return new IssueHit(IssueCatalog.ORPHAN_PAGE, id, "Only found in a sitemap");
            }
        }
    }
}
