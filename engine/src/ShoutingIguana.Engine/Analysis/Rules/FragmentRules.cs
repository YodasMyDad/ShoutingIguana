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
/// <param name="readFacts">The stored facts of a URL (<see cref="AnalysisReader.ReadFacts"/>).</param>
/// <param name="readFragmentLinks">Every link to a #fragment on another page (<see cref="AnalysisReader.ReadFragmentLinks"/>).</param>
internal sealed class FragmentRules(UrlTable urls, Func<int, PageFacts?> readFacts, Func<IEnumerable<(int From, int To, string Fragment)>> readFragmentLinks)
    : ISiteRule
{
    private readonly Dictionary<int, (int Count, int FirstTarget)> _broken = [];

    /// <summary>Each fragment as written and decoded: browsers look for an id matching either (see <see cref="Finds"/>).</summary>
    private readonly HashSet<(int Target, ulong Raw, ulong Decoded)> _wanted = [];

    public IReadOnlyList<IssueDefinition> Checks { get; } = [IssueCatalog.BROKEN_FRAGMENT_LINKS];

    public void Observe(AnalyzedPage page)
    {
        // A page that moves on at once with a meta refresh is a redirect nobody sees, so its links aren't checked.
        if (page.IsRedirect)
        {
            return;
        }

        HashSet<ulong>? ownIds = null;
        foreach (var link in page.Links)
        {
            if (link.Kind != LinkKind.Hyperlink || link.Fragment is null || Checkable(link.Fragment) is not { } fragment)
            {
                continue;
            }

            if (link.ToId == page.UrlId)
            {
                if (page.Facts.AnchorIds.Count < PageWalker.MAX_IDS && !Finds(ownIds ??= HashIds(page.Facts.AnchorIds), Hash(link.Fragment), Hash(fragment)))
                {
                    Record(page.UrlId, link.ToId);
                }
            }
            else if (urls[link.ToId] is { IsInternal: true, IsPage: true })
            {
                _wanted.Add((link.ToId, Hash(link.Fragment), Hash(fragment)));
            }
        }
    }

    public IEnumerable<IssueHit> Finish()
    {
        HashSet<(int Target, ulong Raw, ulong Decoded)> missing = [];
        foreach (var target in _wanted.GroupBy(pair => pair.Target))
        {
            // A capped id list can't prove an id is missing.
            if (readFacts(target.Key) is not { } facts || facts.AnchorIds.Count >= PageWalker.MAX_IDS)
            {
                continue;
            }

            var ids = HashIds(facts.AnchorIds);
            missing.UnionWith(target.Where(pair => !Finds(ids, pair.Raw, pair.Decoded)));
        }

        if (missing.Count > 0)
        {
            foreach (var (from, to, raw) in readFragmentLinks())
            {
                if (!urls[from].IsMetaRefresh && Checkable(raw) is { } fragment && missing.Contains((to, Hash(raw), Hash(fragment))))
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

    /// <summary>
    /// Browsers look for the fragment as written first, then decoded (HTML spec, "find a potential indicated element"), so
    /// id="%E3%81%AF" is found by #%E3%81%AF, and id="は" by #は however the link encodes it.
    /// </summary>
    private static bool Finds(HashSet<ulong> ids, ulong raw, ulong decoded) => ids.Contains(raw) || ids.Contains(decoded);

    private static HashSet<ulong> HashIds(IReadOnlyList<string> ids) => ids.Select(Hash).ToHashSet();

    private static ulong Hash(string text) => XxHash3.HashToUInt64(MemoryMarshal.AsBytes(text.AsSpan()));

    private void Record(int pageId, int targetId) =>
        _broken[pageId] = _broken.TryGetValue(pageId, out var current) ? (current.Count + 1, current.FirstTarget) : (1, targetId);
}
