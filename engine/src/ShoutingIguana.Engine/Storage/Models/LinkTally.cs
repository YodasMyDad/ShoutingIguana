using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// The links a filter set matches, counted two ways as they're read (from the link summary's rows, or from the links
/// themselves): by what they are (kind, position, followed or not), for the facets, and by target, for the target's
/// facets and for a sort by the target, which finds a page by adding up each target's links. Counted in plain arrays, as
/// it's done once per link read.
/// </summary>
internal sealed class LinkTally
{
    /// <summary>Kinds, positions and the three follow states pack into this many slots.</summary>
    public const int SHAPES = 1 << (KIND_BITS + POSITION_BITS + 2);

    public const int FOLLOWED = 0;
    public const int NOFOLLOW = 1;
    public const int NOT_HYPERLINK = 2;

    /// <summary>A kind or a position this engine doesn't know, as a shape holds it.</summary>
    public const int UNKNOWN_KIND = (1 << KIND_BITS) - 1;
    public const int UNKNOWN_POSITION = (1 << POSITION_BITS) - 1;

    /// <summary>The most targets counted one by one: 2^26 URLs are 256 MB of counts.</summary>
    private const long MAX_TARGETS = 1L << 26;

    private const int KIND_BITS = 4;
    private const int POSITION_BITS = 3;
    private static readonly int KINDS = Enum.GetValues<LinkKind>().Length;
    private static readonly int POSITIONS = Enum.GetValues<LinkPosition>().Length;

    private readonly int[] _byTarget;

    /// <param name="largestUrlId">The largest URL id in the crawl.</param>
    /// <param name="urls">
    /// How many URLs it has. The crawler numbers them from 1 with none missing; where ids run far past the count (a crafted
    /// file's), targets aren't counted one by one but together, as unknown, so no file can make a tally large.
    /// </param>
    public LinkTally(long largestUrlId, long urls) =>
        _byTarget = new int[largestUrlId is >= 0 and <= MAX_TARGETS && largestUrlId <= 4 * urls + 4_096 ? largestUrlId + 1 : 1];

    /// <summary>The links counted.</summary>
    public long Links { get; private set; }

    /// <summary>The links by <see cref="Shape"/>.</summary>
    public long[] ByShape { get; } = new long[SHAPES];

    /// <summary>The links to a URL.</summary>
    public int To(long urlId) => urlId > 0 && urlId < _byTarget.Length ? _byTarget[urlId] : 0;

    /// <summary>The links to no URL the tally counts one by one: a crafted file's, to an id the crawl doesn't have.</summary>
    public int ToUnknown => _byTarget[0];

    /// <summary>How many targets it counts one by one: ids below this.</summary>
    public int TargetLimit => _byTarget.Length;

    /// <summary>The targets with links, in id order.</summary>
    public IEnumerable<(int Target, int Links)> Targets()
    {
        for (var id = 1; id < _byTarget.Length; id++)
        {
            if (_byTarget[id] > 0)
            {
                yield return (id, _byTarget[id]);
            }
        }
    }

    public void Add(long toId, long kind, long position, long flags, bool sourceNofollow, long links)
    {
        Links += links;
        ByShape[Shape(kind, position, flags, sourceNofollow)] += links;
        var slot = toId > 0 && toId < _byTarget.Length ? toId : 0;
        _byTarget[slot] = (int)Math.Min(int.MaxValue, _byTarget[slot] + links);
    }

    /// <summary>Adds another tally's counts: the parts of one pass split over several connections, each of which counted its own stretch of the links.</summary>
    public void Merge(LinkTally other)
    {
        Links += other.Links;
        for (var shape = 0; shape < SHAPES; shape++)
        {
            ByShape[shape] += other.ByShape[shape];
        }

        for (var target = 0; target < _byTarget.Length && target < other._byTarget.Length; target++)
        {
            _byTarget[target] = (int)Math.Min(int.MaxValue, (long)_byTarget[target] + other._byTarget[target]);
        }
    }

    /// <summary>
    /// What a link is, as the facets count it: its kind and position, unknown when this engine has no name for them, and
    /// whether search engines follow it, which only a hyperlink can be asked: not when its <c>rel</c> says nofollow, nor
    /// when its page's robots directives do.
    /// </summary>
    public static int Shape(long kind, long position, long flags, bool sourceNofollow)
    {
        var knownKind = kind >= 0 && kind < KINDS ? (int)kind : UNKNOWN_KIND;
        var knownPosition = position >= 0 && position < POSITIONS ? (int)position : UNKNOWN_POSITION;
        var follow = kind != (int)LinkKind.Hyperlink ? NOT_HYPERLINK : (flags & (long)LinkFlags.NoFollow) != 0 || sourceNofollow ? NOFOLLOW : FOLLOWED;
        return knownKind | (knownPosition << KIND_BITS) | (follow << (KIND_BITS + POSITION_BITS));
    }

    public static int KindOf(int shape) => shape & UNKNOWN_KIND;

    public static int PositionOf(int shape) => (shape >> KIND_BITS) & UNKNOWN_POSITION;

    public static int FollowOf(int shape) => shape >> (KIND_BITS + POSITION_BITS);
}
