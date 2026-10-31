using System.IO.Hashing;
using System.Runtime.InteropServices;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Parsing;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// Links to #fragments that match no id or named anchor on the target page. Same-page links are checked as each page
/// is seen. For links to other pages, the pass keeps only the distinct (target, fragment) pairs as hashes, so a listing
/// that links every product's #reviews costs one entry per product, not one per link. Each target's ids are then read
/// once, and the links table is streamed to find the pages whose links miss.
/// </summary>
internal sealed class FragmentRules(UrlTable urls, AnalysisReader reader) : ISiteRule
{
    private readonly Dictionary<int, (int Count, int FirstTarget)> _broken = [];
    private readonly HashSet<(int Target, ulong Fragment)> _wanted = [];

    public void Observe(AnalyzedPage page)
    {
        HashSet<ulong>? ownIds = null;
        foreach (var link in page.Links)
        {
            if (link.Kind != LinkKind.Hyperlink || link.Fragment is null || Checkable(link.Fragment) is not { } fragment)
            {
                continue;
            }

            if (link.ToId == page.UrlId)
            {
                if (page.Facts.AnchorIds.Count < PageWalker.MAX_IDS && !(ownIds ??= HashIds(page.Facts.AnchorIds)).Contains(Hash(fragment)))
                {
                    Record(page.UrlId, link.ToId);
                }
            }
            else if (urls[link.ToId] is { IsInternal: true, IsPage: true })
            {
                _wanted.Add((link.ToId, Hash(fragment)));
            }
        }
    }

    public IEnumerable<IssueHit> Finish()
    {
        HashSet<(int Target, ulong Fragment)> missing = [];
        foreach (var target in _wanted.GroupBy(pair => pair.Target))
        {
            // A capped id list can't prove an id is missing.
            if (reader.ReadFacts(target.Key) is not { } facts || facts.AnchorIds.Count >= PageWalker.MAX_IDS)
            {
                continue;
            }

            var ids = HashIds(facts.AnchorIds);
            missing.UnionWith(target.Where(pair => !ids.Contains(pair.Fragment)));
        }

        if (missing.Count > 0)
        {
            foreach (var (from, to, raw) in reader.ReadFragmentLinks())
            {
                if (Checkable(raw) is { } fragment && missing.Contains((to, Hash(fragment))))
                {
                    Record(from, to);
                }
            }
        }

        foreach (var (pageId, (count, firstTarget)) in _broken)
        {
            yield return new IssueHit(IssueCatalog.BROKEN_FRAGMENT_LINKS, pageId, RuleText.Count(count, "link", "links"), firstTarget);
        }
    }

    /// <summary>
    /// The decoded fragment, or null when it doesn't name an element: "top" scrolls to the top without one (HTML
    /// spec), "!" and "/" start app routes, and "=", ":" or "&amp;" mean app state or text fragments (Elementor's
    /// #elementor-action%3A... arrives encoded, hence decoding first).
    /// </summary>
    private static string? Checkable(string raw)
    {
        var fragment = Uri.UnescapeDataString(raw);
        return fragment.Length == 0 || fragment.Equals("top", StringComparison.OrdinalIgnoreCase) || fragment[0] is '!' or '/' ||
               fragment.IndexOfAny(['=', ':', '&']) >= 0
            ? null
            : fragment;
    }

    private static HashSet<ulong> HashIds(IReadOnlyList<string> ids) => ids.Select(Hash).ToHashSet();

    private static ulong Hash(string text) => XxHash3.HashToUInt64(MemoryMarshal.AsBytes(text.AsSpan()));

    private void Record(int pageId, int targetId) =>
        _broken[pageId] = _broken.TryGetValue(pageId, out var current) ? (current.Count + 1, current.FirstTarget) : (1, targetId);
}
