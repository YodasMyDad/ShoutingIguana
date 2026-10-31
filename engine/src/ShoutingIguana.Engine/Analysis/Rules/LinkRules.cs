using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// Where a page's hyperlinks go: broken targets, redirects, nofollow and links without text. A link whose redirects
/// end at an error (or loop) is broken, not just a link to a redirect.
/// </summary>
internal sealed class LinkRules(UrlTable urls) : IPageRule
{
    public IEnumerable<IssueHit> Check(AnalyzedPage page)
    {
        TargetSet brokenInternal = new(), brokenExternal = new(), redirects = new(), nofollow = new(), withoutText = new();
        foreach (var link in page.Links)
        {
            if (link.Kind != LinkKind.Hyperlink || link.ToId == page.UrlId || link.Flags.HasFlag(LinkFlags.SamePage))
            {
                continue;
            }

            var target = urls[link.ToId];
            var isBroken = urls.IsBrokenThroughRedirects(link.ToId);
            if (!target.IsInternal)
            {
                if (isBroken)
                {
                    brokenExternal.Add(link.ToId);
                }

                continue;
            }

            if (link.Flags.HasFlag(LinkFlags.NoFollow))
            {
                nofollow.Add(link.ToId);
            }

            // Head <link> elements such as rel="amphtml" have no text by design.
            if (link.Position != LinkPosition.Head && string.IsNullOrWhiteSpace(link.Text))
            {
                withoutText.Add(link.ToId);
            }

            if (isBroken)
            {
                brokenInternal.Add(link.ToId);
            }
            else if (target is { Fetched: true, Outcome: FetchOutcome.Redirect })
            {
                redirects.Add(link.ToId);
            }
        }

        if (brokenInternal.Count > 0)
        {
            yield return new IssueHit(IssueCatalog.BROKEN_INTERNAL_LINKS, page.UrlId,
                RuleText.BrokenTargets(urls, brokenInternal, "broken URL", "broken URLs"), brokenInternal.First);
        }

        if (brokenExternal.Count > 0)
        {
            yield return new IssueHit(IssueCatalog.BROKEN_EXTERNAL_LINKS, page.UrlId,
                RuleText.BrokenTargets(urls, brokenExternal, "broken URL", "broken URLs"), brokenExternal.First);
        }

        if (redirects.Count > 0)
        {
            yield return new IssueHit(IssueCatalog.LINKS_TO_REDIRECTS, page.UrlId,
                RuleText.Count(redirects.Count, "redirecting URL", "redirecting URLs"), redirects.First);
        }

        if (nofollow.Count > 0)
        {
            yield return new IssueHit(IssueCatalog.INTERNAL_NOFOLLOW_LINKS, page.UrlId,
                RuleText.Count(nofollow.Count, "URL", "URLs"), nofollow.First);
        }

        if (withoutText.Count > 0)
        {
            yield return new IssueHit(IssueCatalog.EMPTY_ANCHOR_TEXT, page.UrlId,
                RuleText.Count(withoutText.Count, "URL", "URLs"), withoutText.First);
        }
    }
}
