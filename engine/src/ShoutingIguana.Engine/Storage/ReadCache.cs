using System.Collections.Concurrent;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// What the grid readers have counted, kept so that paging and scrolling don't count again: matching rows, facets, a
/// check's groups, and the URL ids a text filter or a search matched. Each entry is keyed by the crawl file, its stamp,
/// its last analysis and what was read, so a crawl continued or analysed again reads afresh; a crawl that's running is
/// never cached. Safe for many requests at once, and bounded: past its capacity a part starts again empty, the id sets,
/// which can be large, after only a few.
/// </summary>
public sealed class ReadCache(int capacity = ReadCache.DEFAULT_CAPACITY, int largeCapacity = ReadCache.DEFAULT_LARGE_CAPACITY)
{
    public const int DEFAULT_CAPACITY = 4_096;

    /// <summary>Id sets of up to <see cref="GridPlanner.ID_SET_CAP"/> URLs each: a few megabytes at most.</summary>
    public const int DEFAULT_LARGE_CAPACITY = 16;

    private readonly ConcurrentDictionary<string, object> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, object> _large = new(StringComparer.Ordinal);

    /// <summary>How many entries it holds: for tests.</summary>
    public int Count => _entries.Count + _large.Count;

    internal T GetOrAdd<T>(string key, Func<T> read) where T : notnull => GetOrAdd(_entries, capacity, key, read);

    internal T GetOrAddLarge<T>(string key, Func<T> read) where T : notnull => GetOrAdd(_large, largeCapacity, key, read);

    private static T GetOrAdd<T>(ConcurrentDictionary<string, object> entries, int limit, string key, Func<T> read) where T : notnull
    {
        if (entries.TryGetValue(key, out var cached) && cached is T value)
        {
            return value;
        }

        value = read();
        if (entries.Count >= limit)
        {
            entries.Clear();
        }

        entries[key] = value;
        return value;
    }
}
