using ShoutingIguana.Engine.Parsing;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>What the near-duplicate check keeps of a page: its URL id, its exact content hash, and its 256-bit fingerprint.</summary>
/// <param name="Lane0">The first lane, <see cref="PageFacts.SimHash"/>.</param>
internal readonly record struct PageFingerprint(int UrlId, ulong ContentHash, ulong Lane0, SimHashLanes Lanes)
{
    /// <summary>One 64-bit lane of the fingerprint, 0 to 3.</summary>
    public ulong Lane(int lane) => lane switch
    {
        0 => Lane0,
        1 => Lanes.Lane1,
        2 => Lanes.Lane2,
        3 => Lanes.Lane3,
        _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, "A fingerprint has lanes 0 to 3.")
    };

    /// <summary>Differing bits out of all 256.</summary>
    public int Distance(PageFingerprint other) => ContentFingerprint.Distance(Lane0, Lanes, other.Lane0, other.Lanes);
}
