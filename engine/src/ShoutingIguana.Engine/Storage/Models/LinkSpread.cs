using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// How a filter set's links spread over the pages they're on, counted by target as they're read in crawl order: the pages
/// linking to each target, the positions its links sit in, and the pages the links are on in all. The links grouped by
/// target are read from it (<see cref="LinkGridReader.Groups"/>).
/// <para>
/// A page's links are stored together, one after another (the crawl writes a page's links in one go, and a page fetched
/// again has its old links taken away and its new ones stored after every other), so a target's links from one page come
/// one after another in crawl order: a page is counted for a target when the page before it there was another. That
/// takes a number a target, never a set of pages. A pass split by link id counts each stretch apart, and the page that
/// straddles two stretches is counted once when they're put together (<see cref="Merge"/>).
/// </para>
/// </summary>
internal sealed class LinkSpread
{
    /// <summary>The bit for a position this engine has no name for.</summary>
    public const int UNKNOWN_POSITION = 7;

    /// <summary>The most targets counted one by one: as <see cref="LinkTally"/>.</summary>
    private const long MAX_TARGETS = 1L << 26;

    private static readonly int POSITIONS = Enum.GetValues<LinkPosition>().Length;

    private readonly int[] _pages;
    private readonly byte[] _positions;

    /// <summary>For each target, the last page counted for it; 0 for none, as URL ids start at 1. Let go once the pass is put together.</summary>
    private int[]? _lastPage;

    /// <summary>The targets counted for the first page this stretch read, while it was still reading it.</summary>
    private readonly List<int> _headTargets = [];

    private long _firstPage = -1;
    private long _lastFrom = -1;
    private bool _inHead = true;

    /// <param name="largestUrlId">The largest URL id in the crawl.</param>
    /// <param name="urls">How many URLs it has: as <see cref="LinkTally"/>'s, a crafted file's ids far past it are counted together.</param>
    public LinkSpread(long largestUrlId, long urls)
    {
        var size = largestUrlId is >= 0 and <= MAX_TARGETS && largestUrlId <= 4 * urls + 4_096 ? largestUrlId + 1 : 1;
        _pages = new int[size];
        _positions = new byte[size];
        _lastPage = new int[size];
    }

    /// <summary>The pages the links are on, each once.</summary>
    public long Pages { get; private set; }

    /// <summary>The pages linking to a target.</summary>
    public int PagesTo(long urlId) => urlId > 0 && urlId < _pages.Length ? _pages[urlId] : 0;

    /// <summary>
    /// The positions a target's links sit in, a bit each by <see cref="LinkPosition"/>, and
    /// <see cref="UNKNOWN_POSITION"/> for one this engine has no name for.
    /// </summary>
    public int PositionsOf(long urlId) => urlId > 0 && urlId < _positions.Length ? _positions[urlId] : 0;

    /// <summary>One link, read in crawl order.</summary>
    public void Add(long toId, long fromId, long position)
    {
        if (fromId != _lastFrom)
        {
            if (_firstPage < 0)
            {
                _firstPage = fromId;
            }
            else
            {
                _inHead = false;
            }

            _lastFrom = fromId;
            Pages++;
        }

        var slot = toId > 0 && toId < _pages.Length ? (int)toId : 0;
        var page = unchecked((int)fromId);
        if (_lastPage![slot] != page)
        {
            _lastPage[slot] = page;
            _pages[slot]++;
            if (_inHead)
            {
                _headTargets.Add(slot);
            }
        }

        _positions[slot] |= (byte)(1 << (position >= 0 && position < POSITIONS ? (int)position : UNKNOWN_POSITION));
    }

    /// <summary>
    /// Adds the counts of the stretch of links that comes next in crawl order. Where its first page is the last this one
    /// read, the page's links straddle the two: the page is counted once, and once for each target both counted it for.
    /// </summary>
    public void Merge(LinkSpread next)
    {
        if (next._firstPage >= 0 && next._firstPage == _lastFrom)
        {
            Pages--;
            var page = unchecked((int)next._firstPage);
            foreach (var slot in next._headTargets.Where(slot => slot < _pages.Length && _lastPage![slot] == page))
            {
                _pages[slot]--;
            }
        }

        Pages += next.Pages;
        for (var slot = 0; slot < _pages.Length && slot < next._pages.Length; slot++)
        {
            _pages[slot] += next._pages[slot];
            _positions[slot] |= next._positions[slot];
            if (next._lastPage![slot] != 0)
            {
                _lastPage![slot] = next._lastPage[slot];
            }
        }

        if (next._lastFrom >= 0)
        {
            (_lastFrom, _firstPage) = (next._lastFrom, _firstPage < 0 ? next._firstPage : _firstPage);
        }
    }

    /// <summary>Lets go of what only putting stretches together needs, once they are: what's kept is a number and a byte a target.</summary>
    public LinkSpread Seal()
    {
        _lastPage = null;
        _headTargets.Clear();
        return this;
    }
}
