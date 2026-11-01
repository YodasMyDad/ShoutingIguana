using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// Politeness for one host. Caps parallel requests and spaces request starts by the configured interval. Backs off
/// automatically when the host signals overload (429, 503, block pages; Retry-After is honoured) or keeps failing, then
/// eases back to full speed as successful responses return and as time passes. The spacing is worked out when a request
/// is due, from when the last one actually started and the backoff as it stands then, so requests queued behind a
/// backoff that has since eased aren't held to it.
/// </summary>
internal sealed class HostGate(int maxConcurrency, TimeSpan minimumInterval, TimeProvider timeProvider)
{
    /// <summary>URLs in a row, each after its retries, that got no response before an origin counts as unreachable.</summary>
    public const int UNREACHABLE_AFTER_URLS = 3;

    /// <summary>Failures in a row (timeouts, dropped connections, 502, 504) before they widen the spacing: one among successes is the URL's own.</summary>
    private const int FAILURES_BEFORE_BACKOFF = 2;

    private static readonly TimeSpan MIN_BACKOFF = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MAX_BACKOFF = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SETTLED_BACKOFF = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan MAX_PAUSE = TimeSpan.FromMinutes(2);

    /// <summary>The backoff halves every this long after the signal that last set it.</summary>
    private static readonly TimeSpan BACKOFF_HALF_LIFE = TimeSpan.FromMilliseconds(500);

    private readonly SemaphoreSlim _slots = new(maxConcurrency, maxConcurrency);
    private readonly Lock _sync = new();
    private readonly TimeSpan _configuredInterval = minimumInterval;
    private TimeSpan _minimumInterval = minimumInterval;
    private TimeSpan _backoff = TimeSpan.Zero;
    private DateTimeOffset _backoffSetAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastStart = DateTimeOffset.MinValue;
    private DateTimeOffset _pausedUntil = DateTimeOffset.MinValue;
    private int _failuresInARow;

    /// <summary>The spacing added to the interval as it stands now. Tests read it; the gate works it out afresh at every start.</summary>
    public TimeSpan CurrentBackoff
    {
        get
        {
            lock (_sync)
            {
                return BackoffAt(timeProvider.GetUtcNow());
            }
        }
    }

    /// <summary>What spaces this host's requests out now, beyond the crawl's settings, for its progress to say.</summary>
    public HostGateState ReadState()
    {
        lock (_sync)
        {
            var now = timeProvider.GetUtcNow();
            return new HostGateState(
                _minimumInterval > _configuredInterval ? _minimumInterval : null,
                BackoffAt(now),
                _pausedUntil > now ? _pausedUntil - now : TimeSpan.Zero);
        }
    }

    /// <summary>Holds every request back for <paramref name="duration"/>, e.g. while the host is unreachable.</summary>
    public void Pause(TimeSpan duration)
    {
        lock (_sync)
        {
            _pausedUntil = Max(_pausedUntil, timeProvider.GetUtcNow() + duration);
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
            // Worked out again after every wait: a backoff that eased, or a Retry-After that arrived, while this request
            // waited applies to it as it stands when it is due.
            while (!TryStart(out var wait))
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
            var now = timeProvider.GetUtcNow();
            var backoff = BackoffAt(now);
            switch (signal)
            {
                case HostSignal.Throttled:
                    SetBackoff(Min(Max(backoff * 2, MIN_BACKOFF), MAX_BACKOFF), now);
                    if (retryAfter is { } delay && delay > TimeSpan.Zero)
                    {
                        _pausedUntil = Max(_pausedUntil, now + Min(delay, MAX_PAUSE));
                    }

                    break;
                case HostSignal.Failed:
                    if (++_failuresInARow >= FAILURES_BEFORE_BACKOFF)
                    {
                        SetBackoff(Min(Max(backoff * 1.5, MIN_BACKOFF / 2), MAX_BACKOFF), now);
                    }

                    break;
                case HostSignal.Success:
                    _failuresInARow = 0;
                    SetBackoff(backoff * 0.9, now);
                    break;
            }
        }
    }

    internal void Release() => _slots.Release();

    /// <summary>Starts a request now if the spacing since the last start and any pause allow it; otherwise says how long to wait.</summary>
    private bool TryStart(out TimeSpan wait)
    {
        lock (_sync)
        {
            var now = timeProvider.GetUtcNow();
            var due = Max(_lastStart + _minimumInterval + BackoffAt(now), _pausedUntil);
            if (due <= now)
            {
                _lastStart = now;
                wait = TimeSpan.Zero;
                return true;
            }

            wait = due - now;
            return false;
        }
    }

    /// <summary>The backoff as it stands at <paramref name="now"/>: halved every <see cref="BACKOFF_HALF_LIFE"/> since it was set.</summary>
    private TimeSpan BackoffAt(DateTimeOffset now)
    {
        if (_backoff == TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var eased = _backoff * Math.Pow(0.5, (now - _backoffSetAt) / BACKOFF_HALF_LIFE);
        return eased < SETTLED_BACKOFF ? TimeSpan.Zero : eased;
    }

    private void SetBackoff(TimeSpan backoff, DateTimeOffset now)
    {
        _backoff = backoff < SETTLED_BACKOFF ? TimeSpan.Zero : backoff;
        _backoffSetAt = now;
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) => left > right ? left : right;

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left > right ? left : right;

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;
}
