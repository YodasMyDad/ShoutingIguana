using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ShoutingIguana.Core.Services;

/// <summary>
/// Per-host request throttle. Ensures a minimum gap between requests to the same host
/// regardless of how many workers are running, by serialising access through a per-host
/// <see cref="SemaphoreSlim"/> and stamping <c>LastRequestUtc</c> after each acquisition.
/// </summary>
public sealed class HostThrottle(ILogger<HostThrottle> logger) : IDisposable
{
    private static readonly TimeSpan EvictionAge = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, HostEntry> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<IAsyncDisposable> AcquireAsync(string host, TimeSpan minDelay, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("Host must be provided.", nameof(host));
        }

        var entry = _entries.GetOrAdd(host, static _ => new HostEntry());
        await entry.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var wait = entry.LastRequestUtc + minDelay - DateTime.UtcNow;
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

    private void EvictStale()
    {
        if (_entries.Count <= 64)
        {
            return;
        }

        var cutoff = DateTime.UtcNow - EvictionAge;
        foreach (var (host, entry) in _entries)
        {
            if (entry.LastRequestUtc < cutoff && entry.Gate.CurrentCount == 1)
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

    private sealed class HostEntry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public DateTime LastRequestUtc { get; set; } = DateTime.MinValue;
    }

    private sealed class Release(HostEntry entry) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            entry.LastRequestUtc = DateTime.UtcNow;
            entry.Gate.Release();
            return ValueTask.CompletedTask;
        }
    }
}
