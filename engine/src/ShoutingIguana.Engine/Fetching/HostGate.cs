using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// Politeness for one host. Caps parallel requests and spaces request starts by the configured interval.
/// Backs off automatically when the host signals overload (429, 503, block pages; Retry-After is
/// honoured), then decays back to full speed as successful responses return.
/// </summary>
internal sealed class HostGate(int maxConcurrency, TimeSpan minimumInterval, TimeProvider timeProvider)
{
    private static readonly TimeSpan MIN_BACKOFF = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MAX_BACKOFF = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SETTLED_BACKOFF = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan MAX_PAUSE = TimeSpan.FromMinutes(2);

    private readonly SemaphoreSlim _slots = new(maxConcurrency, maxConcurrency);
    private readonly Lock _sync = new();
    private TimeSpan _minimumInterval = minimumInterval;
    private TimeSpan _backoff = TimeSpan.Zero;
    private DateTimeOffset _nextStart = DateTimeOffset.MinValue;
    private DateTimeOffset _pausedUntil = DateTimeOffset.MinValue;

    public TimeSpan CurrentBackoff
    {
        get
        {
            lock (_sync)
            {
                return _backoff;
            }
        }
    }

    /// <summary>Applies a robots.txt Crawl-delay; never lowers an interval the user configured.</summary>
    public void RaiseMinimumInterval(TimeSpan interval)
    {
        lock (_sync)
        {
            if (interval > _minimumInterval)
            {
                _minimumInterval = interval;
            }
        }
    }

    public async ValueTask<HostGateLease> AcquireAsync(CancellationToken cancellationToken)
    {
        await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TimeSpan wait;
            lock (_sync)
            {
                var now = timeProvider.GetUtcNow();
                var start = Max(now, Max(_nextStart, _pausedUntil));
                _nextStart = start + _minimumInterval + _backoff;
                wait = start - now;
            }

            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, timeProvider, cancellationToken).ConfigureAwait(false);
            }

            return new HostGateLease(this);
        }
        catch
        {
            _slots.Release();
            throw;
        }
    }

    public void Report(HostSignal signal, TimeSpan? retryAfter = null)
    {
        lock (_sync)
        {
            switch (signal)
            {
                case HostSignal.Throttled:
                    _backoff = Min(Max(_backoff * 2, MIN_BACKOFF), MAX_BACKOFF);
                    if (retryAfter is { } delay && delay > TimeSpan.Zero)
                    {
                        _pausedUntil = Max(_pausedUntil, timeProvider.GetUtcNow() + Min(delay, MAX_PAUSE));
                    }

                    break;
                case HostSignal.Failed:
                    _backoff = Min(Max(_backoff * 1.5, MIN_BACKOFF / 2), MAX_BACKOFF);
                    break;
                case HostSignal.Success:
                    _backoff = _backoff < SETTLED_BACKOFF ? TimeSpan.Zero : _backoff * 0.9;
                    break;
            }
        }
    }

    internal void Release() => _slots.Release();

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) => left > right ? left : right;

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left > right ? left : right;

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;
}
