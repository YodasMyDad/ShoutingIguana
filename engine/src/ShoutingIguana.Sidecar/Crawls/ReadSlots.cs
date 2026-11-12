using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Sidecar.Models;

namespace ShoutingIguana.Sidecar.Crawls;

/// <summary>
/// How many reads of crawl files run at once: one a core (<see cref="SidecarOptions.GridReads"/>), as each keeps a core
/// busy. A grid read takes one slot, queueing for it; a read that can split a pass over several connections takes more
/// only from those free at that moment, never waiting, and never the last one free, so a read that arrives while the
/// pass runs still starts at once.
/// </summary>
internal sealed class ReadSlots(SidecarOptions options)
{
    /// <summary>The slots free now: a read waits on it for its own.</summary>
    public SemaphoreSlim Free { get; } = new(options.GridReads, options.GridReads);

    /// <summary>The slots a read may take beside its own, as the engine's readers ask for them.</summary>
    public ReadHelpers Helpers => new(TryTakeHelper, () => Free.Release());

    /// <summary>
    /// Takes a slot for a read that has its own already, if that leaves one free: true when it did. It never waits. Reads
    /// arrive, and other passes ask, at the same moment, so having taken one it looks again: if that was the last after
    /// all (a read took the one it counted on leaving), it gives the slot back and goes without.
    /// </summary>
    internal bool TryTakeHelper()
    {
        if (Free.CurrentCount <= 1 || !Free.Wait(0))
        {
            return false;
        }

        if (Free.CurrentCount > 0)
        {
            return true;
        }

        Free.Release();
        return false;
    }
}
