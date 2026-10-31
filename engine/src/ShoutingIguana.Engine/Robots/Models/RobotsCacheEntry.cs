namespace ShoutingIguana.Engine.Robots.Models;

/// <summary>
/// One origin's robots.txt in the cache: a single load shared by every worker that asks, and, once a load has failed,
/// the time from which a fresh attempt may replace it.
/// </summary>
internal sealed class RobotsCacheEntry
{
    private readonly Lazy<Task<RobotsTxt>> _load;
    private long _retryFromTicks = long.MaxValue;

    public RobotsCacheEntry(Func<RobotsCacheEntry, Task<RobotsTxt>> load) => _load = new Lazy<Task<RobotsTxt>>(() => load(this));

    public Task<RobotsTxt> Load => _load.Value;

    /// <summary>When a fresh attempt may replace the failed load; null while the load hasn't failed.</summary>
    public DateTimeOffset? RetryAt => Volatile.Read(ref _retryFromTicks) is var ticks && ticks != long.MaxValue
        ? new DateTimeOffset(ticks, TimeSpan.Zero)
        : null;

    public bool CanRetry(DateTimeOffset now) => now.UtcTicks >= Volatile.Read(ref _retryFromTicks);

    /// <summary>Marks the load as failed: from <paramref name="time"/> on, the next request for the origin fetches again.</summary>
    public void RetryFrom(DateTimeOffset time) => Volatile.Write(ref _retryFromTicks, time.UtcTicks);
}
