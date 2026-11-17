using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// Hreflang annotations on indexable pages: valid codes, a self-reference, one URL per code, x-default and working
/// targets on each page, then return links across pages. Only annotations in the head or an HTTP Link header count,
/// as with Google, and only on indexable pages: Google reads hreflang on canonical pages, so a canonicalised variant
/// that repeats its canonical's set is doing the recommended thing.
/// </summary>
internal sealed class HreflangRules(UrlTable urls) : IPageRule, ISiteRule
{
    /// <summary>Every declared alternate as (page &lt;&lt; 32 | alternate): 8 bytes each, sorted for the return-link lookups.</summary>
    private readonly List<long> _alternates = [];

    public IEnumerable<IssueHit> Check(AnalyzedPage page)
    {
        var all = page.Facts.Hreflangs;
        if (!page.IsIndexable || all.Count == 0)
        {
            yield break;
        }

        var honoured = all.Where(hreflang => hreflang.InHead || hreflang.FromHttpHeader).ToList();
        if (honoured.Count < all.Count)
        {
            var outside = RuleText.Count(all.Count - honoured.Count, "entry", "entries") + " in the body";
            yield return new IssueHit(IssueCatalog.HREFLANG_OUTSIDE_HEAD, page.UrlId,
                RuleText.HeadEnded(page.Facts.HeadEnd) is { } ended ? RuleText.Fit($"{outside}: {char.ToLowerInvariant(ended[0])}{ended[1..]}") : outside);
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
            yield return new IssueHit(IssueCatalog.HREFLANG_MISSING_SELF, page.UrlId, MissingSelf(page.Url, honoured));
        }

        var conflicting = honoured.Where(hreflang => hreflang.Url is not null)
            .GroupBy(hreflang => hreflang.Language, StringComparer.OrdinalIgnoreCase)
            .Select(group => (Code: group.Key, Urls: group.Select(hreflang => hreflang.Url!).Distinct(StringComparer.Ordinal).ToList()))
            .Where(group => group.Urls.Count > 1)
            .ToList();
        if (conflicting.Count > 0)
        {
            // Each code with the URLs it names, the codes first: grouped by them alone (IssueFaults.HreflangCodes).
            yield return new IssueHit(IssueCatalog.HREFLANG_DUPLICATE_CODE, page.UrlId, RuleText.Fit(string.Join("; ",
                conflicting.Select(group => $"{group.Code}: {string.Join(" · ", group.Urls.Select(url => RuleText.Near(url, page.Url)))}"))));
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
            // As with canonicals: another site's robots.txt disallowing the crawler says nothing about whether Google can
            // see the page, and a site refusing the crawler (a firewall, a rate limit) leaves the URL unverified.
            else if (status.Outcome == FetchOutcome.Redirect || (status.Outcome == FetchOutcome.RobotsDisallowed && status.IsInternal) ||
                     status is { IsPage: true, IsIndexablePage: false })
            {
                nonIndexable.Add(target);
            }
        }

        if (broken.Count > 0)
        {
            yield return new IssueHit(IssueCatalog.HREFLANG_BROKEN_TARGET, page.UrlId, RuleText.Count(broken.Count, "URL", "URLs"), broken.First) { Targets = broken.Ids };
        }

        if (nonIndexable.Count > 0)
        {
            yield return new IssueHit(IssueCatalog.HREFLANG_TO_NON_INDEXABLE, page.UrlId,
                RuleText.Count(nonIndexable.Count, "URL", "URLs"), nonIndexable.First) { Targets = nonIndexable.Ids };
        }
    }

    /// <summary>Of its checks, the one across pages: the rest it reports page by page, from <see cref="Check"/>.</summary>
    public IReadOnlyList<IssueDefinition> Checks { get; } = [IssueCatalog.HREFLANG_MISSING_RETURN];

    public void Observe(AnalyzedPage page)
    {
        if (!page.IsIndexable)
        {
            return;
        }

        foreach (var target in HonouredTargets(page).Distinct())
        {
            _alternates.Add(Pair(page.UrlId, target));
        }
    }

    public IEnumerable<IssueHit> Finish()
    {
        _alternates.Sort();
        for (var start = 0; start < _alternates.Count;)
        {
            var pageId = (int)(_alternates[start] >> 32);
            TargetSet missing = new();
            var end = start;
            for (; end < _alternates.Count && (int)(_alternates[end] >> 32) == pageId; end++)
            {
                // Broken and non-indexable alternates are reported on their own; other sites' pages aren't parsed.
                var target = (int)_alternates[end];
                if (urls[target] is { IsInternal: true, IsIndexablePage: true } && _alternates.BinarySearch(Pair(target, pageId)) < 0)
                {
                    missing.Add(target);
                }
            }

            if (missing.Count > 0)
            {
                yield return new IssueHit(IssueCatalog.HREFLANG_MISSING_RETURN, pageId,
                    $"{RuleText.Count(missing.Count, "alternate doesn't", "alternates don't")} link back", missing.First) { Targets = missing.Ids };
            }

            start = end;
        }
    }

    /// <summary>
    /// How many entries the page lists, none of them it, and the one that nearly is (another scheme, www or not, a trailing
    /// slash, letters' case), which is the usual slip: "None of its 4 entries names this URL; "en" names http://www.x.com/en".
    /// </summary>
    private static string MissingSelf(string pageUrl, IReadOnlyList<HreflangFact> honoured)
    {
        var none = honoured.Count == 1 ? "Its one entry doesn't name this URL" : $"None of its {RuleText.Count(honoured.Count, "entry", "entries")} names this URL";
        var self = Loosely(pageUrl);
        return honoured.FirstOrDefault(hreflang => hreflang.Url is { } url && Loosely(url) == self) is { } near
            ? RuleText.Fit($"{none}; \"{near.Language}\" names {near.Url}")
            : none;
    }

    /// <summary>A URL without its scheme, a leading www., a trailing slash or letters' case.</summary>
    private static string Loosely(string url)
    {
        var rest = url.ToLowerInvariant();
        rest = rest[(rest.IndexOf("://", StringComparison.Ordinal) is var scheme and >= 0 ? scheme + 3 : 0)..];
        rest = rest.StartsWith("www.", StringComparison.Ordinal) ? rest[4..] : rest;
        return rest.TrimEnd('/');
    }

    private static long Pair(int page, int target) => ((long)page << 32) | (uint)target;

    private static IEnumerable<int> HonouredTargets(AnalyzedPage page) => page.Links
        .Where(link => link.Kind == LinkKind.Hreflang && !link.Flags.HasFlag(LinkFlags.OutsideHead) && link.ToId != page.UrlId)
        .Select(link => link.ToId);
}
