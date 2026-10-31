using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Analysis;

/// <summary>
/// The status of every URL in the crawl, in arrays indexed by URL id, so link checks are lookups rather than joins
/// over millions of link rows. It also counts, during the page pass, how many pages link to each URL. Roughly 60
/// bytes per URL.
/// <para>
/// Statuses are what search engines make of each URL, which the crawl can't always know when it fetches a page.
/// Google treats an instant meta refresh as a permanent redirect, so such a page becomes a redirect here. A canonical
/// that leads back to the page itself (/page/ names /page, which redirects to /page/) or to a URL that doesn't work
/// leaves the page indexable: the redirect is the stronger signal, and a broken canonical is ignored.
/// </para>
/// </summary>
internal sealed class UrlTable
{
    private const int MAX_REDIRECT_HOPS = 20;

    private readonly UrlStatus[] _statuses;
    private readonly int[] _followedInlinks;
    private readonly int[] _nofollowInlinks;
    private readonly HashSet<int> _pageTargets = [];
    private readonly List<(int UrlId, IndexabilityReason Reason)> _indexabilityChanges = [];

    /// <param name="instantRefreshes">Parsed pages with a 0-second meta refresh to another URL.</param>
    /// <param name="canonicals">Pages the crawl recorded as canonicalised, with the target of their first honoured canonical.</param>
    public UrlTable(UrlStatus[] statuses, int startId, IEnumerable<(int Page, int Target)>? instantRefreshes = null,
        IEnumerable<(int Page, int Target)>? canonicals = null)
    {
        _statuses = statuses;
        _followedInlinks = new int[statuses.Length];
        _nofollowInlinks = new int[statuses.Length];
        StartId = startId;

        // Refreshes first: a canonical can lead back to its page through one.
        foreach (var (page, target) in instantRefreshes ?? [])
        {
            if (page != target && _statuses[page] is { IsInternal: true, IsPage: true } status)
            {
                _statuses[page] = status with
                {
                    Outcome = FetchOutcome.Redirect, RedirectTargetId = target, IndexabilityReason = IndexabilityReason.Redirected, IsMetaRefresh = true
                };
                _indexabilityChanges.Add((page, IndexabilityReason.Redirected));
            }
        }

        foreach (var (page, target) in canonicals ?? [])
        {
            if (_statuses[page] is { IndexabilityReason: IndexabilityReason.Canonicalised } status &&
                (TraceRedirects(target).FinalId == page || IsBrokenThroughRedirects(target)))
            {
                _statuses[page] = status with { IndexabilityReason = IndexabilityReason.None };
                _indexabilityChanges.Add((page, IndexabilityReason.None));
            }
        }

        var home = startId == 0 ? 0 : TraceRedirects(startId).FinalId;
        HomeId = home != 0 && _statuses[home] is { IsInternal: true, IsPage: true } ? home : startId;
    }

    public int StartId { get; }

    /// <summary>The start URL after its redirects: the page site-wide checks are reported on.</summary>
    public int HomeId { get; }

    /// <summary>URLs whose indexability differs from what the crawl recorded, with the reason search engines will see.</summary>
    public IReadOnlyList<(int UrlId, IndexabilityReason Reason)> IndexabilityChanges => _indexabilityChanges;

    /// <summary>Highest URL id plus one.</summary>
    public int Length => _statuses.Length;

    public ref readonly UrlStatus this[int id] => ref _statuses[id];

    /// <summary>Pages with a followed link to this URL, not counting the URL itself.</summary>
    public int FollowedInlinks(int id) => _followedInlinks[id];

    /// <summary>Pages whose only links to this URL are nofollow.</summary>
    public int NofollowInlinks(int id) => _nofollowInlinks[id];

    /// <summary>Adds a page's hyperlinks to the inlink counts, once per target page.</summary>
    public void CountInlinks(AnalyzedPage page)
    {
        _pageTargets.Clear();
        foreach (var link in page.Links)
        {
            if (link.Kind == LinkKind.Hyperlink && link.ToId != page.UrlId && !link.Flags.HasFlag(LinkFlags.NoFollow))
            {
                _pageTargets.Add(link.ToId);
            }
        }

        foreach (var target in _pageTargets)
        {
            _followedInlinks[target]++;
        }

        foreach (var link in page.Links)
        {
            if (link.Kind == LinkKind.Hyperlink && link.ToId != page.UrlId && link.Flags.HasFlag(LinkFlags.NoFollow) &&
                _pageTargets.Add(link.ToId))
            {
                _nofollowInlinks[link.ToId]++;
            }
        }
    }

    /// <summary>
    /// Credits links to a redirecting URL to where the redirects end, so a menu link to /services that redirects to
    /// /services/ still counts for /services/. Call once, after every page has been counted.
    /// </summary>
    public void CreditRedirectTargets()
    {
        for (var id = 1; id < _statuses.Length; id++)
        {
            if (_statuses[id] is not { Fetched: true, Outcome: FetchOutcome.Redirect } || (_followedInlinks[id] == 0 && _nofollowInlinks[id] == 0))
            {
                continue;
            }

            var final = TraceRedirects(id).FinalId;
            if (final != id)
            {
                _followedInlinks[final] += _followedInlinks[id];
                _nofollowInlinks[final] += _nofollowInlinks[id];
            }
        }
    }

    /// <summary>Follows recorded redirects from a URL until one is not a redirect, comes back on itself, or is unknown.</summary>
    public RedirectTrace TraceRedirects(int id)
    {
        HashSet<int> visited = [];
        var current = id;
        var hops = 0;
        while (_statuses[current] is { Fetched: true, Outcome: FetchOutcome.Redirect, RedirectTargetId: > 0 } redirect)
        {
            if (!visited.Add(current) || hops == MAX_REDIRECT_HOPS)
            {
                return new RedirectTrace(current, hops, IsLoop: true);
            }

            current = redirect.RedirectTargetId;
            hops++;
        }

        return new RedirectTrace(current, hops, IsLoop: false);
    }

    /// <summary>
    /// The URL doesn't lead to a working response: it fails itself, or its redirects loop, stop at a redirect without a
    /// usable Location (or after more hops than browsers follow), or end at a URL that fails.
    /// </summary>
    public bool IsBrokenThroughRedirects(int id)
    {
        var trace = TraceRedirects(id);
        return trace.IsLoop || _statuses[trace.FinalId] is { IsBroken: true } or { Fetched: true, Outcome: FetchOutcome.Redirect };
    }
}
