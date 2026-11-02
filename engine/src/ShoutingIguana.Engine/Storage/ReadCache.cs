using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// What the grid readers have counted, kept so that paging and scrolling don't count again: matching rows, facets, a
/// check's groups, and the URL ids a text filter or a search matched. Each entry is keyed by the crawl file, its stamp,
/// its last analysis and what was read, so a crawl continued or analysed again reads afresh; a running crawl's are kept
/// for <see cref="LIVE_FOR"/> only, then counted again.
/// <list type="bullet">
/// <item>Safe for many requests at once, and each thing is counted once: a request that wants what another is counting
/// waits for that count. If the other's read stops (cancelled, or it failed), one of those waiting counts it instead.</item>
/// <item>Bounded: past its capacity, the entries used least recently go; the id sets, which can be large, after only a
/// few. A long key (a query's conditions with their values can run to 90 KB) is kept by its hash.</item>
/// </list>
/// </summary>
public sealed class ReadCache(int capacity = ReadCache.DEFAULT_CAPACITY, int largeCapacity = ReadCache.DEFAULT_LARGE_CAPACITY)
{
    public const int DEFAULT_CAPACITY = 4_096;

    /// <summary>Id sets, of 140 KB per million URLs each.</summary>
    public const int DEFAULT_LARGE_CAPACITY = 32;

    /// <summary>Keys longer than this are kept by their hash.</summary>
    private const int MAX_KEY_LENGTH = 256;

    /// <summary>How long a running crawl's counts are kept before they're counted again: the UI shows live counts at most four times a second.</summary>
    public static readonly TimeSpan LIVE_FOR = TimeSpan.FromSeconds(2);

    private readonly Store _entries = new(capacity);
    private readonly Store _large = new(largeCapacity);

    /// <summary>How many entries it holds, counted or being counted: for tests.</summary>
    public int Count => _entries.Count + _large.Count;

    /// <summary>Tests only: the characters its keys take.</summary>
    internal long KeyLength => _entries.KeyLength + _large.KeyLength;

    /// <summary>The value kept under the key, or <paramref name="read"/>'s, which is then kept; while another request is reading it, its value.</summary>
    /// <param name="maxAge">How long a value is good for; for ever when null.</param>
    /// <exception cref="OperationCanceledException">The token was cancelled, while reading or while waiting for another request's read.</exception>
    internal T GetOrAdd<T>(string key, Func<T> read, TimeSpan? maxAge = null, CancellationToken cancellationToken = default) where T : notnull =>
        _entries.GetOrAdd(Shorten(key), read, maxAge, cancellationToken);

    /// <summary>As <see cref="GetOrAdd{T}"/>, for something large (an id set), of which only a few are kept.</summary>
    internal T GetOrAddLarge<T>(string key, Func<T> read, TimeSpan? maxAge = null, CancellationToken cancellationToken = default) where T : notnull =>
        _large.GetOrAdd(Shorten(key), read, maxAge, cancellationToken);

    /// <summary>Whether a large value is kept, read already; one being read isn't.</summary>
    internal bool HasLarge(string key, TimeSpan? maxAge = null) => _large.Has(Shorten(key), maxAge);

    /// <summary>Whether a value is kept under the key, read already; one being read isn't.</summary>
    internal bool Has(string key, TimeSpan? maxAge = null) => _entries.Has(Shorten(key), maxAge);

    /// <summary>Keeps a value under the key, in place of any.</summary>
    internal void Set<T>(string key, T value) where T : notnull => _entries.Set(Shorten(key), value);

    private static string Shorten(string key) =>
        key.Length <= MAX_KEY_LENGTH ? key : $"{key[..64]}#{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))}";

    /// <summary>Entries by key, each a value or a read under way, evicted least recently used first.</summary>
    private sealed class Store(int capacity)
    {
        private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
        private long _clock;
        private int _trimming;

        public int Count => _entries.Count;

        public long KeyLength => _entries.Keys.Sum(key => (long)key.Length);

        public T GetOrAdd<T>(string key, Func<T> read, TimeSpan? maxAge, CancellationToken cancellationToken) where T : notnull
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_entries.TryGetValue(key, out var entry))
                {
                    if (entry.IsStale(maxAge))
                    {
                        _entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
                        continue;
                    }

                    entry.LastUsed = Interlocked.Increment(ref _clock);
                    if (entry.Wait(cancellationToken) is T value)
                    {
                        return value;
                    }

                    // Its read stopped before it had a value: read it here instead.
                    continue;
                }

                var mine = new Entry(Interlocked.Increment(ref _clock));
                if (!_entries.TryAdd(key, mine))
                {
                    continue;
                }

                T fresh;
                try
                {
                    fresh = read();
                }
                catch
                {
                    _entries.TryRemove(new KeyValuePair<string, Entry>(key, mine));
                    mine.Abandon();
                    throw;
                }

                mine.Complete(fresh);
                Trim();
                return fresh;
            }
        }

        public bool Has(string key, TimeSpan? maxAge) => _entries.TryGetValue(key, out var entry) && entry.HasValue && !entry.IsStale(maxAge);

        public void Set(string key, object value)
        {
            var entry = new Entry(Interlocked.Increment(ref _clock));
            entry.Complete(value);
            _entries[key] = entry;
            Trim();
        }

        /// <summary>Past the capacity, down to seven eighths of it, so that trimming is rare: the values used least recently go first.</summary>
        private void Trim()
        {
            if (_entries.Count <= capacity || Interlocked.Exchange(ref _trimming, 1) == 1)
            {
                return;
            }

            try
            {
                var excess = _entries.Count - capacity + capacity / 8;
                foreach (var (key, entry) in _entries.Where(pair => pair.Value.HasValue).OrderBy(pair => pair.Value.LastUsed).Take(excess).ToList())
                {
                    _entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
                }
            }
            finally
            {
                Volatile.Write(ref _trimming, 0);
            }
        }
    }

    /// <summary>A value, or a read of it under way that others can wait for.</summary>
    private sealed class Entry(long lastUsed)
    {
        private readonly TaskCompletionSource<object?> _value = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private long _readAt;

        public long LastUsed { get; set; } = lastUsed;

        public bool HasValue => _value.Task is { IsCompletedSuccessfully: true, Result: not null };

        /// <summary>Read longer ago than <paramref name="maxAge"/>; a read under way never is.</summary>
        public bool IsStale(TimeSpan? maxAge) => maxAge is { } age && HasValue && Stopwatch.GetElapsedTime(Volatile.Read(ref _readAt)) > age;

        /// <summary>The value, once read; null when its read stopped without one.</summary>
        public object? Wait(CancellationToken cancellationToken)
        {
            _value.Task.Wait(cancellationToken);
            return _value.Task.Result;
        }

        public void Complete(object value)
        {
            Volatile.Write(ref _readAt, Stopwatch.GetTimestamp());
            _value.TrySetResult(value);
        }

        public void Abandon() => _value.TrySetResult(null);
    }
}
