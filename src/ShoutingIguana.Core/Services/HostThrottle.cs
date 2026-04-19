using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ShoutingIguana.Core.Services;

/// <summary>
/// Per-host request throttle. Allows up to <c>maxConcurrency</c> workers to hit the same
/// host simultaneously while stamping each acquire with <c>LastRequestUtc</c> so the next
/// worker waits at least <paramref name="minDelay"/> from the previous acquire. With
/// <c>maxConcurrency == 1</c> this degenerates to strict serialisation; with higher values
/// N fetches overlap but successive starts are still spaced by the rate limit.
/// </summary>
public sealed class HostThrottle(ILogger<HostThrottle> logger) : IDisposable
{
    private static readonly TimeSpan EvictionAge = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, HostEntry> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<IAsyncDisposable> AcquireAsync(string host, TimeSpan minDelay, int maxConcurrency, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("Host must be provided.", nameof(host));
        }

        if (maxConcurrency < 1)
        {
            maxConcurrency = 1;
        }

        var entry = _entries.GetOrAdd(host, _ => new HostEntry(maxConcurrency));
        await entry.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        TimeSpan wait;
        lock (entry.StampLock)
        {
            var now = DateTime.UtcNow;
            var scheduled = entry.LastRequestUtc + minDelay;
            wait = scheduled > now ? scheduled - now : TimeSpan.Zero;
            // Stamp the acquire time up-front so concurrent waiters space themselves
            // against the most-recent scheduled start, not the last completed fetch.
            entry.LastRequestUtc = scheduled > now ? scheduled : now;
        }

        try
        {
            if (wait > TimeSpan.Zero)
            {
                logger.LogDebug("Throttling {Host}: waiting {WaitMs}ms", host, wait.TotalMilliseconds);
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            entry.Gate.Release();
            throw;
        }

        EvictStale();
        return new Release(entry);
    }

    /// <summary>
    /// Drops all per-host entries so the next crawl starts with fresh semaphores sized
    /// from its own <c>ConcurrentRequests</c>. Safe to call only when no acquires are
    /// in flight — callers must invoke this before spawning crawl workers.
    /// </summary>
    public void ResetForNewCrawl()
    {
        foreach (var entry in _entries.Values)
        {
            entry.Gate.Dispose();
        }
        _entries.Clear();
    }

    private void EvictStale()
    {
        if (_entries.Count <= 64)
        {
            return;
        }

        var cutoff = DateTime.UtcNow - EvictionAge;
        foreach (var (host, entry) in _entries)
        {
            if (entry.LastRequestUtc < cutoff && entry.Gate.CurrentCount == entry.MaxConcurrency)
            {
                if (_entries.TryRemove(host, out var removed))
                {
                    removed.Gate.Dispose();
                }
            }
        }
    }

    public void Dispose()
    {
        foreach (var entry in _entries.Values)
        {
            entry.Gate.Dispose();
        }
        _entries.Clear();
    }

    private sealed class HostEntry(int maxConcurrency)
    {
        public SemaphoreSlim Gate { get; } = new(maxConcurrency, maxConcurrency);
        public int MaxConcurrency { get; } = maxConcurrency;
        public object StampLock { get; } = new();
        public DateTime LastRequestUtc { get; set; } = DateTime.MinValue;
    }

    private sealed class Release(HostEntry entry) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            entry.Gate.Release();
            return ValueTask.CompletedTask;
        }
    }
}
