using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// The rel="next" and rel="prev" links of paginated pages: where they lead (a page linked only through them, one that's
/// broken or redirects, one that can't be indexed) and whether they make a sequence, which needs the pages they name.
/// Only paginated pages are kept, a few ids each.
/// </summary>
/// <param name="readUrlId">The id of a stored URL, given normalised, or 0: the facts name the targets by URL.</param>
/// <param name="readUrl">The stored URL with an id, or null.</param>
internal sealed class PaginationRules(UrlTable urls, Func<string, int> readUrlId, Func<int, string?> readUrl) : ISiteRule
{
    /// <summary>rel="next" links followed, page to page, before a trace gives up looking for a loop.</summary>
    private const int MAX_STEPS = 50;

    private const string NEXT = "rel=\"next\"";
    private const string PREV = "rel=\"prev\"";

    // Each paginated page's rel="next" and rel="prev" targets, and the hits found on the page alone.
    private readonly Dictionary<int, (int[] Next, int[] Prev)> _series = [];
    private readonly List<IssueHit> _hits = [];

    public IReadOnlyList<IssueDefinition> Checks { get; } =
    [
        IssueCatalog.PAGINATION_NOT_LINKED, IssueCatalog.PAGINATION_BROKEN, IssueCatalog.PAGINATION_TO_NON_INDEXABLE, IssueCatalog.PAGINATION_SEQUENCE_ERROR
    ];

    public void Observe(AnalyzedPage page)
    {
        if (page.IsRedirect || page.Facts.Pagination.Count == 0)
        {
            return;
        }

        var next = Targets(page, isNext: true);
        var prev = Targets(page, isNext: false);
        _series[page.UrlId] = (next, prev);

        // Pages it links to with <a href>, its sections included: a fragment doesn't make another page. A link to a copy
        // of a page counts for the page the copy names as its canonical: an AMP page's pager links the AMP copy of the
        // next page, while its rel="next" names the page itself.
        var linked = page.Links.Where(link => link.Kind == LinkKind.Hyperlink).Select(link => link.ToId).ToHashSet();
        linked.UnionWith(linked.Select(urls.CanonicalTarget).Where(id => id > 0).ToList());
        TargetSet unlinked = new(), broken = new(), nonIndexable = new();
        string? unlinkedDetail = null, brokenDetail = null, nonIndexableDetail = null;
        foreach (var (target, rel) in next.Select(id => (id, NEXT)).Concat(prev.Select(id => (id, PREV))))
        {
            var status = urls[target];
            if (target == page.UrlId || !status.Fetched)
            {
                continue;
            }

            if (urls.IsBrokenThroughRedirects(target) || status.Outcome == FetchOutcome.Redirect)
            {
                broken.Add(target);
                brokenDetail ??= urls.IsBrokenThroughRedirects(target) ? $"{rel} {RuleText.BrokenStatus(urls, target)}" : $"{rel} {RuleText.Status(status)}";
                continue;
            }

            if (status is { IsInternal: true, IsPage: true } && !linked.Contains(target))
            {
                unlinked.Add(target);
                unlinkedDetail ??= RuleText.Fit($"{rel} {readUrl(target)} has no link");
            }

            if (status is { IsPage: true, IsIndexablePage: false })
            {
                nonIndexable.Add(target);
                nonIndexableDetail ??= $"{rel} {RuleText.Status(status)}";
            }
        }

        Add(IssueCatalog.PAGINATION_NOT_LINKED, unlinked, unlinkedDetail);
        Add(IssueCatalog.PAGINATION_BROKEN, broken, brokenDetail);
        Add(IssueCatalog.PAGINATION_TO_NON_INDEXABLE, nonIndexable, nonIndexableDetail);

        void Add(IssueDefinition check, TargetSet targets, string? detail)
        {
            if (targets.Count > 0)
            {
                _hits.Add(new IssueHit(check, page.UrlId, detail, targets.First) { Targets = targets.Ids });
            }
        }
    }

    public IEnumerable<IssueHit> Finish()
    {
        foreach (var hit in _hits)
        {
            yield return hit;
        }

        foreach (var (page, (next, prev)) in _series.OrderBy(entry => entry.Key))
        {
            if (SequenceError(page, next, prev) is { } fault)
            {
                yield return new IssueHit(IssueCatalog.PAGINATION_SEQUENCE_ERROR, page, fault);
            }
        }
    }

    /// <summary>
    /// What's wrong with the page's place in its series, the first of: more than one rel="next" or rel="prev"; one naming
    /// the page itself; the page its rel="next" names, read by the crawl, not naming it back; following rel="next" coming
    /// back to a page already passed. Null when nothing is.
    /// </summary>
    private string? SequenceError(int page, int[] next, int[] prev)
    {
        if (next.Length > 1 || prev.Length > 1)
        {
            return next.Length > 1 ? $"{next.Length} {NEXT} URLs" : $"{prev.Length} {PREV} URLs";
        }

        if (next.Contains(page) || prev.Contains(page))
        {
            return $"{(next.Contains(page) ? NEXT : PREV)} names the page itself";
        }

        if (next.Length == 0)
        {
            return null;
        }

        var following = next[0];
        // A copy of a page (an AMP version) is pointed back to by its canonical: the series is the canonical pages'.
        if (urls[following].IsPage && !(_series.TryGetValue(following, out var after) &&
                                        (after.Prev.Contains(page) || (urls.CanonicalTarget(page) is > 0 and var canonical && after.Prev.Contains(canonical)))))
        {
            return RuleText.Fit($"{Near(following, page)} doesn't point back");
        }

        HashSet<int> passed = [page];
        var at = following;
        for (var steps = 0; steps < MAX_STEPS && _series.TryGetValue(at, out var onward) && onward.Next.Length == 1; steps++)
        {
            if (!passed.Add(at))
            {
                return RuleText.Fit($"{NEXT} leads back to {Near(at, page)}");
            }

            at = onward.Next[0];
        }

        return null;
    }

    /// <summary>The distinct pages the page's rel="next" (or rel="prev") links name, in page order, as the crawl stored them.</summary>
    private int[] Targets(AnalyzedPage page, bool isNext) =>
    [
        .. page.Facts.Pagination.Where(link => link.IsNext == isNext && link.Url is not null)
            .Select(link => readUrlId(link.Url!)).Where(id => id > 0).Distinct()
    ];

    private string Near(int id, int page) => readUrl(id) is { } url && readUrl(page) is { } from ? RuleText.Near(url, from) : readUrl(id) ?? "?";
}
