namespace ShoutingIguana.Engine.Parsing.Models;

/// <summary>
/// Lanes 1 to 3 of a page's 256-bit content fingerprint: three more 64-bit SimHashes of the same 3-word shingles as the
/// first lane (<see cref="PageFacts.SimHash"/>), each from token hashes with a seed of its own, so their bits are
/// independent of the first lane's and of each other's. All four together measure how alike two pages are four times as
/// precisely as one (see <see cref="ContentFingerprint"/>).
/// </summary>
public readonly record struct SimHashLanes(ulong Lane1, ulong Lane2, ulong Lane3);
