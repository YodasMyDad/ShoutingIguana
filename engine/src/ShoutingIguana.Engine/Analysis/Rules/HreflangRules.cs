using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// Hreflang annotations: valid codes, a self-reference, one URL per code, x-default and working targets on each page,
/// then return links across pages. Only annotations in the head or an HTTP Link header count, as with Google.
/// </summary>
internal sealed class HreflangRules(UrlTable urls) : IPageRule, ISiteRule
{
    /// <summary>The alternates each page declares, for the return-link check.</summary>
    private readonly Dictionary<int, HashSet<int>> _alternates = [];

    public IEnumerable<IssueHit> Check(AnalyzedPage page)
    {
        var all = page.Facts.Hreflangs;
        if (all.Count == 0)
        {
            yield break;
        }

        var honoured = all.Where(hreflang => hreflang.InHead || hreflang.FromHttpHeader).ToList();
        if (honoured.Count < all.Count)
        {
            yield return new IssueHit(IssueCatalog.HREFLANG_OUTSIDE_HEAD, page.UrlId);
        }

        if (honoured.Count == 0)
        {
            yield break;
        }

        var invalid = honoured.Select(hreflang => hreflang.Language).Where(code => !HreflangCodes.IsValid(code))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (invalid.Count > 0)
        {
            yield return new IssueHit(IssueCatalog.HREFLANG_INVALID_CODE, page.UrlId, string.Join(", ", invalid));
        }

        if (!honoured.Any(hreflang => hreflang.Url == page.Url))
        {
            yield return new IssueHit(IssueCatalog.HREFLANG_MISSING_SELF, page.UrlId);
        }

        var conflicting = honoured.Where(hreflang => hreflang.Url is not null)
            .GroupBy(hreflang => hreflang.Language, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(hreflang => hreflang.Url).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(group => group.Key)
            .ToList();
        if (conflicting.Count > 0)
        {
            yield return new IssueHit(IssueCatalog.HREFLANG_DUPLICATE_CODE, page.UrlId, string.Join(", ", conflicting));
        }

        if (!honoured.Any(hreflang => hreflang.Language.Equals("x-default", StringComparison.OrdinalIgnoreCase)))
        {
            yield return new IssueHit(IssueCatalog.HREFLANG_MISSING_X_DEFAULT, page.UrlId);
        }

        TargetSet broken = new(), nonIndexable = new();
        foreach (var target in HonouredTargets(page))
        {
            var status = urls[target];
            if (!status.Fetched)
            {
                continue;
            }

            if (status.IsBroken)
            {
                broken.Add(target);
            }
            else if (status.Outcome is FetchOutcome.Redirect or FetchOutcome.RobotsDisallowed or FetchOutcome.Blocked ||
                     status is { IsInternal: true, IsPage: true, IsIndexablePage: false })
            {
                nonIndexable.Add(target);
            }
        }

        if (broken.Count > 0)
        {
            yield return new IssueHit(IssueCatalog.HREFLANG_BROKEN_TARGET, page.UrlId, RuleText.Count(broken.Count, "URL", "URLs"), broken.First);
        }

        if (nonIndexable.Count > 0)
        {
            yield return new IssueHit(IssueCatalog.HREFLANG_TO_NON_INDEXABLE, page.UrlId,
                RuleText.Count(nonIndexable.Count, "URL", "URLs"), nonIndexable.First);
        }
    }

    public void Observe(AnalyzedPage page)
    {
        HashSet<int>? targets = null;
        foreach (var target in HonouredTargets(page))
        {
            (targets ??= []).Add(target);
        }

        if (targets is not null)
        {
            _alternates[page.UrlId] = targets;
        }
    }

    public IEnumerable<IssueHit> Finish()
    {
        foreach (var (pageId, targets) in _alternates)
        {
            TargetSet missing = new();
            foreach (var target in targets)
            {
                // Broken and non-indexable alternates are reported on their own; other sites' pages aren't parsed.
                if (urls[target] is { IsInternal: true, IsIndexablePage: true } &&
                    (!_alternates.TryGetValue(target, out var back) || !back.Contains(pageId)))
                {
                    missing.Add(target);
                }
            }

            if (missing.Count > 0)
            {
                yield return new IssueHit(IssueCatalog.HREFLANG_MISSING_RETURN, pageId,
                    $"{RuleText.Count(missing.Count, "alternate doesn't", "alternates don't")} link back", missing.First);
            }
        }
    }

    private static IEnumerable<int> HonouredTargets(AnalyzedPage page) => page.Links
        .Where(link => link.Kind == LinkKind.Hreflang && !link.Flags.HasFlag(LinkFlags.OutsideHead) && link.ToId != page.UrlId)
        .Select(link => link.ToId);
}
