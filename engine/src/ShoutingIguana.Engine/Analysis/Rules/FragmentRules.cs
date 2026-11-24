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
/// once, and the links table is streamed to find the pages whose links miss. An issue names every other page whose
/// section is missing, and a section missing on the page itself as no other URL, so a template's "skip to #main" whose id
/// is missing is one group of the pages it's on, not a group per page.
/// </summary>
/// <param name="readFacts">The stored facts of a URL (<see cref="AnalysisReader.ReadFacts"/>).</param>
/// <param name="readFragmentLinks">Every link to a #fragment on another page (<see cref="AnalysisReader.ReadFragmentLinks"/>).</param>
/// <param name="readUrl">The stored URL with an id: a page's detail names the sections it links to that are missing.</param>
internal sealed class FragmentRules(UrlTable urls, Func<int, PageFacts?> readFacts, Func<IEnumerable<(int From, int To, string Fragment)>> readFragmentLinks,
    Func<int, string?> readUrl)
    : ISiteRule
{
    /// <summary>The missing sections a page's detail names, before it's cut to length.</summary>
    private const int NAMED = 4;

    /// <summary>The first few missing sections of each page's links, as (target, fragment as written), for its detail.</summary>
    private readonly Dictionary<int, List<(int Target, string Fragment)>> _named = [];

    /// <summary>
    /// Each page's links that miss, the other pages they miss on (0 for none yet; the first apart, as most have one), and
    /// whether one misses on the page itself.
    /// </summary>
    private readonly Dictionary<int, (int Count, int First, List<int>? Others, bool Itself)> _broken = [];

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
                    Record(page.UrlId, link.ToId, link.Fragment);
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
                    Record(from, to, raw);
                }
            }
        }

        foreach (var (pageId, (count, first, others, itself)) in _broken)
        {
            var detail = Detail(pageId, count);
            yield return first == 0
                ? new IssueHit(IssueCatalog.BROKEN_FRAGMENT_LINKS, pageId, detail)
                : new IssueHit(IssueCatalog.BROKEN_FRAGMENT_LINKS, pageId, detail, first)
                {
                    Targets = itself ? [first, .. others ?? [], IssueHit.NO_OTHER_URL] : [first, .. others ?? []]
                };
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

    /// <summary>
    /// Whether a link to a #fragment, as written, misses on a page with these ids, as the check judges it: a fragment it
    /// checks that matches no id as written or decoded. Never on a page whose ids were capped, which can't prove one missing.
    /// </summary>
    internal static bool Misses(string raw, IReadOnlyList<string> ids) =>
        ids.Count < PageWalker.MAX_IDS && Checkable(raw) is { } fragment && !ids.Contains(raw, StringComparer.Ordinal) && !ids.Contains(fragment, StringComparer.Ordinal) &&
        !ids.Any(id => id.Contains('%') && string.Equals(UpperEscapes(id), raw, StringComparison.Ordinal));

    /// <summary>
    /// The ids, and each id holding percent escapes once more with their hex digits in capitals: a link's fragment is
    /// stored that way (the URL's normal form), so id="%e0%b9%81" is found by the href="#%e0%b9%81" written beside it.
    /// </summary>
    private static HashSet<ulong> HashIds(IReadOnlyList<string> ids)
    {
        var hashes = ids.Select(Hash).ToHashSet();
        hashes.UnionWith(ids.Where(id => id.Contains('%')).Select(id => Hash(UpperEscapes(id))));
        return hashes;
    }

    private static string UpperEscapes(string id) => string.Create(id.Length, id, static (span, text) =>
    {
        for (var i = 0; i < text.Length; i++)
        {
            var escaped = (i >= 1 && text[i - 1] == '%') || (i >= 2 && text[i - 2] == '%');
            span[i] = escaped && Uri.IsHexDigit(text[i]) ? char.ToUpperInvariant(text[i]) : text[i];
        }
    });

    private static ulong Hash(string text) => XxHash3.HashToUInt64(MemoryMarshal.AsBytes(text.AsSpan()));

    /// <summary>"2 links to missing sections: /faq/#specs, #reviews": a section missing on the page itself is its #fragment alone.</summary>
    private string Detail(int pageId, int count)
    {
        var pageUrl = readUrl(pageId) ?? string.Empty;
        var named = _named.GetValueOrDefault(pageId) ?? [];
        var sections = named.Select(entry => entry.Target == pageId ? $"#{entry.Fragment}" : $"{RuleText.Near(readUrl(entry.Target) ?? string.Empty, pageUrl)}#{entry.Fragment}");
        return RuleText.Fit($"{RuleText.Count(count, "link", "links")} to missing sections: {string.Join(", ", sections)}");
    }

    private void Record(int pageId, int targetId, string fragment)
    {
        var named = _named.TryGetValue(pageId, out var list) ? list : _named[pageId] = [];
        if (named.Count < NAMED && !named.Contains((targetId, fragment)))
        {
            named.Add((targetId, fragment));
        }

        var (count, first, others, itself) = _broken.GetValueOrDefault(pageId);
        if (targetId == pageId)
        {
            itself = true;
        }
        else
        {
            if (first == 0)
            {
                first = targetId;
            }
            else if (targetId != first && others?.Contains(targetId) != true)
            {
                (others ??= []).Add(targetId);
            }
        }

        _broken[pageId] = (count + 1, first, others, itself);
    }
}
