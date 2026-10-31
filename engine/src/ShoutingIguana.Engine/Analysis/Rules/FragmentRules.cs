using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Parsing;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// Links to #fragments that match no id or named anchor on the target page. Same-page links are checked as each page
/// is seen; links to other pages are checked at the end, reading each target's stored ids once.
/// </summary>
internal sealed class FragmentRules(UrlTable urls, AnalysisReader reader) : ISiteRule
{
    private readonly Dictionary<int, (int Count, int FirstTarget)> _broken = [];
    private readonly List<(int From, int To, string Fragment)> _pending = [];

    public void Observe(AnalyzedPage page)
    {
        HashSet<string>? ownIds = null;
        foreach (var link in page.Links)
        {
            if (link.Kind != LinkKind.Hyperlink || link.Fragment is not { } fragment || !IsCheckable(fragment))
            {
                continue;
            }

            if (link.ToId == page.UrlId)
            {
                if (page.Facts.AnchorIds.Count < PageWalker.MAX_IDS &&
                    !Exists(ownIds ??= new HashSet<string>(page.Facts.AnchorIds, StringComparer.Ordinal), fragment))
                {
                    Record(page.UrlId, link.ToId);
                }
            }
            else if (urls[link.ToId] is { IsInternal: true, IsPage: true })
            {
                _pending.Add((page.UrlId, link.ToId, fragment));
            }
        }
    }

    public IEnumerable<IssueHit> Finish()
    {
        foreach (var group in _pending.GroupBy(link => link.To))
        {
            // A capped id list can't prove an id is missing.
            if (reader.ReadFacts(group.Key) is not { } facts || facts.AnchorIds.Count >= PageWalker.MAX_IDS)
            {
                continue;
            }

            var ids = new HashSet<string>(facts.AnchorIds, StringComparer.Ordinal);
            foreach (var link in group.Where(link => !Exists(ids, link.Fragment)))
            {
                Record(link.From, link.To);
            }
        }

        foreach (var (pageId, (count, firstTarget)) in _broken)
        {
            yield return new IssueHit(IssueCatalog.BROKEN_FRAGMENT_LINKS, pageId, RuleText.Count(count, "link", "links"), firstTarget);
        }
    }

    /// <summary>
    /// "top" scrolls to the top without any element (HTML spec). Fragments starting "!" or "/" are app routes, and ones
    /// with "=", ":" or "&amp;" carry app state or text fragments, so none of them name an element.
    /// </summary>
    private static bool IsCheckable(string fragment) =>
        !fragment.Equals("top", StringComparison.OrdinalIgnoreCase) && fragment[0] is not ('!' or '/') &&
        fragment.IndexOfAny(['=', ':', '&']) < 0;

    private static bool Exists(HashSet<string> ids, string fragment) =>
        ids.Contains(fragment) || ids.Contains(Uri.UnescapeDataString(fragment));

    private void Record(int pageId, int targetId) =>
        _broken[pageId] = _broken.TryGetValue(pageId, out var current) ? (current.Count + 1, current.FirstTarget) : (1, targetId);
}
