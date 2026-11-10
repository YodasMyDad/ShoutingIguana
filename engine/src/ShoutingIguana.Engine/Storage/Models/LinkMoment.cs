using System.Globalization;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// One moment of a running crawl, as the links grid answers for it: how many links and URLs the crawl had, and their
/// highest ids, all read from one snapshot. A crawl adds links and never changes one, so the links up to that highest id
/// are the same links whenever they're read afterwards: whatever a read counts for the moment (a filter set's links,
/// their tally, a sort's order), it counts within these bounds, and every number of an answer is then of the one moment,
/// however many reads' snapshots went into it. The one exception is a page the crawl fetches again, whose links it
/// stores again: a read that finds links the moment counted gone starts a moment of its own.
/// </summary>
/// <param name="Id">Which moment this is, among every one taken: what's counted for it is kept under it and no other.</param>
/// <param name="Shared">Other reads count for it too, from their own later snapshots; a read's own answers only from its snapshot.</param>
internal sealed record LinkMoment(long Id, long Links, long MaxLinkId, long Urls, long MaxUrlId, bool Shared)
{
    private static long _taken;

    /// <summary>The next moment's id.</summary>
    public static long NextId() => Interlocked.Increment(ref _taken);

    /// <summary>What's kept for this moment is kept under this.</summary>
    public string Key { get; } = string.Create(CultureInfo.InvariantCulture, $"moment {Id}|");
}
