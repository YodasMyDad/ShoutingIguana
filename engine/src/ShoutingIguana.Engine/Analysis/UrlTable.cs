using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Analysis;

/// <summary>
/// The status of every URL in the crawl, in arrays indexed by URL id, so link checks are lookups rather than joins
/// over millions of link rows. It also counts, during the page pass, the links and the pages that link to each URL.
/// Roughly 70 bytes per URL.
/// <para>
/// Statuses are what search engines make of each URL, which the crawl can't always know when it fetches a page.
/// Google treats an instant meta refresh as a permanent redirect, so such a page becomes a redirect here. A canonical
/// that leads back to the page itself (/page/ names /page, which redirects to /page/) or to a URL that doesn't work
/// leaves the page indexable: the redirect is the stronger signal, and a broken canonical is ignored.
/// </para>
/// </summary>
internal sealed class UrlTable
{
    /// <summary>Redirects followed before a trace stops: as many as Chrome and Firefox follow before giving up.</summary>
    public const int MAX_REDIRECT_HOPS = 20;

    /// <summary>Canonicals followed, page to page, before a trace gives up looking for a loop.</summary>
    public const int MAX_CANONICAL_STEPS = 10;

    private readonly UrlStatus[] _statuses;
    private readonly int[] _followedInlinks;
    private readonly int[] _indexableInlinks;
    private readonly int[] _nofollowInlinks;
    private readonly int[] _inlinks;
    private readonly int[] _nofollowLinks;
    private readonly int[] _linkingPages;
    private readonly int[] _usedOn;
    private readonly HashSet<int> _pageTargets = [];

    // Each canonicalised page's canonical target, and the pages whose canonicals, followed page to page, come back round.
    private readonly Dictionary<int, int> _canonicalTargets = [];
    private readonly HashSet<int> _canonicalLoops = [];
    private readonly List<(int UrlId, IndexabilityReason Reason)> _indexabilityChanges = [];

    /// <param name="instantRefreshes">Parsed pages with a 0-second meta refresh to another URL.</param>
    /// <param name="canonicals">Pages the crawl recorded as canonicalised, with the target of their first honoured canonical.</param>
    public UrlTable(UrlStatus[] statuses, int startId, IEnumerable<(int Page, int Target)>? instantRefreshes = null,
        IEnumerable<(int Page, int Target)>? canonicals = null)
    {
        _statuses = statuses;
        _followedInlinks = new int[statuses.Length];
        _indexableInlinks = new int[statuses.Length];
        _nofollowInlinks = new int[statuses.Length];
        _inlinks = new int[statuses.Length];
        _nofollowLinks = new int[statuses.Length];
        _linkingPages = new int[statuses.Length];
        _usedOn = new int[statuses.Length];
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

        List<(int Page, int Target)> canonicalised = [.. canonicals ?? []];
        foreach (var (page, target) in canonicalised)
        {
            if (_statuses[page] is { IndexabilityReason: IndexabilityReason.Canonicalised } status &&
                (TraceRedirects(target).FinalId == page || IsBrokenThroughRedirects(target)))
            {
                _statuses[page] = status with { IndexabilityReason = IndexabilityReason.None };
                _indexabilityChanges.Add((page, IndexabilityReason.None));
            }
        }

        foreach (var (page, target) in canonicalised)
        {
            if (_statuses[page].IndexabilityReason == IndexabilityReason.Canonicalised)
            {
                _canonicalTargets.TryAdd(page, target);
            }
        }

        FindCanonicalLoops();

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

    /// <summary>
    /// The pages the page pass reads, as <c>AnalysisReader.ReadPages</c> chooses them: the site's HTML pages whose body was
    /// parsed, a page that moves on with an instant meta refresh included. A count for the analysis's progress.
    /// </summary>
    public int PagesToAudit()
    {
        var pages = 0;
        for (var id = 1; id < _statuses.Length; id++)
        {
            if (_statuses[id] is { IsInternal: true, IsHtml: true, IsParsed: true } status && (status.Outcome == FetchOutcome.Success || status.IsMetaRefresh))
            {
                pages++;
            }
        }

        return pages;
    }

    /// <summary>
    /// The URLs crawled, as the library and the URL grid count them: every URL with an outcome, less those never asked
    /// because their host had stopped answering. What a check on any URL flags is a share of these.
    /// </summary>
    public int CountCrawled() => _statuses.Count(status => status.Fetched);

    /// <summary>
    /// The site's pages the crawl tried to read: <see cref="Unread"/>, the site's URLs other than images and files that
    /// refused the crawler (a firewall, a bot challenge) or gave no answer, or that the site kept rate limiting (stored as
    /// not checked after the crawl's last tries), so their links are unknown; and <see cref="Read"/>, its HTML pages that
    /// were. The crawler's probes of the site's other versions aren't counted, nor URLs robots.txt kept it from, which
    /// search engines can't read either.
    /// </summary>
    public (int Read, int Unread) CountPagesRead()
    {
        int read = 0, unread = 0;
        foreach (var status in _statuses)
        {
            if (status is { IsInternal: true, IsRateLimited: true, IsResource: false, IsSiteVersionProbe: false })
            {
                unread++;
                continue;
            }

            if (status is not { IsInternal: true, Fetched: true, IsSiteVersionProbe: false })
            {
                continue;
            }

            if (status.IsPage)
            {
                read++;
            }
            else if (!status.IsResource && status.Outcome is FetchOutcome.Blocked or FetchOutcome.Timeout or FetchOutcome.NetworkError)
            {
                unread++;
            }
        }

        return (read, unread);
    }

    /// <summary>The site's URLs robots.txt kept the crawler out of, as the blocked-by-robots check flags them.</summary>
    public int CountBlockedByRobots() =>
        _statuses.Count(status => status is { IsInternal: true, Fetched: true, Outcome: FetchOutcome.RobotsDisallowed, IsSiteVersionProbe: false });

    public ref readonly UrlStatus this[int id] => ref _statuses[id];

    /// <summary>Pages with a followed link to this URL, not counting the URL itself.</summary>
    public int FollowedInlinks(int id) => _followedInlinks[id];

    /// <summary>Of <see cref="FollowedInlinks"/>, the pages that can be indexed themselves.</summary>
    public int IndexableInlinks(int id) => _indexableInlinks[id];

    /// <summary>Pages whose only links to this URL are nofollow.</summary>
    public int NofollowInlinks(int id) => _nofollowInlinks[id];

    /// <summary>
    /// Hyperlinks to this URL on the pages audited, each one counted, not counting a page's links to itself. Unlike the
    /// counts above, nothing is credited through redirects: these are the links as found.
    /// </summary>
    public int Inlinks(int id) => _inlinks[id];

    /// <summary>
    /// Of <see cref="Inlinks"/>, the ones search engines don't follow: marked rel=nofollow, or on a page whose robots
    /// directives say nofollow, as the crawler judges it.
    /// </summary>
    public int NofollowLinks(int id) => _nofollowLinks[id];

    /// <summary>The pages behind <see cref="Inlinks"/>: each page that links to this URL, once.</summary>
    public int LinkingPages(int id) => _linkingPages[id];

    /// <summary>The pages audited that link to this URL by any kind of link, each once, not counting the URL itself.</summary>
    public int UsedOn(int id) => _usedOn[id];

    /// <summary>Adds a page's hyperlinks to the inlink counts: every link, and every page it links to once.</summary>
    public void CountInlinks(AnalyzedPage page)
    {
        _pageTargets.Clear();
        var pageNoFollow = page.Facts.Robots.NoFollow;
        foreach (var link in page.Links)
        {
            if (link.Kind != LinkKind.Hyperlink || link.ToId == page.UrlId)
            {
                continue;
            }

            _inlinks[link.ToId]++;
            if (pageNoFollow || link.Flags.HasFlag(LinkFlags.NoFollow))
            {
                _nofollowLinks[link.ToId]++;
            }

            if (!link.Flags.HasFlag(LinkFlags.NoFollow))
            {
                _pageTargets.Add(link.ToId);
            }
        }

        foreach (var target in _pageTargets)
        {
            _followedInlinks[target]++;
            if (page.IsIndexable)
            {
                _indexableInlinks[target]++;
            }
        }

        foreach (var link in page.Links)
        {
            if (link.Kind == LinkKind.Hyperlink && link.ToId != page.UrlId && link.Flags.HasFlag(LinkFlags.NoFollow) &&
                _pageTargets.Add(link.ToId))
            {
                _nofollowInlinks[link.ToId]++;
            }
        }

        // Every target is in the set by now, followed or not.
        foreach (var target in _pageTargets)
        {
            _linkingPages[target]++;
        }

        // Then every other kind of link: a URL the page uses (an image, a script, its canonical) counts the page once.
        foreach (var link in page.Links)
        {
            if (link.ToId != page.UrlId)
            {
                _pageTargets.Add(link.ToId);
            }
        }

        foreach (var target in _pageTargets)
        {
            _usedOn[target]++;
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
                _indexableInlinks[final] += _indexableInlinks[id];
                _nofollowInlinks[final] += _nofollowInlinks[id];
            }
        }
    }

    /// <summary>
    /// The page is on a canonical loop: its canonical names a page whose canonical names another, and so on (through
    /// redirects), until one names a page already passed, within <see cref="MAX_CANONICAL_STEPS"/>. Only the pages on the
    /// loop itself are; a page whose canonical leads into one isn't.
    /// </summary>
    public bool IsOnCanonicalLoop(int id) => _canonicalLoops.Contains(id);

    /// <summary>The page a canonicalised page names as its canonical, or 0 for a page that isn't canonicalised.</summary>
    public int CanonicalTarget(int id) => _canonicalTargets.GetValueOrDefault(id);

    /// <summary>The pages of the canonical loop a page is on, in the order its canonicals lead, starting with it; empty when it's on none.</summary>
    public IReadOnlyList<int> CanonicalLoop(int id)
    {
        if (!_canonicalLoops.Contains(id))
        {
            return [];
        }

        List<int> loop = [id];
        for (var next = NextCanonicalPage(id); next is { } page && page != id && loop.Count <= MAX_CANONICAL_STEPS; next = NextCanonicalPage(page))
        {
            loop.Add(page);
        }

        return loop;
    }

    /// <summary>The canonicalised page a canonicalised page's canonical leads to, through redirects, or null when it leads to no such page.</summary>
    private int? NextCanonicalPage(int page)
    {
        var next = TraceRedirects(_canonicalTargets[page]).FinalId;
        return _canonicalTargets.ContainsKey(next) ? next : null;
    }

    /// <summary>
    /// Follows every canonicalised page's canonical, page to page, and marks the pages of each loop found. Each page is
    /// walked from once, whatever the order, so the result doesn't depend on which page the crawl read first.
    /// </summary>
    private void FindCanonicalLoops()
    {
        HashSet<int> done = [];
        List<int> path = [];
        foreach (var start in _canonicalTargets.Keys)
        {
            path.Clear();
            int? current = start;
            while (current is { } page && !done.Contains(page) && path.Count <= MAX_CANONICAL_STEPS)
            {
                var at = path.IndexOf(page);
                if (at >= 0)
                {
                    _canonicalLoops.UnionWith(path.Skip(at));
                    break;
                }

                path.Add(page);
                current = NextCanonicalPage(page);
            }

            done.UnionWith(path);
        }
    }

    /// <summary>
    /// Follows recorded redirects from a URL until one is not a redirect, comes back on itself, or is unknown. A chain
    /// longer than <see cref="MAX_REDIRECT_HOPS"/> stops there, still at a redirect: long, but not a loop.
    /// </summary>
    public RedirectTrace TraceRedirects(int id)
    {
        // Most URLs aren't redirects, and link checks trace every target, so skip the bookkeeping for them.
        if (_statuses[id] is not { Fetched: true, Outcome: FetchOutcome.Redirect, RedirectTargetId: > 0 })
        {
            return new RedirectTrace(id, 0, IsLoop: false);
        }

        HashSet<int> visited = [];
        var current = id;
        var hops = 0;
        while (hops < MAX_REDIRECT_HOPS && _statuses[current] is { Fetched: true, Outcome: FetchOutcome.Redirect, RedirectTargetId: > 0 } redirect)
        {
            if (!visited.Add(current))
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
