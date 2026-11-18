using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Sidecar.Events.Models;

/// <summary>
/// A recheck's own progress (the fix loop): as it starts, at most every 500 ms while it fetches, as its run ends, and once
/// more with <paramref name="Result"/> when the analysis after it has settled it. Never merged or dropped.
/// </summary>
/// <param name="Phase">1, then 2 for the pages referring to what still fails; null before its first progress.</param>
/// <param name="Done">Its fetches done in the phase.</param>
/// <param name="Total">Its fetches in the phase.</param>
/// <param name="Result">What it found, once settled; null until then.</param>
internal sealed record RecheckEventDto(string CrawlId, int RecheckId, RecheckStatus Status, int? Phase, int Done, int Total, RecheckResult? Result);
