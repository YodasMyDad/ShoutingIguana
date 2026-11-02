using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Sidecar.Crawls.Models;
using ShoutingIguana.Sidecar.Library.Models;

namespace ShoutingIguana.Sidecar.Crawls;

/// <summary>
/// The histories of crawls that aren't live here, as counted back from their files, each kept until its file changes:
/// its stamp (size and modified time, its write-ahead log's too) is the library summaries' own. Counting back reads every
/// response, over half a second at a million URLs, and a closed crawl's history never changes, so opening its chart again
/// costs nothing. Kept in memory, so always this engine's reading, and only the most recently used few.
/// </summary>
internal sealed class HistoryCache(int capacity = HistoryCache.CAPACITY)
{
    public const int CAPACITY = 16;

    private readonly Lock _sync = new();
    private readonly Dictionary<string, CachedHistory> _histories = new(StringComparer.Ordinal);
    private long _uses;

    /// <summary>
    /// The crawl's history as its file is now: kept, or <paramref name="read"/> afresh outside the lock. It is kept under
    /// the stamp taken before reading, so a file that changes while it's read is read again next time.
    /// </summary>
    public CrawlHistory Get(string id, string path, Func<CrawlHistory> read)
    {
        var stamp = FileStamp.Of(path);
        lock (_sync)
        {
            if (stamp is not null && _histories.TryGetValue(id, out var cached) && cached.Stamp == stamp)
            {
                _histories[id] = cached with { LastUse = ++_uses };
                return cached.History;
            }
        }

        var history = read();
        if (stamp is null)
        {
            return history;
        }

        lock (_sync)
        {
            _histories[id] = new CachedHistory(stamp, history, ++_uses);
            if (_histories.Count > capacity)
            {
                _histories.Remove(_histories.MinBy(entry => entry.Value.LastUse).Key);
            }
        }

        return history;
    }

    /// <summary>For changes this process made, which a modified time may be too coarse to show.</summary>
    public void Invalidate(string id)
    {
        lock (_sync)
        {
            _histories.Remove(id);
        }
    }
}
